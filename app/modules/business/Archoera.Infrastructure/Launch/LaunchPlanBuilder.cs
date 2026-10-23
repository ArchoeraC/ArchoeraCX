using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Archoera.Application.Mojang;
using Archoera.Domain.Accounts;
using Archoera.Domain.Instances;
using Archoera.Domain.Java;
using Archoera.Domain.Mojang;
using Archoera.Domain.Settings;
using Archoera.Infrastructure.Launch;

namespace Archoera.Infrastructure.Launch;

/// <summary>Result of a read-only launch-file preflight.</summary>
internal sealed record LaunchFileCheck(bool Ok, string? Code, string? Message)
{
    public static LaunchFileCheck Valid { get; } = new(true, null, null);
}

internal static class LaunchPlanBuilder
{
    /// <summary>
    /// Checks launch-critical files without writing or starting anything.
    /// Downloadable files are intentionally not excluded: a missing library,
    /// client jar or asset index is reported so the caller can repair it.
    /// </summary>
    internal static LaunchFileCheck CheckFiles(MojangVersion version, LaunchPaths paths)
    {
        var platform = MojangPlatform.Current();
        var missing = new List<string>();
        foreach (var library in version.Libraries)
        {
            if (!MojangRuleEvaluator.IsAllowed(library.Rules, platform)) continue;
            var relative = LibraryRelativePath(library);
            if (relative is not null &&
                !File.Exists(Path.Combine(paths.LibrariesRoot, relative)))
            {
                missing.Add(relative);
            }

            // Native libraries live in downloads.classifiers, not the base
            // artifact. ExtractNatives() silently skips a missing classifier,
            // which then fails at runtime with "Failed to locate library".
            var native = NativeDownload(library, platform, platform.OsName);
            if (native is null) continue;
            var nativeRelative = NativeRelativePath(
                library,
                platform,
                platform.OsName,
                native);
            if (nativeRelative is not null &&
                !File.Exists(Path.Combine(paths.LibrariesRoot, nativeRelative)))
            {
                missing.Add(nativeRelative);
            }
        }

        if (missing.Count > 0)
        {
            return new LaunchFileCheck(
                false,
                "LIBRARIES_MISSING",
                $"missing {missing.Count} libraries, first: {missing[0]}");
        }

        if (!ClientJarCandidates(version, paths).Any(File.Exists))
        {
            return new LaunchFileCheck(
                false,
                "CLIENT_JAR_MISSING",
                $"client jar not found under {paths.InstanceRoot}");
        }

        var assetIndex = version.AssetIndex?.Id ?? version.Assets;
        if (!string.IsNullOrWhiteSpace(assetIndex) &&
            !string.Equals(assetIndex, "legacy", StringComparison.OrdinalIgnoreCase))
        {
            var indexFile = Path.Combine(
                paths.AssetsRoot,
                "indexes",
                assetIndex + ".json");
            if (!File.Exists(indexFile))
            {
                return new LaunchFileCheck(
                    false,
                    "ASSET_INDEX_MISSING",
                    $"asset index '{assetIndex}' is missing");
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(indexFile));
                if (document.RootElement.TryGetProperty("objects", out var objects) &&
                    objects.ValueKind == JsonValueKind.Object)
                {
                    var missingAssets = 0;
                    string? firstMissing = null;
                    foreach (var item in objects.EnumerateObject())
                    {
                        if (item.Value.ValueKind != JsonValueKind.Object ||
                            !item.Value.TryGetProperty("hash", out var hashElement) ||
                            hashElement.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        var hash = hashElement.GetString() ?? string.Empty;
                        if (hash.Length < 2) continue;
                        var objectPath = Path.Combine(
                            paths.AssetsRoot,
                            "objects",
                            hash[..2],
                            hash);
                        if (File.Exists(objectPath)) continue;
                        missingAssets++;
                        firstMissing ??= item.Name;
                    }

                    if (missingAssets > 0)
                    {
                        return new LaunchFileCheck(
                            false,
                            "ASSET_MISSING",
                            $"missing {missingAssets} assets, first: '{firstMissing}'");
                    }
                }
            }
            catch (Exception exception) when (
                exception is IOException or JsonException or UnauthorizedAccessException)
            {
                return new LaunchFileCheck(
                    false,
                    "ASSET_INDEX_INVALID",
                    $"asset index '{assetIndex}' cannot be read: {exception.Message}");
            }
        }

        return LaunchFileCheck.Valid;
    }

