using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Archoera.Domain;
using Archoera.Domain.Instances;
using Archoera.Domain.Loaders;

namespace Archoera.Infrastructure.Instances;

/// <summary>
/// Discovers instances created by ArchoeraCX and common launcher layouts.
/// This reader is intentionally non-destructive: it never creates
/// <c>.archoera/instance.json</c> for imported folders. The SQLite index stores
/// the discovered metadata until the user edits it.
/// </summary>
internal static class InstanceDiscovery
{
    private static StringComparer PathComparer => PathSemantics.Comparer;

    private static StringComparison PathComparison => PathSemantics.Comparison;

    private static readonly string[] IgnoredRootDirectories =
    [
        "assets", "libraries", "versions", "logs", "crash-reports", "resourcepacks",
        "shaderpacks", "saves", "mods", "config", "defaultconfigs",
        "natives", "downloads", "cache", "screenshots", "replay_recordings",
    ];

    /// <summary>
    /// Version ids a loader installer creates: Forge writes
    /// <c>1.21.1-forge-52.0.1</c> (or <c>forge-52.0.1</c> on older installers),
    /// NeoForge writes <c>neoforge-21.1.176</c>, Fabric
    /// <c>fabric-loader-0.16.9-1.21.1</c> and Quilt
    /// <c>quilt-loader-0.9.1-1.21.1</c>. Those folders hold a launch profile
    /// shared by every instance of that loader build, so they sit in
    /// <c>versions/</c> next to real instances without being one. Matches the
    /// hyphenated ids only; PCL's <c>1.21.1-Forge_52.0.1</c> layout is a real
    /// instance and stays.
    /// </summary>
    private static readonly Regex LoaderProfileId = new(
        @"^(?:(?:\d[^/\\]*-)?(?:forge|neoforge)|fabric-loader|quilt-loader)-\d",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// True when <paramref name="directory"/> is a shared loader profile rather
    /// than an instance folder: the name is an installer version id and the
    /// folder carries no instance data of its own. A profile that gained mods,
    /// configs or <c>.archoera/instance.json</c> is treated as an instance.
    /// </summary>
    internal static bool IsLoaderProfileDirectory(string directory)
    {
        string name;
        try
        {
            name = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (name.Length == 0 || !LoaderProfileId.IsMatch(name)) return false;
        if (!Directory.Exists(directory)) return false;
        if (Directory.Exists(Path.Combine(directory, ".archoera"))) return false;

        // A shared profile only ever holds the version JSON and its jar.
        // Anything a real instance accumulates means the user turned the
        // folder into one, so keep it.
        foreach (var marker in new[] { "logs", "natives", "crash-reports" })
        {
            if (Directory.Exists(Path.Combine(directory, marker))) return false;
        }

        return !HasInstanceMarkers(directory);
    }

    public static IReadOnlyList<GameInstanceRecord> Discover(
        string root,
        CancellationToken cancellationToken = default)
    {
        string rootFull;
        try
        {
            rootFull = Normalize(root);
        }
        catch
        {
            return [];
        }

        if (!Directory.Exists(rootFull))
        {
            return [];
        }

        var profiles = ReadLxProfiles(rootFull);
        var results = new List<GameInstanceRecord>();
        var seen = new HashSet<string>(PathComparer);

        void Add(string? directory, LauncherProfile? profile = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(directory)) return;
            string full;
            try
            {
                full = Normalize(directory);
            }
            catch
            {
                return;
            }

            if (!Directory.Exists(full) || !seen.Add(full)) return;
            var record = TryRead(full, profile);
            if (record is not null) results.Add(record);
        }

        // LauncherX / SJMCL profiles are authoritative for their game roots.
        foreach (var profile in profiles.Values)
        {
            if (string.IsNullOrWhiteSpace(profile.GameDir) ||
                !IsWithin(profile.GameDir, rootFull)) continue;
            Add(profile.GameDir, profile);
        }

        Add(rootFull);

        foreach (var directory in SafeDirectories(Path.Combine(rootFull, "versions")))
        {
            // Installing a loader commits its profile to versions/<id> so every
            // instance of that loader build can launch from it. That folder is
            // not an instance: listing it produced a "neoforge-21.1.176" entry
            // with an unknown game version right after a pack install.
            if (IsLoaderProfileDirectory(directory)) continue;
            Add(directory);
        }

        foreach (var directory in SafeDirectories(Path.Combine(rootFull, "instances")))
        {
            Add(directory);
        }

        foreach (var directory in SafeDirectories(rootFull))
        {
            var name = Path.GetFileName(directory);
            if (IgnoredRootDirectories.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            Add(directory);
        }

        return results;
    }

    private static GameInstanceRecord? TryRead(string directory, LauncherProfile? profile)
    {
        if (TryReadArchoera(directory) is { } archoera)
        {
            return archoera with
            {
                LastPlayedAt = profile?.LastUsed ?? archoera.LastPlayedAt,
            };
        }

        var sjmcl = TryReadSjmcl(directory);
        var mmc = TryReadMmc(directory);
        var modrinth = TryReadModrinth(directory);
        var versionJson = TryReadVersionJson(directory, profile);

        var name = sjmcl?.Name ?? mmc?.Name ?? modrinth?.Name ?? profile?.Name;
        var gameVersion = sjmcl?.GameVersion ?? mmc?.GameVersion ?? modrinth?.GameVersion ?? versionJson?.GameVersion;
        var loader = sjmcl?.Loader ?? mmc?.Loader ?? modrinth?.Loader ?? versionJson?.Loader ?? "vanilla";
        var loaderVersion = LoaderVersionText.Normalize(
            loader,
            gameVersion,
            sjmcl?.LoaderVersion ?? mmc?.LoaderVersion ?? modrinth?.LoaderVersion ?? versionJson?.LoaderVersion);

        if (string.IsNullOrWhiteSpace(name))
        {
            name = Path.GetFileName(directory);
        }

        if (string.IsNullOrWhiteSpace(gameVersion))
        {
            gameVersion = "unknown";
        }

        if (!HasInstanceMarkers(directory) &&
            sjmcl is null && mmc is null && modrinth is null && versionJson is null)
        {
            return null;
        }

        var createdAt = profile?.Created
                        ?? Directory.GetCreationTimeUtc(directory);
        var updatedAt = profile?.LastUsed
                        ?? Directory.GetLastWriteTimeUtc(directory);
        if (createdAt.Year < 2000) createdAt = DateTimeOffset.UtcNow;
        if (updatedAt.Year < 2000) updatedAt = createdAt;

        return new GameInstanceRecord(
            StableId(directory),
            name,
            gameVersion,
            loader,
            loaderVersion,
            directory,
            CountMods(directory),
            false,
            createdAt,
            updatedAt,
            profile?.LastUsed);
    }

    private static GameInstanceRecord? TryReadArchoera(string directory)
    {
        var path = Path.Combine(directory, ".archoera", "instance.json");
        if (!File.Exists(path)) return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var id = String(root, "id");
            var name = String(root, "name");
            var game = String(root, "gameVersion");
            if (id is null || name is null || game is null) return null;
            var loader = String(root, "loader") ?? "vanilla";
            return new GameInstanceRecord(
                id,
                name,
                game,
                loader,
                LoaderVersionText.Normalize(
                    loader,
                    game,
                    String(root, "loaderVersion")),
                directory,
                CountMods(directory),
                root.TryGetProperty("favorite", out var favorite) &&
                favorite.ValueKind == JsonValueKind.True,
                Date(root, "createdAt") ?? Directory.GetCreationTimeUtc(directory),
                Date(root, "updatedAt") ?? Directory.GetLastWriteTimeUtc(directory),
                Date(root, "lastPlayedAt"),
                ReadOverrides(root));
        }
        catch
        {
            return null;
        }
    }

    private static SjmclMetadata? TryReadSjmcl(string directory)
    {
        var path = Path.Combine(directory, "sjmclcfg.json");
        if (!File.Exists(path)) return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var name = String(root, "name");
            var gameVersion = String(root, "version");
            string? loaderType = null;
            string? loaderVersion = null;
            if (root.TryGetProperty("modLoader", out var modLoader) &&
                modLoader.ValueKind == JsonValueKind.Object)
            {
                loaderType = String(modLoader, "loaderType");
                loaderVersion = String(modLoader, "version");
            }

            return new SjmclMetadata(
                name,
                gameVersion,
                NormalizeLoader(loaderType),
                LoaderVersionText.Normalize(loaderType, gameVersion, loaderVersion));
        }
        catch
        {
            return null;
        }
    }

