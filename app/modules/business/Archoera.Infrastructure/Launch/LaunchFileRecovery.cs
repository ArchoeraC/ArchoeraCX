using System.Security.Cryptography;
using System.Text.Json;
using Archoera.Domain.Instances;
using Archoera.Domain.Mojang;

namespace Archoera.Infrastructure.Launch;

/// <summary>
/// Recovers missing launch files from other local game roots (another
/// instance's private libraries, a second game folder, or the shared root)
/// before the caller falls back to downloading them.
///
/// Every copy is verified against the SHA-1 and size from the version metadata;
/// a file that exists but is corrupt is not copied over (the download repair
/// verifies and replaces it if needed).
/// </summary>
internal static class LaunchFileRecovery
{
    /// <summary>Returns the number of files copied into place.</summary>
    public static int TryRecover(
        GameInstanceRecord instance,
        MojangVersion version,
        LaunchPaths paths,
        IReadOnlyList<string> roots)
    {
        if (roots.Count == 0) return 0;

        var platform = MojangPlatform.Current();
        var osKey = platform.OsName;
        var repaired = 0;

        foreach (var library in version.Libraries)
        {
            if (!MojangRuleEvaluator.IsAllowed(library.Rules, platform)) continue;

            var artifact = library.Downloads?.Artifact;
            var relative = LaunchPlanBuilder.LibraryRelativePath(library);
            if (artifact is not null && relative is not null)
            {
                var target = Path.Combine(paths.LibrariesRoot, relative);
                if (IsWithin(paths.LibrariesRoot, target))
                {
                    repaired += CopyFile(
                        target,
                        Path.Combine("libraries", relative),
                        artifact,
                        roots);
                }
            }

            var native = LaunchPlanBuilder.NativeDownload(library, platform, osKey);
            if (native is null) continue;
            var nativeRelative = LaunchPlanBuilder.NativeRelativePath(
                library,
                platform,
                osKey,
                native);
            if (nativeRelative is null) continue;
            var nativeTarget = Path.Combine(paths.LibrariesRoot, nativeRelative);
            if (!IsWithin(paths.LibrariesRoot, nativeTarget)) continue;
            repaired += CopyFile(
                nativeTarget,
                Path.Combine("libraries", nativeRelative),
                native,
                roots);
        }

        repaired += RecoverClientJar(instance, version, paths, roots);
        repaired += RecoverAssets(version, paths, roots);
        return repaired;
    }

    private static int RecoverClientJar(
        GameInstanceRecord instance,
        MojangVersion version,
        LaunchPaths paths,
        IReadOnlyList<string> roots)
    {
        var client = version.Downloads.Client;
        if (client is null || !IsPlainName(version.Id)) return 0;
        if (HasClientJar(instance, version, paths)) return 0;

        var relative = Path.Combine("versions", version.Id, $"{version.Id}.jar");
        var target = Path.Combine(paths.SharedRoot, relative);
        var gameRelative = Path.Combine(
            "versions",
            instance.GameVersion,
            $"{instance.GameVersion}.jar");
        return CopyFile(target, relative, client, roots, gameRelative);
    }

    private static bool HasClientJar(
        GameInstanceRecord instance,
        MojangVersion version,
        LaunchPaths paths)
    {
        var folder = Path.GetFileName(paths.InstanceRoot);
        return new[]
            {
                Path.Combine(paths.InstanceRoot, $"{folder}.jar"),
                Path.Combine(paths.InstanceRoot, $"{version.Id}.jar"),
                Path.Combine(
                    paths.SharedRoot,
                    "versions",
                    version.Id,
                    $"{version.Id}.jar"),
            }
            .Distinct(StringComparer.Ordinal)
            .Any(File.Exists);
    }

    private static int RecoverAssets(
        MojangVersion version,
        LaunchPaths paths,
        IReadOnlyList<string> roots)
    {
        var index = version.AssetIndex;
        var assetIndex = index?.Id ?? version.Assets;
        if (index is null ||
            string.IsNullOrWhiteSpace(assetIndex) ||
            !IsPlainName(assetIndex))
        {
            return 0;
        }

        var repaired = 0;
        var indexRelative = Path.Combine("assets", "indexes", $"{assetIndex}.json");
        var indexTarget = Path.Combine(paths.AssetsRoot, "indexes", $"{assetIndex}.json");
        if (!File.Exists(indexTarget))
        {
            var descriptor = new MojangDownload(index.Url, index.Sha1, index.Size);
            repaired += CopyFile(indexTarget, indexRelative, descriptor, roots);
        }

        if (!File.Exists(indexTarget)) return repaired;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(indexTarget));
            if (!document.RootElement.TryGetProperty("objects", out var objects) ||
                objects.ValueKind != JsonValueKind.Object)
            {
                return repaired;
            }