    /// <summary>
    /// Checks loader artifacts that the version JSON does not list as
    /// libraries. Modern NeoForge locates its patched client and universal jar
    /// through <c>-DlibraryDirectory</c> and <c>--fml.neoForgeVersion</c>, so a
    /// merged profile can pass <see cref="CheckFiles"/> while the game is still
    /// unable to start. Forge profiles list those jars, so only NeoForge needs
    /// this extra probe.
    ///
    /// No installer is needed: an instance prepared by another launcher is
    /// launchable as-is, and repair is only requested when a generated jar is
    /// actually absent.
    /// </summary>
    internal static LaunchFileCheck CheckLoaderArtifacts(
        GameInstanceRecord instance,
        MojangVersion version,
        LaunchPaths paths)
    {
        if (!string.Equals(instance.Loader, "neoforge", StringComparison.OrdinalIgnoreCase))
        {
            return LaunchFileCheck.Valid;
        }

        // Inherited profiles are merged/repaired by the caller.
        if (!string.IsNullOrWhiteSpace(version.InheritsFrom)) return LaunchFileCheck.Valid;

        // Some NeoForge profiles do list their generated jars; CheckFiles has
        // already verified those.
        if (version.Libraries.Any(library =>
                library.Name.StartsWith("net.neoforged:neoforge:", StringComparison.OrdinalIgnoreCase) ||
                library.Name.StartsWith("net.neoforged:forge:", StringComparison.OrdinalIgnoreCase)))
        {
            return LaunchFileCheck.Valid;
        }

        if (string.IsNullOrWhiteSpace(instance.LoaderVersion) &&
            string.IsNullOrWhiteSpace(ArgumentValue(version, "--fml.neoForgeVersion")))
        {
            return new LaunchFileCheck(
                false,
                "LOADER_VERSION_MISSING",
                $"instance {instance.Id} has no NeoForge version");
        }

        // The profile is authoritative: it is what the loader will use.
        var loader = ArgumentValue(version, "--fml.neoForgeVersion") ??
                     instance.LoaderVersion.Trim();
        var game = ArgumentValue(version, "--fml.mcVersion") ?? instance.GameVersion;
        var relative = string.Equals(game, "1.20.1", StringComparison.Ordinal)
            ? LegacyNeoForgeRelative(game, loader)
            : $"net/neoforged/neoforge/{loader}/neoforge-{loader}";
        var basePath = Path.Combine(
            paths.LibrariesRoot,
            relative.Replace('/', Path.DirectorySeparatorChar));
        foreach (var suffix in new[] { "-universal.jar", "-client.jar" })
        {
            var path = basePath + suffix;
            if (!File.Exists(path))
            {
                return new LaunchFileCheck(
                    false,
                    "LOADER_FILES_MISSING",
                    $"NeoForge artifact not found: {path}");
            }
        }

        return LaunchFileCheck.Valid;
    }

    /// <summary>
    /// Legacy NeoForge (1.20.1) publishes under <c>net/neoforged/forge</c> with
    /// the game version prefixed to the loader version.
    /// </summary>
    private static string LegacyNeoForgeRelative(string game, string loader)
    {
        var combined = loader.StartsWith(game + "-", StringComparison.Ordinal)
            ? loader
            : $"{game}-{loader}";
        return $"net/neoforged/forge/{combined}/forge-{combined}";
    }

