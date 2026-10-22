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

    /// <summary>Matches the same game version and loader family.</summary>
    private static bool Matches(
        string path,
        string id,
        GameInstanceRecord instance,
        string folder)
    {
        if (string.Equals(id, folder, StringComparison.OrdinalIgnoreCase)) return true;

        if (instance.Loader.Equals("vanilla", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(
                id,
                instance.GameVersion,
                StringComparison.OrdinalIgnoreCase);
        }

        var loaderMatches =
            id.Contains(instance.Loader, StringComparison.OrdinalIgnoreCase) ||
            (instance.Loader.Equals("neoforge", StringComparison.OrdinalIgnoreCase) &&
             id.Contains("forge", StringComparison.OrdinalIgnoreCase));
        if (!loaderMatches) return false;

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        // Version ids can omit the game version (NeoForge's installer profile
        // is "neoforge-<loader>"), but the loader version must match; a Forge
        // profile must never stand in for NeoForge, or vice versa.
        if (!string.IsNullOrWhiteSpace(instance.LoaderVersion) &&
            !id.Contains(instance.LoaderVersion, StringComparison.OrdinalIgnoreCase) &&
            !text.Contains(instance.LoaderVersion, StringComparison.Ordinal))
        {
            return false;
        }

        if (id.Contains(instance.GameVersion, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // NeoForge's installer profile is id "neoforge-<loader>"; the game
        // version only appears in inheritsFrom or --fml.mcVersion.
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.TryGetProperty("inheritsFrom", out var parent) &&
                parent.ValueKind == JsonValueKind.String &&
                string.Equals(
                    parent.GetString(),
                    instance.GameVersion,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!root.TryGetProperty("arguments", out var arguments) ||
                !arguments.TryGetProperty("game", out var game) ||
                game.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var values = game.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .ToList();
            for (var index = 0; index + 1 < values.Count; index++)
            {
                if (string.Equals(values[index], "--fml.mcVersion", StringComparison.Ordinal) &&
                    string.Equals(
                        values[index + 1],
                        instance.GameVersion,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }

        return false;
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
