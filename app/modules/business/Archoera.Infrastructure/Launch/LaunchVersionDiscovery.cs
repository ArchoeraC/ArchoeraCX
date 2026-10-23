using System.Text.Json;
using Archoera.Application.Mojang;
using Archoera.Domain.Instances;
using Archoera.Domain.Mojang;

namespace Archoera.Infrastructure.Launch;

/// <summary>
/// Locates the launchable version profile for an instance across the shared
/// install roots, loader profile naming schemes and the heuristic fallback.
/// Kept static and side-effect free: it only reads and validates profiles.
/// </summary>
internal static class LaunchVersionDiscovery
{
    internal static MojangVersion LoadVersion(
        GameInstanceRecord instance,
        LaunchPaths paths)
    {
        var directoryName = Path.GetFileName(paths.InstanceRoot);
        var isLoaderInstance = !string.Equals(
            instance.Loader,
            "vanilla",
            StringComparison.OrdinalIgnoreCase);

        // The instance's own profile wins. Every launcher (Mojang, Fabric,
        // Quilt, Forge, NeoForge) writes exactly one profile JSON per version
        // directory and names it after that directory, so the instance folder
        // is authoritative even when its id carries no game version or loader
        // name (e.g. a modpack called "AutumnTown").
        //
        // The one exception: a loader instance whose folder is named after the
        // vanilla version (for example versions/1.21.1) can own the plain
        // vanilla profile while its loader profile lives in a sibling
        // directory. That base profile must not be launched on its own.
        foreach (var own in new[]
                 {
                     Path.Combine(paths.InstanceRoot, $"{directoryName}.json"),
                     Path.Combine(
                         paths.SharedRoot,
                         "versions",
                         directoryName,
                         $"{directoryName}.json"),
                 })
        {
            var parsed = TryReadVersion(own);
            if (parsed is null) continue;
            if (isLoaderInstance && IsVanillaBase(parsed, instance)) continue;
            EnsureMerged(parsed);
            return parsed;
        }

        // Loader profiles are named by the installer, not by the instance
        // folder: Forge writes "<game>-forge-<version>", NeoForge writes
        // "neoforge-<version>" (no game version), Fabric writes
        // "fabric-loader-<version>-<game>". Ask for those ids directly before
        // falling back to the heuristic scan below.
        if (isLoaderInstance)
        {
            var installRoot = LaunchPaths.ResolveInstallRoot(paths.InstanceRoot);
            foreach (var id in LoaderProfileIds(instance))
            {
                foreach (var baseRoot in new[]
                         {
                             installRoot,
                             paths.SharedRoot,
                             paths.InstanceRoot,
                         })
                {
                    var parsed = TryReadVersion(
                        Path.Combine(baseRoot, "versions", id, $"{id}.json"));
                    if (parsed is null) continue;
                    if (IsVanillaBase(parsed, instance)) continue;
                    EnsureMerged(parsed);
                    return parsed;
                }
            }
        }

        // Fallback for instances created from a plain version id (the profile
        // keeps the game/loader id rather than the folder name).
        var candidates = new List<string>
        {
            Path.Combine(paths.InstanceRoot, $"{instance.GameVersion}.json"),
            Path.Combine(
                paths.SharedRoot,
                "versions",
                instance.GameVersion,
                $"{instance.GameVersion}.json"),
        };

        // Also accept a base profile found under versions/. Only ids related to
        // this instance's game version are considered: picking another
        // instance's profile would launch the wrong game.
        foreach (var versionsRoot in new[]
                 {
                     Path.Combine(paths.SharedRoot, "versions"),
                     Path.Combine(paths.InstanceRoot, "versions"),
                 })
        {
            if (!Directory.Exists(versionsRoot)) continue;
            foreach (var directory in Directory.EnumerateDirectories(versionsRoot))
            {
                var name = Path.GetFileName(directory);
                if (string.IsNullOrWhiteSpace(name)) continue;
                // Do not borrow a real instance's merged profile just because
                // its custom name contains this game and loader.
                if (LaunchPaths.LooksLikeInstanceDirectory(directory)) continue;
                candidates.Add(Path.Combine(directory, $"{name}.json"));
            }
        }

        var matches = new List<(MojangVersion Version, int Score, DateTime Modified)>();
        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var version = TryReadVersion(path);
            if (version is null) continue;

            var score = 0;
            if (string.Equals(version.Id, instance.GameVersion, StringComparison.OrdinalIgnoreCase))
            {
                // A vanilla base profile: usable for vanilla instances and as a
                // last resort for a loader instance without a merged profile.
                score += isLoaderInstance ? 10 : 80;
            }
            else if (isLoaderInstance &&
                     version.Id.Contains(instance.Loader, StringComparison.OrdinalIgnoreCase))
            {
                // Standard loader profile ids: "1.21.1-forge-47.2.0",
                // "fabric-loader-0.16.14-1.21.1", and NeoForge's
                // "neoforge-21.1.264" (which omits the game version).
                var hasGame = version.Id.Contains(
                    instance.GameVersion,
                    StringComparison.OrdinalIgnoreCase);
                var hasLoaderVersion =
                    !string.IsNullOrWhiteSpace(instance.LoaderVersion) &&
                    version.Id.Contains(
                        instance.LoaderVersion,
                        StringComparison.OrdinalIgnoreCase);
                if (!hasLoaderVersion &&
                    MentionsLoaderVersion(version.Id, instance.Loader))
                {
                    // "neoforge-21.1.248" when the instance wants 21.1.264: a
                    // profile for another loader build must not be used. Ids
                    // such as "1.21.1-neoforge" carry no version and remain
                    // acceptable as a last resort.
                    continue;
                }

                if (hasGame)
                {
                    score += version.Id.StartsWith(
                        instance.GameVersion + "-",
                        StringComparison.OrdinalIgnoreCase)
                        ? 60
                        : 30;
                }
                else
                {
                    // No game version in the id (NeoForge style): still a valid
                    // loader profile, but behind ids that name both.
                    score += 20;
                }

                if (!string.IsNullOrWhiteSpace(instance.LoaderVersion) &&
                    version.Id.Contains(
                        instance.LoaderVersion,
                        StringComparison.OrdinalIgnoreCase))
                {
                    score += 10;
                }
            }

            if (score == 0) continue;
            matches.Add((version, score, File.GetLastWriteTimeUtc(path)));
        }