    private static MmcMetadata? TryReadMmc(string directory)
    {
        var packPath = Path.Combine(directory, "mmc-pack.json");
        var cfgPath = Path.Combine(directory, "instance.cfg");
        if (!File.Exists(packPath) && !File.Exists(cfgPath)) return null;

        string? name = null;
        if (File.Exists(cfgPath))
        {
            foreach (var line in File.ReadLines(cfgPath))
            {
                if (line.StartsWith("name=", StringComparison.OrdinalIgnoreCase))
                {
                    name = line["name=".Length..].Trim();
                    break;
                }
            }
        }

        string? gameVersion = null;
        string? loader = null;
        string? loaderVersion = null;
        if (File.Exists(packPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(packPath));
                if (document.RootElement.TryGetProperty("components", out var components) &&
                    components.ValueKind == JsonValueKind.Array)
                {
                    foreach (var component in components.EnumerateArray())
                    {
                        if (component.ValueKind != JsonValueKind.Object) continue;
                        var uid = String(component, "uid");
                        var version = String(component, "version");
                        if (uid is null) continue;
                        if (uid.Contains("minecraft", StringComparison.OrdinalIgnoreCase))
                        {
                            gameVersion = version;
                        }
                        else if (uid.Contains("fabric", StringComparison.OrdinalIgnoreCase) ||
                                 uid.Contains("quilt", StringComparison.OrdinalIgnoreCase) ||
                                 uid.Contains("forge", StringComparison.OrdinalIgnoreCase))
                        {
                            loader = NormalizeLoader(uid);
                            loaderVersion = version;
                        }
                    }
                }
            }
            catch
            {
                // Keep whatever instance.cfg supplied.
            }
        }

