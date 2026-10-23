using System.Text;
using System.Text.Json;
using Archoera.Application.Instances;
using Archoera.Application.Downloads;
using Archoera.Application.Loaders;
using Archoera.Application.Modules;
using Archoera.Application.Packs;
using Archoera.Domain.Downloads;
using Archoera.Domain.Instances;
using Archoera.Domain.Java;
using Archoera.Domain.Loaders;
using Archoera.Domain.Mojang;
using Archoera.Domain.Resources;
using Archoera.Infrastructure.Downloads;
using Archoera.Infrastructure.Instances;
using Archoera.Infrastructure.Java;
using Archoera.Infrastructure.Launch;
using Archoera.Infrastructure.Loaders;
using Archoera.Infrastructure.Mojang;
using Archoera.Infrastructure.Resources.Modrinth;
using Archoera.Infrastructure.Modules;
using Archoera.Infrastructure.Settings;

namespace Archoera.Infrastructure.Packs;

/// <summary>Merged launch profile produced by an in-place Forge repair.</summary>
public sealed record ForgeInstallOutcome(string VersionId, string VersionJson);

/// <summary>Result of an in-place dependency repair for an existing instance.</summary>
public sealed record PackRepairResult(GameInstanceRecord Instance, string VersionId);

/// <summary>Read-only loader artifact check used by launch preflight.</summary>
public sealed record ForgeCheckResult(bool Ok, string? Code, string? Message);

/// <summary>Outcome of one local/Modrinth pack install.</summary>
public sealed record PackInstallResult(
    GameInstanceRecord Instance,
    string PackVersionId,
    string Source,
    int DownloadedFiles,
    int SkippedFiles,
    int ExtractedFiles,
    long DownloadedBytes);

/// <summary>
/// Installs a Modrinth <c>.mrpack</c>: create the instance, install vanilla +
/// Fabric/Quilt from the pack dependencies, extract archive overrides, then
/// download every remaining index file through the Rust executor.
///
/// The archive is parsed/extracted in C# (the Rust executor stays plan-only).
/// Extraction and index downloads run concurrently; bundled overrides always
/// win because their paths are removed from the download plan first.
/// </summary>
public sealed class PackInstallService
{
    private static readonly HttpClient SharedHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    private readonly IInstanceRepository _repository;
    private readonly JsonAppSettingsStore _settings;
    private readonly IDownloadPlanRunner _runner;
    private readonly PackGameInstallService _gameInstall;
    private readonly ModrinthClient _modrinth;
    private readonly JavaRuntimeSelectionService _java;
    private readonly IForgeArtifactPlanner _forgePlanner;
    private readonly IForgeRepairService _forgeRepair;
    private readonly RegionPolicy _regionPolicy;

    public PackInstallService(
        IInstanceRepository? repository = null,
        JsonAppSettingsStore? settings = null,
        IDownloadPlanRunner? runner = null,
        PackGameInstallService? gameInstall = null,
        ModrinthClient? modrinth = null,
        JavaRuntimeSelectionService? java = null,
        IForgeArtifactPlanner? forgePlanner = null,
        IForgeRepairService? forgeRepair = null,
        RegionPolicy? regionPolicy = null)
    {
        _settings = settings ?? new JsonAppSettingsStore();
        _repository = repository ?? new SqliteInstanceRepository(settings: _settings);
        _runner = runner ?? new DownloadPlanRunner();
        _gameInstall = gameInstall ?? new PackGameInstallService();
        _java = java ?? new JavaRuntimeSelectionService(settings: _settings);
        _forgePlanner = forgePlanner ?? new ForgeArtifactPlanner();
        _regionPolicy = regionPolicy ?? new RegionPolicy();
        _forgeRepair = forgeRepair ?? new ForgeRepairService(
            new ForgeProcessorRunner(),
            new LoaderArtifactVerifier());
        _modrinth = modrinth ?? new ModrinthClient(
            SharedHttp,
            new ModrinthClientOptions
            {
                UserAgent = ModrinthClientOptions.CreateUserAgent(
                    "0.0.1+1",
                    "https://github.com/BetaStudio2/ArchoeraCX"),
            });
    }

