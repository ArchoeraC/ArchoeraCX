using Archoera.Application.Downloads;
using Archoera.Application.Loaders;
using Archoera.Application.Modules;
using Archoera.Application.Packs;
using Archoera.Domain.Instances;
using Archoera.Domain.Java;
using Archoera.Domain.Loaders;
using Archoera.Domain.Mojang;
using Archoera.Infrastructure.Java;
using Archoera.Infrastructure.Launch;
using Archoera.Infrastructure.Loaders;
using Archoera.Infrastructure.Modules;

namespace Archoera.Infrastructure.Packs;

/// <summary>
/// Repair half of <see cref="PackInstallService"/>: read-only loader checks,
/// local installer reuse, and the vanilla/loader repair flows shared by the
/// dependency-check UI and launch auto-repair. The install half lives in
/// PackInstallService.cs.
/// </summary>
public sealed partial class PackInstallService
{
    /// <summary>
    /// Read-only quick check of installer-owned/generated artifacts. Used by
    /// launch preflight so a deleted PATCHED jar triggers auto-repair even when
    /// it is not part of the launch classpath.
    /// </summary>
    public async Task<ForgeCheckResult> CheckForgeInstanceAsync(
        GameInstanceRecord instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var kind = instance.Loader.Trim().ToLowerInvariant() switch
        {
            "forge" => LoaderKind.Forge,
            "neoforge" => LoaderKind.NeoForge,
            _ => throw new PackException(
                PackErrorCode.LoaderUnsupported,
                $"instance loader '{instance.Loader}' does not use processors"),
        };

        var loaderVersion = LoaderVersionText.Normalize(
            instance.Loader,
            instance.GameVersion,
            instance.LoaderVersion);
        if (string.IsNullOrWhiteSpace(loaderVersion))
        {
            return new ForgeCheckResult(
                false,
                "LOADER_VERSION_MISSING",
                $"instance {instance.Id} has no loader version");
        }

        string installerPath;
        string installRoot;
        try
        {
            installRoot = LaunchPaths.ResolveLoaderRoot(instance.RootPath, null);
            var relative = ForgeMavenUrls.DefaultInstallerRelativePath(
                    kind,
                    instance.GameVersion,
                    loaderVersion)
                .Replace('/', Path.DirectorySeparatorChar);
            // Repair stores the installer under the instance root's downloads/
            // folder, while loader.plan stores it under the loader root. Accept
            // either location.
            var candidates = new[] { installRoot, instance.RootPath }
                .Distinct(StringComparer.Ordinal)
                .Select(root => Path.Combine(root, relative));
            installerPath = candidates.FirstOrDefault(File.Exists) ??
                            candidates.First();
        }
        catch (ForgeDownloadException exception)
        {
            return new ForgeCheckResult(false, exception.Code, exception.Message);
        }
        if (!File.Exists(installerPath))
        {
            return new ForgeCheckResult(
                false,
                "INSTALLER_MISSING",
                $"installer not found: {installerPath}");
        }

        try
        {
            var manifest = await _forgePlanner
                .ParseAsync(
                    installerPath,
                    installRoot,
                    Path.Combine(installRoot, "libraries"),
                    kind,
                    cancellationToken)
                .ConfigureAwait(false);
            var verification = new LoaderArtifactVerifier().Verify(
                manifest,
                installRoot,
                LoaderVerifyMode.Quick);
            return verification.Ok
                ? new ForgeCheckResult(true, null, null)
                : new ForgeCheckResult(
                    false,
                    "LOADER_FILES_MISSING",
                    $"missing {verification.Missing.Count} loader artifact(s), " +
                    $"mismatched {verification.Mismatched.Count}");
        }
        catch (Exception exception) when (
            exception is IOException or LoaderManifestException or
            ArgumentException or UnauthorizedAccessException)
        {
            return new ForgeCheckResult(
                false,
                "LOADER_CHECK_FAILED",
                exception.Message);
        }
    }

