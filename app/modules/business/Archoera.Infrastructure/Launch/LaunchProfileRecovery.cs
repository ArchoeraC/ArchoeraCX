using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Archoera.Application.Mojang;
using Archoera.Domain.Instances;
using Archoera.Domain.Mojang;

namespace Archoera.Infrastructure.Launch;

/// <summary>
/// Rebuilds a missing or unmerged launch profile from files that already exist
/// on disk. Launch repair used to go straight to the network; for an instance
/// imported from another launcher (or one whose profile JSON was lost) the
/// loader profile and base version are usually still in <c>versions/</c>, so a
/// local merge is enough. Download repair remains the fallback when this fails.
/// </summary>
internal static class LaunchProfileRecovery
{
    private static readonly VersionMergeService Merge = new();

    /// <summary>
    /// True when the instance profile is missing, unreadable, or still carries
    /// <c>inheritsFrom</c>. A merged profile is left untouched.
    /// </summary>
    public static bool NeedsRecovery(string path)
    {
        if (!File.Exists(path)) return true;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("inheritsFrom", out var parent) &&
                   parent.ValueKind == JsonValueKind.String &&
                   !string.IsNullOrWhiteSpace(parent.GetString());
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return true;
        }
    }

    /// <summary>
    /// Writes <c>&lt;instance&gt;/&lt;folder&gt;.json</c> from a local sibling
    /// profile, merging an <c>inheritsFrom</c> chain when needed.
    /// </summary>
    public static async Task<bool> TryRecoverAsync(
        GameInstanceRecord instance,
        LaunchPaths paths,
        CancellationToken cancellationToken = default)
    {
        var folder = Path.GetFileName(paths.InstanceRoot);
        var target = Path.Combine(paths.InstanceRoot, $"{folder}.json");

        // The instance profile exists but still inherits: merge it in place.
        if (File.Exists(target))
        {
            if (!LooksLikeVersion(target)) return false;
            var rootId = ReadId(target);
            if (rootId is null) return false;
            try
            {
                var merged = await MergeAsync(rootId, paths, folder, cancellationToken)
                    .ConfigureAwait(false);
                return merged is not null && WriteProfile(target, merged, paths, folder);
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                return false;
            }
        }

        foreach (var candidate in CandidateProfiles(paths, folder))
        {
            var id = ReadId(candidate);
            if (id is null || !Matches(candidate, id, instance, folder)) continue;
            try
            {
                var merged = await MergeAsync(id, paths, folder, cancellationToken)
                    .ConfigureAwait(false);
                if (merged is null) continue;
                if (WriteProfile(target, merged, paths, folder)) return true;
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                // Try the next candidate.
            }
        }

        return false;
    }

    private static async Task<string?> MergeAsync(
        string rootId,
        LaunchPaths paths,
        string folder,
        CancellationToken cancellationToken)
    {
        var effective = await Merge
            .MergeAsync(
                rootId,
                (id, _) => Task.FromResult(LocalLayer(id, paths, folder)),
                cancellationToken)
            .ConfigureAwait(false);
        return Encoding.UTF8.GetString(effective.Json);
    }

    private static bool IsRecoverable(Exception exception)
        => exception is VersionMergeException
            or IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException
            or InvalidOperationException;

    /// <summary>
    /// Resolves one layer from the instance, its siblings, or the shared
    /// versions directory. A file whose internal id differs from the request is
    /// ignored: <see cref="VersionChainResolver"/> treats that as a mismatch.
    /// </summary>
    private static VersionLayer? LocalLayer(string id, LaunchPaths paths, string folder)
    {
        foreach (var path in new[]
                 {
                     Path.Combine(paths.InstanceRoot, $"{id}.json"),
                     Path.Combine(paths.InstanceRoot, $"{folder}.json"),
                     Path.Combine(paths.InstanceRoot, "versions", id, $"{id}.json"),
                     Path.Combine(paths.SharedRoot, "versions", id, $"{id}.json"),
                 }.Distinct(StringComparer.Ordinal))
        {
            if (!File.Exists(path)) continue;
            if (!string.Equals(ReadId(path), id, StringComparison.Ordinal)) continue;
            try
            {
                return new VersionLayer(id, File.ReadAllText(path), path);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateProfiles(LaunchPaths paths, string folder)
    {
        foreach (var root in new[] { paths.SharedRoot, paths.InstanceRoot }
                     .Distinct(StringComparer.Ordinal))
        {
            var versions = Path.Combine(root, "versions");
            if (!Directory.Exists(versions)) continue;
            foreach (var directory in Directory.EnumerateDirectories(versions))
            {
                var name = Path.GetFileName(directory);
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (LaunchPaths.LooksLikeInstanceDirectory(directory)) continue;
                var file = Path.Combine(directory, $"{name}.json");
                if (File.Exists(file) && LooksLikeVersion(file)) yield return file;
            }
        }

        // A profile may also sit next to the instance under another id.
        if (!Directory.Exists(paths.InstanceRoot)) yield break;
        foreach (var file in Directory.EnumerateFiles(
                     paths.InstanceRoot,
                     "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            if (string.Equals(
                    Path.GetFileNameWithoutExtension(file),
                    folder,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (LooksLikeVersion(file)) yield return file;
        }
    }

    /// <summary>
    /// Matches the instance's own folder, the same game version for vanilla,
    /// or the exact loader build for a loader instance.
    /// </summary>
    private static bool Matches(
        string path,
        string id,
        GameInstanceRecord instance,
        string folder)
    {
        if (string.Equals(id, folder, StringComparison.OrdinalIgnoreCase)) return true;

        MojangVersion? version;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            version = MojangJson.ParseVersion(document.RootElement);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or
            ArgumentException or MojangException)
        {
            return false;
        }

        return LoaderProfileIdentity.Matches(instance, version);
    }

    private static string? ReadId(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("id", out var id) &&
                   id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static bool LooksLikeVersion(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                   root.TryGetProperty("mainClass", out _) &&
                   (root.TryGetProperty("libraries", out _) ||
                    root.TryGetProperty("arguments", out _));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes the merged profile atomically. The id follows the client jar the
    /// instance already has: pack instances keep <c>&lt;folder&gt;.jar</c> and
    /// loader args such as <c>-DignoreList=client-extra,${version_name}.jar</c>
    /// must name that jar.
    /// </summary>
    private static bool WriteProfile(
        string target,
        string json,
        LaunchPaths paths,
        string folder)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root) return false;
            var clientJar = Path.Combine(paths.InstanceRoot, $"{folder}.jar");
            if (File.Exists(clientJar)) root["id"] = folder;
            var temporary = target + ".tmp";
            File.WriteAllText(
                temporary,
                root.ToJsonString(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, target, overwrite: true);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }
}
