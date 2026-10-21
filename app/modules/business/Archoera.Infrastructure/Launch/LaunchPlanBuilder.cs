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

internal static class LaunchPlanBuilder
{
    public static LaunchPlan Build(
        GameInstanceRecord instance,
        MojangVersion version,
        AccountRecord account,
        JavaRuntimeInfo java,
        AppSettings settings,
        LaunchPaths paths)
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
            ["launcher_version"] = "0.1.0",
            ["auth_player_name"] = account.DisplayName,
            ["version_name"] = version.Id,
            ["game_directory"] = paths.InstanceRoot,
            ["assets_root"] = paths.AssetsRoot,
            ["assets_index_name"] = assetIndex,
            ["auth_uuid"] = uuid,
            ["auth_access_token"] = "0",
            ["clientid"] = string.Empty,
            ["auth_xuid"] = account.Xuid ?? string.Empty,
            ["user_type"] = "legacy",
            ["version_type"] = string.IsNullOrWhiteSpace(version.Type) ? "release" : version.Type,
            ["resolution_width"] = "854",
            ["resolution_height"] = "480",
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
            "-Dminecraft.launcher.version=0.1.0",
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
                "--accessToken", "0",
                "--userType", "legacy",
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
    {
        var name = Path.GetFileName(paths.InstanceRoot);
        var candidates = new[]
        {
            Path.Combine(paths.InstanceRoot, $"{name}.jar"),
            Path.Combine(paths.InstanceRoot, $"{version.Id}.jar"),
            Path.Combine(paths.SharedRoot, "versions", version.Id, $"{version.Id}.jar"),
        };
        return candidates.FirstOrDefault(File.Exists)
               ?? throw new LaunchException(
                   "CLIENT_JAR_MISSING",
                   $"client jar not found under {paths.InstanceRoot}");
    }

    private static void ExtractNatives(
        MojangVersion version,
        LaunchPaths paths,
        MojangPlatform platform)
    {
        Directory.CreateDirectory(paths.NativesRoot);
        if (Directory.EnumerateFiles(paths.NativesRoot).Any()) return;

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

    private static MojangDownload? NativeDownload(
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

    private static string? LibraryRelativePath(MojangLibrary library)
    {
        var artifact = library.Downloads?.Artifact;
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