        if (matches.Count == 0)
        {
            throw new LaunchException(
                "VERSION_JSON_MISSING",
                $"no version JSON found for {instance.GameVersion} in {paths.SharedRoot}");
        }

        var selected = matches
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Modified)
            .First()
            .Version;

        // A loader instance must never launch the plain game: the vanilla base
        // profile has no loader libraries, so the game would start unmodded.
        // Report the missing install so launch can repair it (or tell the user
        // to install the loader) instead of silently booting vanilla.
        if (isLoaderInstance && IsVanillaBase(selected, instance))
        {
            throw new LaunchException(
                "LOADER_NOT_INSTALLED",
                $"{instance.Loader} {instance.LoaderVersion} is not installed for instance {instance.Name}");
        }

        EnsureMerged(selected);
        return selected;
    }

    /// <summary>
    /// True when the id names a concrete loader build, for example
    /// <c>neoforge-21.1.248</c> or <c>1.21.8-forge-58.1.26</c>.
    /// </summary>
    private static bool MentionsLoaderVersion(string id, string loader)
    {
        var marker = loader + "-";
        var index = id.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return false;
        var rest = index + marker.Length;
        return rest < id.Length && char.IsAsciiDigit(id[rest]);
    }

    /// <summary>
    /// Version ids the loader installer uses for this instance, most specific
    /// first. NeoForge omits the game version; legacy NeoForge (1.20.1) used
    /// the Forge coordinates.
    /// </summary>
    internal static IReadOnlyList<string> LoaderProfileIds(GameInstanceRecord instance)
    {
        var game = instance.GameVersion;
        var loader = instance.LoaderVersion;
        if (string.IsNullOrWhiteSpace(loader)) return [];

        var loaderKind = instance.Loader.Trim().ToLowerInvariant();
        return loaderKind switch
        {
            "forge" =>
            [
                $"{game}-forge-{loader}",
                $"forge-{loader}",
            ],
            "neoforge" =>
            [
                $"neoforge-{loader}",
                $"{game}-neoforge-{loader}",
                $"{game}-forge-{loader}",
            ],
            "fabric" => [$"fabric-loader-{loader}-{game}"],
            "quilt" => [$"quilt-loader-{loader}-{game}"],
            _ => [],
        };
    }

    /// <summary>
    /// Reads a version profile, returning null when the file is missing or is
    /// not a launchable profile (game data such as <c>usercache.json</c>, a
    /// loader cache, or a truncated download).
    /// </summary>
    private static MojangVersion? TryReadVersion(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return MojangJson.ParseVersion(document.RootElement);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or ArgumentException
                or MojangException)
        {
            return null;
        }
    }

    /// <summary>
    /// True when a profile is just the vanilla game profile for this instance,
    /// i.e. it carries the game version id and none of the loader's libraries.
    /// </summary>
    private static bool IsVanillaBase(MojangVersion version, GameInstanceRecord instance)
    {
        if (!string.Equals(version.Id, instance.GameVersion, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !version.Libraries.Any(library =>
            library.Name.Contains(instance.Loader, StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsureMerged(MojangVersion version)
    {
        if (!string.IsNullOrWhiteSpace(version.InheritsFrom))
        {
            throw new LaunchException(
                "VERSION_NOT_MERGED",
                $"version '{version.Id}' still inherits from '{version.InheritsFrom}'; install/merge it first");
        }
    }
}