    /// <summary>Value of a <c>--name value</c> pair in the game arguments.</summary>
    private static string? ArgumentValue(MojangVersion version, string name)
    {
        if (version.Arguments is null) return null;
        foreach (var argument in version.Arguments.Game)
        {
            for (var index = 0; index + 1 < argument.Values.Count; index++)
            {
                if (!string.Equals(argument.Values[index], name, StringComparison.Ordinal))
                {
                    continue;
                }

                var value = argument.Values[index + 1];
                if (!string.IsNullOrWhiteSpace(value) &&
                    !value.Contains("${", StringComparison.Ordinal))
                {
                    return value;
                }
            }
        }

        return null;
    }

    public static LaunchPlan Build(
        GameInstanceRecord instance,
        MojangVersion version,
        AccountRecord account,
        JavaRuntimeInfo java,
        AppSettings settings,
        LaunchPaths paths,
        string accessToken = "0",
        string clientId = "",
        string userType = "legacy")
    {
        if (string.IsNullOrWhiteSpace(version.MainClass))
        {
            throw new LaunchException("VERSION_INVALID", "version JSON has no mainClass");
        }

        var platform = MojangPlatform.Current();
        var classpath = BuildClasspath(version, paths, platform);
        var clientJar = FindClientJar(version, paths);
        classpath.Add(clientJar);

        ExtractNatives(version, paths, platform);

        var uuid = string.IsNullOrWhiteSpace(account.Uuid)
            ? OfflineUuid(account.DisplayName)
            : account.Uuid;
        var assetIndex = version.AssetIndex?.Id ?? version.Assets ?? "legacy";
        var substitutions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["natives_directory"] = paths.NativesRoot,
            ["classpath"] = string.Join(Path.PathSeparator, classpath),
            ["classpath_separator"] = Path.PathSeparator.ToString(),
            ["launcher_name"] = "ArchoeraCX",
            ["launcher_version"] = "0.0.1+1",
            ["auth_player_name"] = account.DisplayName,
            ["version_name"] = version.Id,
            ["game_directory"] = paths.InstanceRoot,
            ["assets_root"] = paths.AssetsRoot,
            ["assets_index_name"] = assetIndex,
            ["auth_uuid"] = uuid,
            ["auth_access_token"] = accessToken,
            ["clientid"] = clientId,
            ["auth_xuid"] = account.Xuid ?? string.Empty,
            ["user_type"] = userType,
            ["version_type"] = string.IsNullOrWhiteSpace(version.Type) ? "release" : version.Type,
            ["resolution_width"] = settings.WindowWidth.ToString(),
            ["resolution_height"] = settings.WindowHeight.ToString(),
            ["library_directory"] = paths.LibrariesRoot,
            ["quickPlayPath"] = string.Empty,
            ["quickPlaySingleplayer"] = string.Empty,
            ["quickPlayMultiplayer"] = string.Empty,
        };

        var jvm = new List<string>
        {
            $"-Xms{settings.MemoryMinMb}M",
            $"-Xmx{settings.MemoryMaxMb}M",
            "-Dfile.encoding=UTF-8",
            "-Dminecraft.launcher.brand=ArchoeraCX",
            "-Dminecraft.launcher.version=0.0.1+1",
            "-Djava.library.path=" + paths.NativesRoot,
        };
        var gcFlag = settings.GcMode switch
        {
            "g1gc" => "-XX:+UseG1GC",
            "zgc" => "-XX:+UseZGC",
            "parallel" => "-XX:+UseParallelGC",
            "serial" => "-XX:+UseSerialGC",
            "shenandoah" => "-XX:+UseShenandoahGC",
            "epsilon" => "-XX:+UseEpsilonGC",
            _ => null,
        };
        var customJvm = string.IsNullOrWhiteSpace(settings.JvmArgs)
            ? []
            : settings.JvmArgs
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        var customSelectsGc = customJvm.Any(argument =>
            argument.StartsWith("-XX:+Use", StringComparison.Ordinal) &&
            argument.EndsWith("GC", StringComparison.Ordinal));
        if (gcFlag is not null && !customSelectsGc)
        {
            jvm.Add(gcFlag);
        }
        jvm.AddRange(customJvm);