    /// <summary>
    /// Repairs Forge/NeoForge processor outputs using an installer that is
    /// already on disk, without the download policy or network. Returns false
    /// when no local installer exists, when a non-regenerable artifact is
    /// missing, or when processors fail; the caller then falls back to the
    /// normal download repair.
    /// </summary>
    public async Task<bool> TryRepairForgeLocallyAsync(
        GameInstanceRecord instance,
        string javaPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (string.IsNullOrWhiteSpace(javaPath)) return false;

        var kind = instance.Loader.Trim().ToLowerInvariant() switch
        {
            "forge" => LoaderKind.Forge,
            "neoforge" => LoaderKind.NeoForge,
            _ => (LoaderKind?)null,
        };
        if (kind is null || string.IsNullOrWhiteSpace(instance.LoaderVersion))
        {
            return false;
        }

        var installer = FindLocalForgeInstaller(instance, kind.Value);
        if (installer is null) return false;

        var installRoot = LaunchPaths.ResolveLoaderRoot(instance.RootPath, null);
        try
        {
            var manifest = await _forgePlanner
                .ParseAsync(
                    installer,
                    installRoot,
                    Path.Combine(installRoot, "libraries"),
                    kind.Value,
                    cancellationToken)
                .ConfigureAwait(false);

            // Generated outputs are exactly what we are about to rebuild.
            // Anything else (libraries, extracted installer files) must already
            // be present, otherwise processors cannot run offline.
            var verification = new LoaderArtifactVerifier().Verify(
                manifest,
                installRoot,
                LoaderVerifyMode.Quick);
            var blocking = verification.Missing
                .Where(artifact => artifact.Kind != LoaderArtifactKind.Generated)
                .Concat(verification.Mismatched.Where(
                    artifact => artifact.Kind != LoaderArtifactKind.Generated))
                .ToList();
            if (blocking.Count > 0 || (verification.Unsafe?.Count ?? 0) > 0)
            {
                return false;
            }

            var repair = await _forgeRepair
                .RepairAsync(manifest, installRoot, javaPath, cancellationToken)
                .ConfigureAwait(false);
            return repair.Ok;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Corrupt installer or processor failure: download repair decides.
            return false;
        }
    }

    /// <summary>
    /// Finds a Forge/NeoForge installer left by this or another launcher in the
    /// instance, the shared Maven cache, or a sibling instance's download folder.
    /// </summary>
    internal static string? FindLocalForgeInstaller(
        GameInstanceRecord instance,
        LoaderKind kind)
    {
        var loader = LoaderVersionText.Normalize(
            kind.ToString(),
            instance.GameVersion,
            instance.LoaderVersion);
        if (string.IsNullOrWhiteSpace(loader)) return null;
        var game = instance.GameVersion;
        var installRoot = LaunchPaths.ResolveLoaderRoot(instance.RootPath, null);
        var libraries = Path.Combine(installRoot, "libraries");
        var token = kind == LoaderKind.Forge ? "forge" : "neoforge";
        var legacy = loader.StartsWith(game + "-", StringComparison.Ordinal)
            ? loader
            : $"{game}-{loader}";

        var names = kind == LoaderKind.Forge
            ? new[]
            {
                $"forge-{game}-{loader}-installer.jar",
                $"forge-{loader}-installer.jar",
            }
            : new[]
            {
                $"neoforge-{loader}-installer.jar",
                $"forge-{legacy}-installer.jar",
            };

        var directories = new List<string>();
        if (kind == LoaderKind.Forge)
        {
            directories.Add(Path.Combine(
                libraries, "net", "minecraftforge", "forge", $"{game}-{loader}-installer"));
            directories.Add(Path.Combine(
                libraries, "net", "minecraftforge", "forge", $"{loader}-installer"));
            directories.Add(Path.Combine(
                libraries, "net", "minecraftforge", "forge", $"{game}-{loader}"));
        }
        else
        {
            directories.Add(Path.Combine(
                libraries, "net", "neoforged", "neoforge", $"{loader}-installer"));
            directories.Add(Path.Combine(
                libraries, "net", "neoforged", "neoforge", loader));
            directories.Add(Path.Combine(
                libraries, "net", "neoforged", "forge", $"{legacy}-installer"));
            directories.Add(Path.Combine(
                libraries, "net", "neoforged", "forge", legacy));
        }

        var candidates = new List<string>();
        foreach (var directory in directories)
        {
            foreach (var name in names)
            {
                candidates.Add(Path.Combine(directory, name));
            }
        }

        var downloads = Path.Combine(instance.RootPath, "downloads", "loaders", token);
        candidates.Add(Path.Combine(downloads, $"{game}-{loader}-installer.jar"));
        foreach (var name in names)
        {
            candidates.Add(Path.Combine(downloads, name));
        }

        // Other instances often keep the installer they were installed with.
        var versions = Path.Combine(installRoot, "versions");
        if (Directory.Exists(versions))
        {
            foreach (var directory in Directory.EnumerateDirectories(versions))
            {
                var sibling = Path.Combine(directory, "downloads", "loaders", token);
                candidates.Add(Path.Combine(sibling, $"{game}-{loader}-installer.jar"));
                foreach (var name in names)
                {
                    candidates.Add(Path.Combine(sibling, name));
                }
            }
        }

        return candidates
            .Distinct(StringComparer.Ordinal)
            .FirstOrDefault(PackForgeInstallerBootstrap.IsZip);
    }