            foreach (var item in objects.EnumerateObject())
            {
                if (item.Value.ValueKind != JsonValueKind.Object ||
                    !item.Value.TryGetProperty("hash", out var hashElement) ||
                    hashElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var hash = hashElement.GetString() ?? string.Empty;
                if (!IsAssetHash(hash)) continue;
                long? size = item.Value.TryGetProperty("size", out var sizeElement) &&
                             sizeElement.ValueKind == JsonValueKind.Number &&
                             sizeElement.TryGetInt64(out var declared)
                    ? declared
                    : null;

                var relative = Path.Combine("assets", "objects", hash[..2], hash);
                var target = Path.Combine(paths.AssetsRoot, "objects", hash[..2], hash);
                if (File.Exists(target)) continue;
                repaired += CopyFile(
                    target,
                    relative,
                    new MojangDownload("local", hash, size),
                    roots);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // The download repair handles an unreadable index.
        }

        return repaired;
    }

    /// <summary>
    /// Copies <paramref name="sourceRelative"/> from the first root that has a
    /// verified file. <paramref name="alternateSourceRelative"/> covers vanilla
    /// client jars stored under the game version instead of the profile id.
    /// </summary>
    private static int CopyFile(
        string target,
        string sourceRelative,
        MojangDownload download,
        IReadOnlyList<string> roots,
        string? alternateSourceRelative = null)
    {
        try
        {
            if (File.Exists(target)) return 0;
            if (!IsSafeRelative(sourceRelative)) return 0;

            foreach (var root in roots)
            {
                foreach (var relative in RelativeCandidates(
                             sourceRelative,
                             alternateSourceRelative))
                {
                    var source = Path.Combine(root, relative);
                    if (CopyVerified(source, target, download)) return 1;
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Best effort; the caller can download the file.
        }

        return 0;
    }

    private static IEnumerable<string> RelativeCandidates(
        string relative,
        string? alternate)
    {
        yield return relative;
        if (!string.IsNullOrWhiteSpace(alternate) &&
            !string.Equals(alternate, relative, StringComparison.Ordinal))
        {
            yield return alternate;
        }
    }

    private static bool CopyVerified(
        string source,
        string target,
        MojangDownload download)
    {
        if (!File.Exists(source)) return false;

        var info = new FileInfo(source);
        if (download.Size is { } size && info.Length != size) return false;
        if (!string.IsNullOrWhiteSpace(download.Sha1) &&
            !HashMatches(source, download.Sha1))
        {
            return false;
        }

        // Require at least one integrity hint; a bare Maven coordinate without
        // size/hash is not enough to trust another root's copy.
        if (download.Size is null && string.IsNullOrWhiteSpace(download.Sha1))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(target);
        if (string.IsNullOrWhiteSpace(directory)) return false;
        Directory.CreateDirectory(directory);
        var temporary = target + ".tmp";
        try
        {
            File.Copy(source, temporary, overwrite: true);
            File.Move(temporary, target, overwrite: true);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            TryDelete(temporary);
            return false;
        }
    }

    private static bool HashMatches(string path, string expected)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(SHA1.HashData(stream));
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsPlainName(string value)
        => !string.IsNullOrWhiteSpace(value) &&
           Path.GetFileName(value) == value &&
           !value.Contains("..", StringComparison.Ordinal);

    private static bool IsAssetHash(string value)
        => value.Length == 40 && value.All(Uri.IsHexDigit);

    /// <summary>
    /// Rejects absolute paths and traversal. The caller supplies metadata-derived
    /// relatives, so this is a last line of defence.
    /// </summary>
    private static bool IsSafeRelative(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value)) return false;
        var segments = value.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        return segments.All(segment => segment is not ("." or ".."));
    }

    private static bool IsWithin(string root, string path)
    {
        try
        {
            var rootFull = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            var pathFull = Path.GetFullPath(path);
            return pathFull.StartsWith(
                rootFull,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // best effort
        }
    }
}