    /// <summary>Installs a local <c>.mrpack</c> into a new instance.</summary>
    public async Task<PackInstallResult> InstallLocalAsync(
        string archivePath,
        string? name = null,
        string? rootPath = null,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(archivePath);
        if (!File.Exists(fullPath))
        {
            throw new PackException(
                PackErrorCode.ArchiveInvalid,
                $"pack archive not found: {fullPath}");
        }

        var contents = PackArchive.Read(fullPath, clientSide: true);
        return await InstallFromArchiveAsync(
                fullPath,
                contents,
                new PackSource("local", null, null, fullPath),
                name,
                rootPath,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads a Modrinth modpack version (<c>.mrpack</c>) through the Rust
    /// executor, then installs it into a new instance.
    /// </summary>
    public async Task<PackInstallResult> InstallModrinthAsync(
        string projectId,
        string versionId,
        string? name = null,
        string? rootPath = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId) ||
            string.IsNullOrWhiteSpace(versionId))
        {
            throw new PackException(
                PackErrorCode.Invalid,
                "projectId and versionId are required");
        }

        var version = await _modrinth
            .GetVersionAsync(versionId, cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(version.ProjectId, projectId, StringComparison.OrdinalIgnoreCase))
        {
            throw new PackException(
                PackErrorCode.Invalid,
                $"version {versionId} does not belong to project {projectId}");
        }

        var file = SelectPackFile(version);
        var hash = file.PreferredHash
            ?? throw new PackException(
                PackErrorCode.Invalid,
                $"{file.FileName}: provider supplied neither sha512 nor sha1");
        if (!DownloadPlanPaths.IsPlainFileName(file.FileName))
        {
            throw new PackException(
                PackErrorCode.Invalid,
                "provider returned an unsafe pack file name");
        }

        var stagingRoot = Path.Combine(
            _settings.DataRoot,
            "staging",
            "packs",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);
        var archivePath = Path.Combine(stagingRoot, "pack.mrpack");
        try
        {
            var plan = new DownloadPlanBuilder(stagingRoot)
                .AddFile(
                    file.Url,
                    "pack.mrpack",
                    hash,
                    file.Size,
                    mode: DownloadFileMode.Ensure)
                .Build();

            var download = await _runner
                .ExecuteAsync(plan, cancellationToken)
                .ConfigureAwait(false);
            if (!download.Completed)
            {
                throw new PackException(
                    PackErrorCode.DownloadIncomplete,
                    $"pack download {download.Status}" +
                    (download.FailedCount > 0
                        ? $" ({download.FailedCount} file(s) failed): " +
                          download.FailedSummary
                        : string.Empty) +
                    TailDetail(download.Stderr));
            }

            var contents = PackArchive.Read(archivePath, clientSide: true);
            return await InstallFromArchiveAsync(
                    archivePath,
                    contents,
                    new PackSource("modrinth", projectId, versionId, file.FileName),
                    name,
                    rootPath,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (Directory.Exists(stagingRoot))
                {
                    Directory.Delete(stagingRoot, recursive: true);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // Staging cleanup is best-effort.
            }
        }
    }

    private async Task<PackInstallResult> InstallFromArchiveAsync(
        string archivePath,
        PackArchiveContents contents,
        PackSource source,
        string? requestedName,
        string? rootPath,
        CancellationToken cancellationToken)
    {
        var index = contents.Index;
        var loaderName = index.Loader.Kind.Trim().ToLowerInvariant();
        var forgeKind = loaderName switch
        {
            "forge" => LoaderKind.Forge,
            "neoforge" => LoaderKind.NeoForge,
            _ => (LoaderKind?)null,
        };
        if (forgeKind is null &&
            loaderName is not ("vanilla" or "fabric" or "quilt"))
        {
            throw new PackException(
                PackErrorCode.LoaderUnsupported,
                $"unsupported pack loader '{index.Loader.Kind}'");
        }

        if (!_runner.IsAvailable)
        {
            throw new DownloadRunnerException(
                "DOWNLOADER_MISSING",
                "archoera-dl was not found; build app/modules/downloader and set ARCHOERACX_DL_BIN");
        }

        var platform = MojangPlatform.Current();
        JavaRuntimeInfo? javaRuntime = null;
        if (forgeKind is not null)
        {
            // Fail before creating an instance or downloading anything when
            // the Forge/NeoForge processors have no usable JVM.
            javaRuntime = await _java
                .SelectAsync(null, index.MinecraftVersion, cancellationToken)
                .ConfigureAwait(false);
        }

        var instance = await CreateInstanceAsync(
                index,
                requestedName,
                rootPath,
                cancellationToken)
            .ConfigureAwait(false);

        // A brand-new instance has no loader layout yet, so the user's
        // "share loader files" preference decides where it goes.
        var shareLoaderFiles = (await _settings
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false))
            .ShareLoaderFiles;

        DownloadPlan gamePlan;
        string versionId;
        string versionJson;
        if (forgeKind is { } kind)
        {
            var outcome = await InstallForgeGameAsync(
                    instance,
                    kind,
                    javaRuntime!.Path,
                    cancellationToken,
                    shareLoaderFiles)
                .ConfigureAwait(false);
            versionId = outcome.VersionId;
            versionJson = outcome.VersionJson;
        }
        else
        {
            var game = await _gameInstall
                .BuildAsync(
                    instance.RootPath,
                    index.MinecraftVersion,
                    index.Loader.Kind,
                    index.Loader.Version,
                    platform,
                    await ResolveSourceModeAsync(cancellationToken).ConfigureAwait(false),
                    cancellationToken,
                    shareLoaderFiles)
                .ConfigureAwait(false);

            gamePlan = game.Plan;
            versionId = game.VersionId;
            versionJson = game.VersionJson;

            ModuleProgress.Report(
                0,
                gamePlan.TotalBytes,
                0,
                $"Installing Minecraft {index.MinecraftVersion} + {game.LoaderKind}");

            var gameDownload = await _runner
                .ExecuteAsync(gamePlan, cancellationToken)
                .ConfigureAwait(false);
            if (!gameDownload.Completed)
            {
                throw new PackException(
                    PackErrorCode.DownloadIncomplete,
                    $"game/loader download {gameDownload.Status}" +
                    (gameDownload.FailedCount > 0
                        ? $" ({gameDownload.FailedCount} file(s) failed): " +
                          gameDownload.FailedSummary
                        : string.Empty) +
                    TailDetail(gameDownload.Stderr));
            }

            PackGameInstallService.CommitVersionJson(
                instance.RootPath,
                versionId,
                versionJson,
                shareLoaderFiles);
        }

        var overridePaths = contents.Overrides
            .Select(entry => entry.RelativePath)
            .ToHashSet(ModpackPlanBuilder.PathComparer);

        var packPlan = ModpackPlanBuilder.Build(
            instance.RootPath,
            index,
            overridePaths,
            clientSide: true,
            sourceMode: await ResolveSourceModeAsync(cancellationToken)
                .ConfigureAwait(false));

        // Overrides are extracted while index downloads run. They never target
        // the same destination: conflicts were removed from the plan above.
        var extractTask = Task.Run(
            () => PackArchive.Extract(
                archivePath,
                instance.RootPath,
                contents.Overrides,
                cancellationToken,
                (done, total) =>
                {
                    if (done == total || done % 64 == 0)
                    {
                        ModuleProgress.Report(
                            done,
                            total,
                            0,
                            "Extracting bundled overrides");
                    }
                }),
            cancellationToken);

        PlanExecutionResult packDownload;
        IReadOnlyList<string> extracted;
        try
        {
            packDownload = await _runner
                .ExecuteAsync(packPlan, cancellationToken)
                .ConfigureAwait(false);
            extracted = await extractTask.ConfigureAwait(false);
        }
        catch
        {
            try
            {
                await extractTask.ConfigureAwait(false);
            }
            catch
            {
                // Preserve the original failure.
            }

            throw;
        }

        if (!packDownload.Completed)
        {
            throw new PackException(
                PackErrorCode.DownloadIncomplete,
                $"pack files download {packDownload.Status}" +
                (packDownload.FailedCount > 0
                    ? $" ({packDownload.FailedCount} file(s) failed): " +
                      packDownload.FailedSummary
                    : string.Empty) +
                TailDetail(packDownload.Stderr));
        }

        var applicable = index.Files.Count(file => file.AppliesTo(clientSide: true));
        var skipped = applicable - packPlan.Files.Count;
        var result = new PackInstallResult(
            instance,
            index.VersionId,
            source.Kind,
            packPlan.Files.Count,
            Math.Max(0, skipped),
            extracted.Count,
            packPlan.TotalBytes);

        WritePackManifest(instance.RootPath, index, source, result);
        return result;
    }

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

