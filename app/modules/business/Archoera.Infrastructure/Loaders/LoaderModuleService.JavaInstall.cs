using System.Text;
using System.Text.Json;
using Archoera.Application;
using Archoera.Application.Downloads;
using Archoera.Application.Loaders;
using Archoera.Application.Mojang;
using Archoera.Domain.Loaders;
using Archoera.Domain.Mojang;
using Archoera.Infrastructure.Launch;
using Archoera.Infrastructure.Modules;
using Archoera.Infrastructure.Mojang;

namespace Archoera.Infrastructure.Loaders;

// The pure Java path is independent of loader.plan/repair; keeping it in its
// own partial file keeps LoaderModuleService under the file-size budget.
public sealed partial class LoaderModuleService
{
    /// <summary>
    /// Pure Java install: runs the official Forge/NeoForge installer with
    /// <c>--installClient</c>, then imports the version it generated into the
    /// instance profile. The installer owns downloads and processors, so this
    /// path is independent of <c>loader.plan</c>/<c>loader.repair</c>.
    /// </summary>
    private async Task<byte[]> InstallJavaAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(paramsJson ?? "{}");
            var root = document.RootElement;
            var instanceRoot = ReadString(root, "root");
            var installerPath = ReadString(root, "installerPath");
            var kindText = ReadString(root, "kind");
            var gameVersion = ReadString(root, "gameVersion");
            var loaderVersion = ReadString(root, "loaderVersion");
            var javaPath = ReadString(root, "javaPath");
            if (string.IsNullOrWhiteSpace(instanceRoot) ||
                !Path.IsPathFullyQualified(instanceRoot) ||
                string.IsNullOrWhiteSpace(installerPath) ||
                !Path.IsPathFullyQualified(installerPath) ||
                !TryParseKind(kindText, out _) ||
                string.IsNullOrWhiteSpace(gameVersion) ||
                string.IsNullOrWhiteSpace(loaderVersion) ||
                string.IsNullOrWhiteSpace(javaPath) ||
                !Path.IsPathFullyQualified(javaPath))
            {
                return ModuleJson.Error(
                    "E_INVALID",
                    "loader.installJava requires root, installerPath, kind, " +
                    "gameVersion, loaderVersion and javaPath");
            }

            var installerFull = Path.GetFullPath(installerPath);
            if (!File.Exists(installerFull))
            {
                return ModuleJson.Error(
                    "INSTALLER_MISSING",
                    $"installer jar not found: {installerFull}");
            }

            if (!File.Exists(javaPath))
            {
                return ModuleJson.Error(
                    "JAVA_MISSING",
                    $"java executable not found: {javaPath}");
            }

            if (!ForgeInstallerRunner.SupportsClientInstall(installerFull))
            {
                return ModuleJson.Error(
                    "LOADER_JAVA_INSTALL_UNSUPPORTED",
                    "this installer has no --installClient CLI " +
                    "(Forge 1.7.10 and older)");
            }

            var rootFull = Path.GetFullPath(instanceRoot);
            Directory.CreateDirectory(rootFull);
            // The installer writes libraries and versions into the loader root:
            // the shared game root when sharing is on, otherwise the instance.
            var shareLoaderFiles =
                !root.TryGetProperty("shareLoaderFiles", out var shareElement) ||
                shareElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                shareElement.GetBoolean();
            var loaderRoot = LaunchPaths.ResolveLoaderRoot(rootFull, shareLoaderFiles);
            Directory.CreateDirectory(loaderRoot);
            EnsureLauncherProfiles(loaderRoot);

            var sourceMode = await ResolveSourceModeAsync(cancellationToken)
                .ConfigureAwait(false);
            var mirror = sourceMode == DownloadSourceMode.Mirror
                ? ForgeMavenMirror.Bmclapi
                : null;
            var startedAt = DateTimeOffset.UtcNow;
            var run = await new ForgeInstallerRunner()
                .RunAsync(
                    javaPath,
                    installerFull,
                    loaderRoot,
                    mirror,
                    TimeSpan.FromMinutes(45),
                    cancellationToken)
                .ConfigureAwait(false);
            if (run.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(run.StandardError)
                    ? run.StandardOutput
                    : run.StandardError;
                return ModuleJson.Error(
                    "LOADER_JAVA_INSTALL_FAILED",
                    $"official installer exited with {run.ExitCode}: {Tail(detail)}");
            }

            var versionId = await ResolveInstalledVersionIdAsync(
                    installerFull,
                    loaderRoot,
                    startedAt,
                    cancellationToken)
                .ConfigureAwait(false);
            if (versionId is null)
            {
                return ModuleJson.Error(
                    "LOADER_JAVA_INSTALL_FAILED",
                    "the installer did not produce versions/<id>/<id>.json");
            }