    /// <summary>
    /// Resolves an instance, selects Java, and repairs its Forge/NeoForge
    /// downloads/processors in place. Used by the dependency-check UI.
    /// </summary>
    public async Task<PackRepairResult> RepairInstanceAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            throw new PackException(
                PackErrorCode.InstanceFailed,
                "instanceId is required");
        }

        var instance = (await _repository
                .ListAsync(cancellationToken)
                .ConfigureAwait(false))
            .FirstOrDefault(item => string.Equals(item.Id, instanceId, StringComparison.Ordinal))
            ?? throw new PackException(
                PackErrorCode.InstanceFailed,
                $"instance not found: {instanceId}");

        var loader = instance.Loader.Trim().ToLowerInvariant();
        if (loader is not ("vanilla" or "fabric" or "quilt" or "forge" or "neoforge"))
        {
            throw new PackException(
                PackErrorCode.LoaderUnsupported,
                $"unsupported loader '{instance.Loader}'");
        }

        if (loader is "forge" or "neoforge")
        {
            var java = await _java
                .SelectAsync(null, instance.GameVersion, cancellationToken)
                .ConfigureAwait(false);
            var outcome = await RepairForgeInstanceAsync(
                    instance,
                    java.Path,
                    cancellationToken)
                .ConfigureAwait(false);
            return new PackRepairResult(instance, outcome.VersionId);
        }

        return await RepairSimpleInstanceAsync(instance, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Restores vanilla/Fabric/Quilt downloads from the merged/profile plan.
    /// No processors or JVM are required.
    /// </summary>
    public async Task<PackRepairResult> RepairSimpleInstanceAsync(
        GameInstanceRecord instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        EnsureRunnerAvailable();

        var loader = instance.Loader.Trim().ToLowerInvariant();
        var game = await _gameInstall
            .BuildAsync(                instance.RootPath,
                instance.GameVersion,
                loader,
                instance.LoaderVersion,
                MojangPlatform.Current(),
                await ResolveSourceModeAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken,
                shareLoaderFiles: null)
            .ConfigureAwait(false);

        ModuleProgress.Report(
            0,
            game.Plan.TotalBytes,
            0,
            $"Installing Minecraft {instance.GameVersion} + {loader}");

        var download = await _runner
            .ExecuteAsync(game.Plan, cancellationToken)
            .ConfigureAwait(false);
        if (!download.Completed)
        {
            throw DownloadIncomplete(download, "game/loader");
        }

        PackGameInstallService.CommitVersionJson(
            instance.RootPath,
            game.VersionId,
            game.VersionJson,
            shareLoaderFiles: null);
        return new PackRepairResult(instance, game.VersionId);
    }

    /// <summary>
    /// User preference: `official` keeps the official hosts first, `mirror`
    /// prefers BMCLAPI, and `auto` keeps the mirror-first behaviour used for
    /// installer bootstrapping on slow international links.
    /// </summary>
    private async Task<bool> PreferMirrorAsync(CancellationToken cancellationToken)
    {
        try
        {
            var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
            return DownloadSourceModes.Parse(settings.DownloadSource) != DownloadSourceMode.Official;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// Repairs an existing Forge/NeoForge instance in place: restores missing
    /// downloads through a fresh combined plan, reruns processors, and commits
    /// the merged launch profile. Safe to call when all files are valid.
    /// </summary>
    public async Task<ForgeInstallOutcome> RepairForgeInstanceAsync(
        GameInstanceRecord instance,
        string javaPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var kind = instance.Loader.Trim().ToLowerInvariant() switch
        {
            "forge" => LoaderKind.Forge,
            "neoforge" => LoaderKind.NeoForge,
            _ => throw new PackException(
                PackErrorCode.LoaderUnsupported,
                $"instance loader '{instance.Loader}' does not use processors"),
        };

        return await InstallForgeGameAsync(
                instance,
                kind,
                javaPath,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ForgeInstallOutcome> InstallForgeGameAsync(
        GameInstanceRecord instance,
        LoaderKind kind,
        string javaPath,
        CancellationToken cancellationToken,
        bool? shareLoaderFiles = null)
    {
        EnsureRunnerAvailable();

        var loaderVersion = LoaderVersionText.Normalize(
            instance.Loader,
            instance.GameVersion,
            instance.LoaderVersion);
        if (string.IsNullOrWhiteSpace(loaderVersion))
        {
            throw new PackException(
                PackErrorCode.Invalid,
                $"instance {instance.Id} has no loader version");
        }

        var platform = MojangPlatform.Current();
        var installerPath = await _forgeBootstrap.EnsureForgeInstallerAsync(
                instance.RootPath,
                kind,
                instance.GameVersion,
                loaderVersion,
                cancellationToken)
            .ConfigureAwait(false);

        var forge = await _gameInstall
            .BuildForgeFromInstallerAsync(
                instance.RootPath,
                kind,
                instance.GameVersion,
                loaderVersion,
                installerPath,
                platform,
                preferMavenMirror: await PreferMirrorAsync(cancellationToken)
                    .ConfigureAwait(false),
                cancellationToken,
                shareLoaderFiles: shareLoaderFiles)
            .ConfigureAwait(false);

        ModuleProgress.Report(
            0,
            forge.Plan.TotalBytes,
            0,
            $"Installing Minecraft {instance.GameVersion} + {kind}");

        var download = await _runner
            .ExecuteAsync(forge.Plan, cancellationToken)
            .ConfigureAwait(false);
        if (!download.Completed)
        {
            throw DownloadIncomplete(download, "game/loader");
        }

        var installRoot = LaunchPaths.ResolveLoaderRoot(instance.RootPath, shareLoaderFiles);
        var manifest = await _forgePlanner
            .ParseAsync(
                installerPath,
                installRoot,
                Path.Combine(installRoot, "libraries"),
                kind,
                cancellationToken)
            .ConfigureAwait(false);

        ModuleProgress.Report(
            0,
            Math.Max(1, manifest.ProcessorCount),
            0,
            $"Running {kind} processors");

        var repair = await _forgeRepair
            .RepairAsync(
                manifest,
                installRoot,
                javaPath,
                cancellationToken)
            .ConfigureAwait(false);
        if (!repair.Ok)
        {
            throw new PackException(
                PackErrorCode.LoaderRepairFailed,
                $"{kind} processors failed: " +
                string.Join("; ", repair.Details.Take(4)));
        }

        PackGameInstallService.CommitVersionJson(
            instance.RootPath,
            forge.VersionId,
            forge.VersionJson,
            shareLoaderFiles);

        return new ForgeInstallOutcome(forge.VersionId, forge.VersionJson);
    }
}