        if (string.IsNullOrWhiteSpace(instance.LoaderVersion))
        {
            return new ForgeCheckResult(
                false,
                "LOADER_VERSION_MISSING",
                $"instance {instance.Id} has no loader version");
        }

        string installerPath;
        try
        {
            installerPath = Path.Combine(
                instance.RootPath,
                ForgeMavenUrls.DefaultInstallerRelativePath(
                        kind,
                        instance.GameVersion,
                        instance.LoaderVersion)
                    .Replace('/', Path.DirectorySeparatorChar));
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
            var installRoot = LaunchPaths.ResolveLoaderRoot(instance.RootPath, null);
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
        if (string.IsNullOrWhiteSpace(instance.LoaderVersion)) return null;
        var game = instance.GameVersion;
        var loader = instance.LoaderVersion.Trim();
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
            .FirstOrDefault(IsZip);
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
        if (!_runner.IsAvailable)
        {
            throw new DownloadRunnerException(
                "DOWNLOADER_MISSING",
                "archoera-dl was not found; build app/modules/downloader and set ARCHOERACX_DL_BIN");
        }

        var loader = instance.Loader.Trim().ToLowerInvariant();
        var game = await _gameInstall
            .BuildAsync(
                instance.RootPath,
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
            throw new PackException(
                PackErrorCode.DownloadIncomplete,
                $"game/loader download {download.Status}" +
                (download.FailedCount > 0
                    ? $" ({download.FailedCount} file(s) failed): " +
                      download.FailedSummary
                    : string.Empty) +
                TailDetail(download.Stderr));
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

    private async Task<DownloadSourceMode> ResolveSourceModeAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
            return await DownloadSourceResolver
                .ResolveAsync(settings.DownloadSource, _regionPolicy, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return DownloadSourceMode.Auto;
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
        if (!_runner.IsAvailable)
        {
            throw new DownloadRunnerException(
                "DOWNLOADER_MISSING",
                "archoera-dl was not found; build app/modules/downloader and set ARCHOERACX_DL_BIN");
        }

        if (string.IsNullOrWhiteSpace(instance.LoaderVersion))
        {
            throw new PackException(
                PackErrorCode.Invalid,
                $"instance {instance.Id} has no loader version");
        }

        var platform = MojangPlatform.Current();
        var installerPath = await EnsureForgeInstallerAsync(
                instance.RootPath,
                kind,
                instance.GameVersion,
                instance.LoaderVersion,
                cancellationToken)
            .ConfigureAwait(false);

        var forge = await _gameInstall
            .BuildForgeFromInstallerAsync(
                instance.RootPath,
                kind,
                instance.GameVersion,
                instance.LoaderVersion,
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
            throw new PackException(
                PackErrorCode.DownloadIncomplete,
                $"game/loader download {download.Status}" +
                (download.FailedCount > 0
                    ? $" ({download.FailedCount} file(s) failed): " +
                      download.FailedSummary
                    : string.Empty) +
                TailDetail(download.Stderr));
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

    /// <summary>
    /// Ensures the Forge/NeoForge installer exists inside the instance. The
    /// normal path uses the Maven sidecar plus the Rust executor. When the
    /// official NeoForge Maven/sidecar is unreachable, a BMCLAPI mirror is
    /// fetched by C# and the SHA-1 used for the combined plan is computed from
    /// those bytes; libraries still use the installer's published hashes.
    /// </summary>
    private async Task<string> EnsureForgeInstallerAsync(
        string instanceRoot,
        LoaderKind kind,
        string gameVersion,
        string loaderVersion,
        CancellationToken cancellationToken)
    {
        var relative = ForgeMavenUrls.DefaultInstallerRelativePath(
            kind,
            gameVersion,
            loaderVersion);
        var destination = Path.Combine(
            instanceRoot,
            relative.Replace('/', Path.DirectorySeparatorChar));
        if (IsZip(destination)) return destination;

        var parent = Path.GetDirectoryName(destination);
        if (parent is not null) Directory.CreateDirectory(parent);

        var installerUri = ForgeMavenUrls.Installer(kind, gameVersion, loaderVersion);
        try
        {
            var descriptor = await _gameInstall
                .ResolveForgeInstallerAsync(
                    kind,
                    gameVersion,
                    loaderVersion,
                    cancellationToken)
                .ConfigureAwait(false);
            if (descriptor.Size is not { } size)
            {
                throw new PackException(
                    PackErrorCode.InstallerFailed,
                    "installer size is missing");
            }

            var primary = descriptor.Url;
            IReadOnlyList<string> mirrors = [];
            if (ForgeMavenMirror.TryRewrite(
                    descriptor.Url,
                    ForgeMavenMirror.Bmclapi,
                    out var mapped,
                    out var fallback))
            {
                primary = mapped;
                mirrors = fallback;
            }

            var plan = new DownloadPlanBuilder(instanceRoot)
                .AddFile(
                    primary,
                    relative,
                    DownloadHash.Create(DownloadHashAlgorithm.Sha1, descriptor.Sha1),
                    size,
                    mirrors)
                .Build();
            var result = await _runner
                .ExecuteAsync(plan, cancellationToken)
                .ConfigureAwait(false);
            if (result.Completed && IsZip(destination))
            {
                return destination;
            }
        }
        catch (ForgeDownloadException)
        {
            // Official NeoForge Maven is blocked on some networks; fall back.
        }
        catch (DownloadRunnerException exception)
            when (exception.Code != "DOWNLOADER_MISSING")
        {
            // Try the mirror bootstrap before failing the whole install.
        }

        await DownloadInstallerFallbackAsync(
                installerUri,
                destination,
                cancellationToken)
            .ConfigureAwait(false);
        return destination;
    }

    private static async Task DownloadInstallerFallbackAsync(
        Uri officialUri,
        string destination,
        CancellationToken cancellationToken)
    {
        var mirrors = ForgeMavenMirror.InstallerMirrors(officialUri);
        if (mirrors.Count == 0)
        {
            throw new PackException(
                PackErrorCode.InstallerFailed,
                $"no mirror is available for {officialUri.Host}");
        }

        var failures = new List<string>();
        ModuleProgress.Report(0, 0, 0, "Downloading loader installer");
        foreach (var mirror in mirrors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await DownloadFileAsync(mirror, destination, cancellationToken)
                    .ConfigureAwait(false);
                if (IsZip(destination)) return;
                failures.Add($"{mirror.Host}: not a jar");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is HttpRequestException or IOException or
                UnauthorizedAccessException or OperationCanceledException)
            {
                failures.Add($"{mirror.Host}: {exception.Message}");
            }
        }

        throw new PackException(
            PackErrorCode.InstallerFailed,
            "installer download failed: " + string.Join("; ", failures.Take(3)));
    }

    private static async Task DownloadFileAsync(
        Uri uri,
        string destination,
        CancellationToken cancellationToken)
    {
        const long maxBytes = 128L * 1024 * 1024;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd(
            "ArchoeraCX/0.0.1+1 (https://github.com/BetaStudio2/ArchoeraCX)");
        using var response = await SharedHttp
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"HTTP {(int)response.StatusCode} from {uri.Host}");
        }

        if (response.Content.Headers.ContentLength is { } length && length > maxBytes)
        {
            throw new HttpRequestException(
                $"{uri.Host} returned an oversized installer ({length} bytes)");
        }

        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var source = await response.Content
                             .ReadAsStreamAsync(timeout.Token)
                             .ConfigureAwait(false))
            await using (var target = new FileStream(
                             temporary,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 128 * 1024,
                             useAsync: true))
            {
                var buffer = new byte[128 * 1024];
                long total = 0;
                while (true)
                {
                    var read = await source
                        .ReadAsync(buffer, timeout.Token)
                        .ConfigureAwait(false);
                    if (read == 0) break;
                    total += read;
                    if (total > maxBytes)
                    {
                        throw new HttpRequestException(
                            $"{uri.Host} returned an oversized installer");
                    }

                    await target
                        .WriteAsync(buffer.AsMemory(0, read), timeout.Token)
                        .ConfigureAwait(false);
                }
            }

            if (!IsZip(temporary))
            {
                throw new HttpRequestException(
                    $"{uri.Host} did not return a jar");
            }

            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // Best effort.
            }
        }
    }

    private static bool IsZip(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using var stream = File.OpenRead(path);
            var first = stream.ReadByte();
            var second = stream.ReadByte();
            return first == 'P' && second == 'K';
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task<GameInstanceRecord> CreateInstanceAsync(
        ModpackIndex index,
        string? requestedName,
        string? rootPath,
        CancellationToken cancellationToken)
    {
        var name = SanitizeInstanceName(requestedName ?? index.Name);
        try
        {
            return await _repository
                .CreateAsync(
                    name,
                    index.MinecraftVersion,
                    index.Loader.Kind,
                    index.Loader.Version,
                    rootPath,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            throw new PackException(
                PackErrorCode.InstanceFailed,
                $"cannot create instance: {exception.Message}",
                exception);
        }
    }

    private static ResourceFile SelectPackFile(ResourceVersion version)
    {
        static bool IsPack(ResourceFile candidate)
            => candidate.HasUrl &&
               candidate.FileName.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase);

        var file = version.FileList.FirstOrDefault(candidate => candidate.Primary && IsPack(candidate))
                   ?? version.FileList.FirstOrDefault(IsPack);

        if (file is null)
        {
            throw new PackException(
                PackErrorCode.Invalid,
                $"version {version.Id} has no downloadable .mrpack file");
        }

        return file;
    }

    private static string SanitizeInstanceName(string value)
    {
        var builder = new StringBuilder(Math.Min(value.Length, 80));
        foreach (var character in value)
        {
            if (char.IsControl(character)) continue;
            builder.Append(character);
            if (builder.Length >= 80) break;
        }

        var clean = builder.ToString().Trim();
        return clean.Length == 0 ? "Modpack" : clean;
    }

    private static void WritePackManifest(
        string instanceRoot,
        ModpackIndex index,
        PackSource source,
        PackInstallResult result)
    {
        try
        {
            var directory = Path.Combine(instanceRoot, ".archoera");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "pack.json");
            var temporary = path + ".tmp";
            using (var stream = new FileStream(
                       temporary,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None))
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
                   {
                       Indented = true,
                   }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("schemaVersion", 1);
                writer.WriteString("name", index.Name);
                writer.WriteString("versionId", index.VersionId);
                writer.WriteString("gameVersion", index.MinecraftVersion);
                writer.WriteString("loader", index.Loader.Kind);
                writer.WriteString("loaderVersion", index.Loader.Version);
                writer.WriteString("source", source.Kind);
                if (source.ProjectId is not null)
                {
                    writer.WriteString("projectId", source.ProjectId);
                }

                if (source.VersionId is not null)
                {
                    writer.WriteString("providerVersionId", source.VersionId);
                }

                if (source.Origin is not null)
                {
                    writer.WriteString("origin", source.Origin);
                }

                writer.WriteString("installedAt", DateTimeOffset.UtcNow.ToString("O"));
                writer.WriteNumber("downloadedFiles", result.DownloadedFiles);
                writer.WriteNumber("skippedFiles", result.SkippedFiles);
                writer.WriteNumber("extractedFiles", result.ExtractedFiles);
                writer.WriteNumber("downloadedBytes", result.DownloadedBytes);
                writer.WriteEndObject();
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Provenance is informative; a completed install is still valid.
        }
    }

    private static string TailDetail(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var trimmed = text.Trim();
        return ": " + (trimmed.Length <= 600 ? trimmed : trimmed[^600..]);
    }

    private sealed record PackSource(
        string Kind,
        string? ProjectId,
        string? VersionId,
        string? Origin);
}