        if (version.Arguments is { Jvm.Count: > 0 })
        {
            foreach (var argument in version.Arguments.Jvm)
            {
                if (!MojangRuleEvaluator.IsAllowed(argument.Rules, platform)) continue;
                foreach (var value in argument.Values)
                {
                    jvm.Add(Substitute(value, substitutions));
                }
            }
        }

        if (!jvm.Contains("-cp"))
        {
            jvm.Add("-cp");
            jvm.Add(substitutions["classpath"]);
        }

        var game = new List<string>();
        if (version.Arguments is { Game.Count: > 0 })
        {
            foreach (var argument in version.Arguments.Game)
            {
                if (!MojangRuleEvaluator.IsAllowed(argument.Rules, platform)) continue;
                foreach (var value in argument.Values)
                {
                    game.Add(Substitute(value, substitutions));
                }
            }
        }

        if (settings.Fullscreen)
        {
            game.Add("--fullscreen");
        }

        if (!string.IsNullOrWhiteSpace(settings.GameArgs))
        {
            game.AddRange(settings.GameArgs.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        if (game.Count == 0)
        {
            game.AddRange(
            [
                "--username", account.DisplayName,
                "--version", version.Id,
                "--gameDir", paths.InstanceRoot,
                "--assetsDir", paths.AssetsRoot,
                "--assetIndex", assetIndex,
                "--uuid", uuid,
                "--accessToken", accessToken,
                "--userType", userType,
                "--versionType", "release",
            ]);
        }

        jvm.Add(version.MainClass);
        jvm.AddRange(game);
        return new LaunchPlan(
            java.Path,
            paths.InstanceRoot,
            Path.Combine(paths.InstanceRoot, ".archoera", "logs"),
            jvm);
    }

    private static List<string> BuildClasspath(
        MojangVersion version,
        LaunchPaths paths,
        MojangPlatform platform)
    {
        var classpath = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        foreach (var library in version.Libraries)
        {
            if (!MojangRuleEvaluator.IsAllowed(library.Rules, platform)) continue;
            var relative = LibraryRelativePath(library);
            if (relative is null) continue;
            var full = Path.Combine(paths.LibrariesRoot, relative);
            if (!File.Exists(full))
            {
                missing.Add(relative);
                continue;
            }
            if (seen.Add(full)) classpath.Add(full);
        }

        if (missing.Count > 0)
        {
            throw new LaunchException(
                "LIBRARIES_MISSING",
                $"missing {missing.Count} libraries, first: {missing[0]}");
        }

        return classpath;
    }

    private static string FindClientJar(MojangVersion version, LaunchPaths paths)
        => ClientJarCandidates(version, paths).FirstOrDefault(File.Exists)
           ?? throw new LaunchException(
               "CLIENT_JAR_MISSING",
               $"client jar not found under {paths.InstanceRoot}");

    private static IEnumerable<string> ClientJarCandidates(
        MojangVersion version,
        LaunchPaths paths)
    {
        var name = Path.GetFileName(paths.InstanceRoot);
        yield return Path.Combine(paths.InstanceRoot, $"{name}.jar");
        yield return Path.Combine(paths.InstanceRoot, $"{version.Id}.jar");
        yield return Path.Combine(
            paths.SharedRoot,
            "versions",
            version.Id,
            $"{version.Id}.jar");
    }

    private static void ExtractNatives(
        MojangVersion version,
        LaunchPaths paths,
        MojangPlatform platform)
    {
        Directory.CreateDirectory(paths.NativesRoot);

        // Extract every launch instead of trusting a non-empty folder: a stale
        // or partially extracted natives directory otherwise keeps failing
        // with "Failed to locate library". The jars are a few MB at most.
        var osKey = platform.OsName;
        foreach (var library in version.Libraries)
        {
            if (!MojangRuleEvaluator.IsAllowed(library.Rules, platform)) continue;
            var download = NativeDownload(library, platform, osKey);
            if (download is null) continue;
            var relative = download.Path ?? DerivePath(library.Name, null);
            var jar = Path.Combine(paths.LibrariesRoot, relative);
            if (!File.Exists(jar)) continue;
            try
            {
                using var archive = ZipFile.OpenRead(jar);
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith('/')) continue;
                    if (entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase)) continue;
                    if (library.Extract?.Exclude.Any(
                            pattern => System.Text.RegularExpressions.Regex.IsMatch(
                                entry.FullName,
                                pattern,
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase)) == true)
                    {
                        continue;
                    }

                    var target = Path.Combine(paths.NativesRoot, Path.GetFileName(entry.FullName));
                    entry.ExtractToFile(target, overwrite: true);
                }
            }
            catch (Exception exception) when (
                exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                throw new LaunchException(
                    "NATIVES_FAILED",
                    $"could not extract natives from {relative}: {exception.Message}");
            }
        }
    }

    internal static MojangDownload? NativeDownload(
        MojangLibrary library,
        MojangPlatform platform,
        string osKey)
    {
        if (library.Natives is not null &&
            library.Natives.TryGetValue(osKey, out var rawClassifier))
        {
            var classifier = rawClassifier.Replace("${arch}", platform.NativeArchBits);
            return library.Downloads?.ClassifierMap.GetValueOrDefault(classifier);
        }

        var name = library.Name;
        if (name.Contains(":natives-", StringComparison.OrdinalIgnoreCase))
        {
            return library.Downloads?.Artifact;
        }

        return null;
    }

    internal static string? NativeRelativePath(
        MojangLibrary library,
        MojangPlatform platform,
        string osKey,
        MojangDownload download)
    {
        if (!string.IsNullOrWhiteSpace(download.Path))
        {
            return download.Path.Replace('\\', '/');
        }

        if (library.Natives is not null &&
            library.Natives.TryGetValue(osKey, out var rawClassifier))
        {
            return DerivePath(
                library.Name,
                rawClassifier.Replace("${arch}", platform.NativeArchBits));
        }

        return library.Name.Contains(":natives-", StringComparison.OrdinalIgnoreCase)
            ? DerivePath(library.Name, null)
            : null;
    }

    internal static string? LibraryRelativePath(MojangLibrary library)
    {
        var artifact = library.Downloads?.Artifact;
        if (artifact is null && library.Downloads?.ClassifierMap.Count > 0)
        {
            // Classifier-only entry (typically a legacy native): there is no
            // base jar to put on the classpath. Natives are verified and
            // extracted through NativeDownload instead.
            return null;
        }

        if (artifact is not null && !string.IsNullOrWhiteSpace(artifact.Path))
        {
            return artifact.Path.Replace('\\', '/');
        }

        if (artifact is not null)
        {
            return DerivePath(library.Name, null);
        }

        // Loader profiles may use a bare Maven coordinate plus a repository URL.
        return DerivePath(library.Name, null);
    }

    private static string DerivePath(string name, string? forcedClassifier)
    {
        var parts = name.Split(':');
        if (parts.Length < 3) return string.Empty;
        var group = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        var classifier = forcedClassifier ?? (parts.Length > 3 ? parts[3] : null);
        var fileName = classifier is null
            ? $"{artifact}-{version}.jar"
            : $"{artifact}-{version}-{classifier}.jar";
        return $"{group}/{artifact}/{version}/{fileName}";
    }

    private static string Substitute(string value, IReadOnlyDictionary<string, string> substitutions)
    {
        var result = value;
        foreach (var (key, replacement) in substitutions)
        {
            result = result.Replace("${" + key + "}", replacement, StringComparison.Ordinal);
        }
        return result;
    }

    private static string OfflineUuid(string name)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"OfflinePlayer:{name}"));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x30);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        var hex = Convert.ToHexStringLower(bytes);
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }
}