        return new MmcMetadata(name, gameVersion, loader, loaderVersion);
    }

    private static ModrinthMetadata? TryReadModrinth(string directory)
    {
        var path = Path.Combine(directory, "modrinth.index.json");
        if (!File.Exists(path)) return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var name = String(root, "name");
            string? gameVersion = null;
            string? loader = null;
            string? loaderVersion = null;
            if (root.TryGetProperty("dependencies", out var dependencies) &&
                dependencies.ValueKind == JsonValueKind.Object)
            {
                gameVersion = String(dependencies, "minecraft");
                loaderVersion = String(dependencies, "fabric-loader") ??
                                String(dependencies, "quilt-loader") ??
                                String(dependencies, "forge") ??
                                String(dependencies, "neoforge");
                if (String(dependencies, "fabric-loader") is not null) loader = "fabric";
                else if (String(dependencies, "quilt-loader") is not null) loader = "quilt";
                else if (String(dependencies, "neoforge") is not null) loader = "neoforge";
                else if (String(dependencies, "forge") is not null) loader = "forge";
            }

            return new ModrinthMetadata(name, gameVersion, loader, loaderVersion);
        }
        catch
        {
            return null;
        }
    }

    private static VersionMetadata? TryReadVersionJson(
        string directory,
        LauncherProfile? profile)
    {
        var versionPath = Path.Combine(directory, $"{Path.GetFileName(directory)}.json");
        if (!File.Exists(versionPath)) return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(versionPath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var id = String(root, "id");
            var inherits = String(root, "inheritsFrom");
            var mainClass = String(root, "mainClass") ?? string.Empty;
            var gameVersion = !string.IsNullOrWhiteSpace(inherits) ? inherits : null;
            if (gameVersion is null && id is not null && LooksLikeVersion(id))
            {
                gameVersion = id;
            }

            var loader = "vanilla";
            var loaderVersion = string.Empty;
            if (root.TryGetProperty("libraries", out var libraries) &&
                libraries.ValueKind == JsonValueKind.Array)
            {
                foreach (var library in libraries.EnumerateArray())
                {
                    if (library.ValueKind != JsonValueKind.Object) continue;
                    var coordinate = String(library, "name");
                    if (coordinate is null) continue;
                    if (coordinate.StartsWith("net.fabricmc:fabric-loader:", StringComparison.OrdinalIgnoreCase))
                    {
                        loader = "fabric";
                        loaderVersion = coordinate.Split(':').Last();
                    }
                    else if (coordinate.StartsWith("org.quiltmc:quilt-loader:", StringComparison.OrdinalIgnoreCase))
                    {
                        loader = "quilt";
                        loaderVersion = coordinate.Split(':').Last();
                    }
                    else if (coordinate.StartsWith("net.neoforged:neoforge:", StringComparison.OrdinalIgnoreCase))
                    {
                        loader = "neoforge";
                        loaderVersion = coordinate.Split(':').Last();
                    }
                    else if (coordinate.StartsWith("net.neoforged:forge:", StringComparison.OrdinalIgnoreCase) ||
                             coordinate.StartsWith("net.minecraftforge:forge:", StringComparison.OrdinalIgnoreCase))
                    {
                        loader = "forge";
                        var value = coordinate.Split(':').Last();
                        loaderVersion = value.Contains('-') ? value[(value.IndexOf('-') + 1)..] : value;
                    }
                }
            }

            if (mainClass.Contains("fabric", StringComparison.OrdinalIgnoreCase)) loader = "fabric";
            if (mainClass.Contains("quilt", StringComparison.OrdinalIgnoreCase)) loader = "quilt";
            if (mainClass.Contains("bootstraplauncher", StringComparison.OrdinalIgnoreCase) &&
                loader == "vanilla")
            {
                loader = id?.Contains("neoforge", StringComparison.OrdinalIgnoreCase) == true
                    ? "neoforge"
                    : "forge";
            }

            return new VersionMetadata(
                profile?.Name,
                gameVersion,
                loader,
                loaderVersion);
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, LauncherProfile> ReadLxProfiles(string root)
    {
        var result = new Dictionary<string, LauncherProfile>(PathComparer);
        var path = Path.Combine(root, "lx_profiles.json");
        if (!File.Exists(path)) return result;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("profiles", out var profiles) ||
                profiles.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var property in profiles.EnumerateObject())
            {
                var value = property.Value;
                if (value.ValueKind != JsonValueKind.Object) continue;
                var gameDir = String(value, "gameDir");
                if (string.IsNullOrWhiteSpace(gameDir)) continue;
                string normalized;
                try
                {
                    normalized = Normalize(gameDir);
                }
                catch
                {
                    continue;
                }

                result[normalized] = new LauncherProfile(
                    String(value, "name"),
                    normalized,
                    Date(value, "created"),
                    Date(value, "lastUsed"));
            }
        }
        catch
        {
            // A malformed profile file must not block directory discovery.
        }

        return result;
    }

    private static bool HasInstanceMarkers(string directory)
        => File.Exists(Path.Combine(directory, "sjmclcfg.json")) ||
           File.Exists(Path.Combine(directory, "mmc-pack.json")) ||
           File.Exists(Path.Combine(directory, "instance.cfg")) ||
           File.Exists(Path.Combine(directory, "modrinth.index.json")) ||
           Directory.Exists(Path.Combine(directory, "mods")) ||
           Directory.Exists(Path.Combine(directory, "saves")) ||
           File.Exists(Path.Combine(directory, "options.txt")) ||
           Directory.Exists(Path.Combine(directory, "config"));

    private static int CountMods(string directory)
    {
        try
        {
            var mods = Path.Combine(directory, "mods");
            return Directory.Exists(mods)
                ? Directory.GetFiles(mods, "*.jar", SearchOption.TopDirectoryOnly).Length
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static string StableId(string directory)
    {
        var key = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? directory.ToLowerInvariant()
            : directory;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return "inst-" + Convert.ToHexString(hash).ToLowerInvariant()[..24];
    }

    private static bool LooksLikeVersion(string value)
        => value.Length > 0 && char.IsAsciiDigit(value[0]) && value.Contains('.');

    private static string NormalizeLoader(string? value)
    {
        var text = value?.ToLowerInvariant() ?? string.Empty;
        if (text.Contains("neoforge")) return "neoforge";
        if (text.Contains("quilt")) return "quilt";
        if (text.Contains("fabric")) return "fabric";
        if (text.Contains("forge")) return "forge";
        return "vanilla";
    }

    private static bool IsWithin(string path, string root)
    {
        try
        {
            var fullPath = Normalize(path);
            var fullRoot = Normalize(root);
            return fullPath.Equals(fullRoot, PathComparison) ||
                   fullPath.StartsWith(
                       fullRoot + Path.DirectorySeparatorChar,
                       PathComparison);
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> SafeDirectories(string root)
    {
        try
        {
            return Directory.Exists(root)
                ? Directory.GetDirectories(root)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                : [];
        }
        catch
        {
            return [];
        }
    }

    private static string Normalize(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string? String(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) &&
           element.ValueKind == JsonValueKind.String &&
           !string.IsNullOrWhiteSpace(element.GetString())
            ? element.GetString()
            : null;

    private static DateTimeOffset? Date(JsonElement root, string name)
        => DateTimeOffset.TryParse(String(root, name), out var value) &&
           value.Year >= 2000
            ? value
            : null;

    private static InstanceOverrides? ReadOverrides(JsonElement root)
    {
        if (!root.TryGetProperty("overrides", out var element) ||
            element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var overrides = new InstanceOverrides(
            Int(element, "memoryMinMb"),
            Int(element, "memoryMaxMb"),
            String(element, "jvmArgs"),
            String(element, "gcMode"),
            String(element, "gameArgs"),
            String(element, "javaPath"),
            Int(element, "windowWidth"),
            Int(element, "windowHeight"),
            Bool(element, "fullscreen"));
        return overrides.IsEmpty ? null : overrides;
    }

    private static int? Int(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) &&
           element.ValueKind == JsonValueKind.Number &&
           element.TryGetInt32(out var value)
            ? value
            : null;

    private static bool? Bool(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) &&
           element.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? element.GetBoolean()
            : null;

    private sealed record SjmclMetadata(
        string? Name,
        string? GameVersion,
        string Loader,
        string LoaderVersion);

    private sealed record MmcMetadata(
        string? Name,
        string? GameVersion,
        string? Loader,
        string? LoaderVersion);

    private sealed record ModrinthMetadata(
        string? Name,
        string? GameVersion,
        string? Loader,
        string? LoaderVersion);

    private sealed record VersionMetadata(
        string? Name,
        string? GameVersion,
        string? Loader,
        string? LoaderVersion);

    private sealed record LauncherProfile(
        string? Name,
        string GameDir,
        DateTimeOffset? Created,
        DateTimeOffset? LastUsed);
}