            var versionPath = Path.Combine(
                loaderRoot,
                "versions",
                versionId,
                $"{versionId}.json");
            var versionJson = await File
                .ReadAllTextAsync(versionPath, cancellationToken)
                .ConfigureAwait(false);
            var mergedJson = await MergeWithVanillaAsync(
                    gameVersion,
                    versionId,
                    versionJson,
                    cancellationToken)
                .ConfigureAwait(false);
            var profilePath = LoaderPaths.InstanceProfilePath(rootFull);
            Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
            WriteAtomically(profilePath, mergedJson);

            return BuildEnvelope(writer =>
            {
                writer.WriteString("versionId", versionId);
                writer.WriteString("versionJson", mergedJson);
                writer.WriteString("profilePath", profilePath);
                writer.WriteString("installerPath", installerFull);
                writer.WriteString("installRoot", loaderRoot);
                writer.WriteBoolean("javaInstalled", true);
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or ArgumentException or
            ForgeDownloadException or MojangException or VersionMergeException)
        {
            return ModuleJson.Error(
                "LOADER_JAVA_INSTALL_FAILED",
                exception.Message);
        }
    }

    /// <summary>
    /// The installer refuses to run without a launcher profile file. Create a
    /// minimal one when the instance has never been opened by a launcher; an
    /// existing file is never rewritten.
    /// </summary>
    private static void EnsureLauncherProfiles(string rootFull)
    {
        var path = Path.Combine(rootFull, "launcher_profiles.json");
        if (File.Exists(path))
        {
            return;
        }

        WriteAtomically(path, """{"profiles":{},"settings":{},"version":3}""");
    }

    /// <summary>
    /// Resolves the version id the installer generated. The installer's own
    /// metadata is authoritative; a fresh profile under <c>versions/</c> is the
    /// fallback for installers that omit it.
    /// </summary>
    private static async Task<string?> ResolveInstalledVersionIdAsync(
        string installerPath,
        string rootFull,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        var declared = ReadInstallerVersionId(installerPath);
        if (declared is not null &&
            File.Exists(Path.Combine(rootFull, "versions", declared, $"{declared}.json")))
        {
            return declared;
        }

        var versions = Path.Combine(rootFull, "versions");
        if (!Directory.Exists(versions))
        {
            return declared;
        }

        string? newest = null;
        var newestAt = DateTime.MinValue;
        foreach (var file in Directory.EnumerateFiles(
                     versions,
                     "*.json",
                     SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var written = File.GetLastWriteTimeUtc(file);
            if (written < startedAt.UtcDateTime.AddSeconds(-2))
            {
                continue;
            }

            if (written <= newestAt)
            {
                continue;
            }

            var id = Path.GetFileNameWithoutExtension(file);
            if (!string.Equals(
                    Path.GetFileName(Path.GetDirectoryName(file)),
                    id,
                    StringComparison.Ordinal))
            {
                continue;
            }

            newest = id;
            newestAt = written;
        }

        return newest ?? declared;
    }

    private static string? ReadInstallerVersionId(string installerPath)
    {
        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(installerPath);
            var entry = archive.Entries.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.FullName,
                    "version.json",
                    StringComparison.OrdinalIgnoreCase));
            if (entry is not null)
            {
                using var stream = entry.Open();
                using var document = JsonDocument.Parse(stream);
                return ReadString(document.RootElement, "id");
            }

            var profile = archive.Entries.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.FullName,
                    "install_profile.json",
                    StringComparison.OrdinalIgnoreCase));
            if (profile is null)
            {
                return null;
            }

            using var profileStream = profile.Open();
            using var profileDocument = JsonDocument.Parse(profileStream);
            if (profileDocument.RootElement.TryGetProperty("versionInfo", out var versionInfo))
            {
                return ReadString(versionInfo, "id");
            }

            if (profileDocument.RootElement.TryGetProperty("install", out var install))
            {
                return ReadString(install, "target") ?? ReadString(install, "version");
            }

            return null;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or JsonException or
            UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The installer writes a profile that still inherits from the vanilla
    /// version. The launch module requires a self-contained profile, so merge
    /// the vanilla layer before writing it into the instance folder.
    /// </summary>
    private async Task<string> MergeWithVanillaAsync(
        string gameVersion,
        string versionId,
        string versionJson,
        CancellationToken cancellationToken)
    {
        using var ownedHttp = _mojang is null ? new HttpClient() : null;
        var mojang = _mojang;
        if (mojang is null)
        {
            var userAgent = MojangClientOptions.CreateUserAgent(
                ArchoeraBuildInfo.Version,
                "https://github.com/BetaStudio2/ArchoeraCX");
            mojang = new MojangClient(
                ownedHttp!,
                new MojangClientOptions { UserAgent = userAgent });
        }

        var vanillaJson = await mojang
            .GetVersionJsonAsync(gameVersion, cancellationToken)
            .ConfigureAwait(false);
        var merged = new VersionMergeService().Merge(
        [
            new VersionLayer(
                gameVersion,
                Encoding.UTF8.GetString(vanillaJson),
                "mojang"),
            new VersionLayer(versionId, versionJson, "loader"),
        ]);
        return Encoding.UTF8.GetString(merged.Json);
    }

    private static string Tail(string value)
        => value.Length <= 800 ? value : value[^800..];
}
