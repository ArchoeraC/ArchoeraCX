using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Archoera.Application;
using Archoera.Application.Instances;
using Archoera.Application.Downloads;
using Archoera.Application.Loaders;
using Archoera.Application.Modules;
using Archoera.Application.Mojang;
using Archoera.Application.Packs;
using Archoera.Domain;
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
using Archoera.Infrastructure.Loaders.Native;
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
public sealed partial class PackInstallService
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
    private readonly PackForgeInstallerBootstrap _forgeBootstrap;
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
            new NativeProcessorRunner(),
            new LoaderArtifactVerifier(),
            mojMapsFallback: new ClientMappingsFallback(
                new MojangClient(
                    SharedHttp,
                    new MojangClientOptions
                    {
                        UserAgent = MojangClientOptions.CreateUserAgent(
                            ArchoeraBuildInfo.Version,
                            "https://github.com/BetaStudio2/ArchoeraCX"),
                    }),
                SharedHttp));
        _forgeBootstrap = new PackForgeInstallerBootstrap(_gameInstall, _runner, SharedHttp);
        _modrinth = modrinth ?? new ModrinthClient(
            SharedHttp,
            new ModrinthClientOptions
            {
                UserAgent = ModrinthClientOptions.CreateUserAgent(
                    ArchoeraBuildInfo.Version,
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

        if (NativePackArchive.TryRead(fullPath, out var native))
        {
            return await InstallNativeAsync(
                    fullPath,
                    native,
                    new PackSource(native.Format, null, native.VersionId, fullPath),
                    name,
                    rootPath,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var (contents, _) = PackArchiveRepair.ReadWithRepair(fullPath, clientSide: true);
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
    /// Downloads a provider-supplied direct archive (Technic non-Solder packs)
    /// and installs its legacy layout. The provider publishes no digest, so the
    /// plan uses the explicit unverified sentinel, the executor still enforces
    /// the declared size, and the archive's ZIP CRCs are validated by
    /// <see cref="NativePackArchive"/> / <see cref="PackArchive.Extract"/>.
    /// The computed SHA-256 is recorded next to the instance provenance.
    /// </summary>
    public async Task<PackInstallResult> InstallDownloadedArchiveAsync(
        string url,
        long size,
        string versionId,
        string name,
        string summary,
        string gameVersion,
        string sourceKind,
        string? sourceProjectId,
        string? sourceVersionId,
        string? requestedName,
        string? rootPath,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new PackException(
                PackErrorCode.Invalid,
                "pack archive URL must be HTTPS");
        }

        if (size <= 0)
        {
            throw new PackException(
                PackErrorCode.Invalid,
                "pack archive size is required");
        }

        var stagingRoot = Path.Combine(
            _settings.DataRoot,
            "staging",
            "packs",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);
        var archivePath = Path.Combine(stagingRoot, "pack.zip");
        try
        {
            var plan = new DownloadPlanBuilder(stagingRoot)
                .AddFile(PlanFile.CreateUnverified(
                    uri.AbsoluteUri,
                    "pack.zip",
                    size))
                .Build();
            var download = await _runner
                .ExecuteAsync(plan, cancellationToken, DownloadProfile.Archive)
                .ConfigureAwait(false);
            if (!download.Completed)
            {
                throw DownloadIncomplete(download, "pack");
            }

            if (!NativePackArchive.TryRead(archivePath, out var native))
            {
                throw new PackException(
                    PackErrorCode.Unsupported,
                    "this Technic archive has no supported legacy launcher metadata");
            }

            var sha256 = await ComputeSha256Async(archivePath, cancellationToken)
                .ConfigureAwait(false);
            native = native with
            {
                Name = string.IsNullOrWhiteSpace(name) ? native.Name : name,
                VersionId = string.IsNullOrWhiteSpace(versionId)
                    ? native.VersionId
                    : versionId,
                Summary = string.IsNullOrWhiteSpace(native.Summary)
                    ? summary
                    : native.Summary,
                GameVersion = string.IsNullOrWhiteSpace(native.GameVersion)
                    ? gameVersion
                    : native.GameVersion,
            };
            var source = new PackSource(
                sourceKind,
                sourceProjectId,
                sourceVersionId,
                null);
            var result = await InstallNativeAsync(
                    archivePath,
                    native,
                    source,
                    requestedName,
                    rootPath,
                    cancellationToken)
                .ConfigureAwait(false);
            result = result with { DownloadedFiles = 1, DownloadedBytes = size };
            WritePackManifest(result.Instance.RootPath, native, source, result);
            WriteArchiveDigest(result.Instance.RootPath, sha256, uri);
            return result;
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

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void WriteArchiveDigest(
        string instanceRoot,
        string sha256,
        Uri archive)
    {
        try
        {
            var directory = Path.Combine(instanceRoot, ".archoera");
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "pack-archive.sha256"),
                $"{sha256}  {Path.GetFileName(archive.LocalPath)}\n");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Provenance is informative; a completed install is still valid.
        }
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
                throw DownloadIncomplete(download, "pack");
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

    private async Task<PackInstallResult> InstallNativeAsync(
        string archivePath,
        NativePackContents pack,
        PackSource source,
        string? requestedName,
        string? rootPath,
        CancellationToken cancellationToken)
    {
        var loaderName = pack.Loader.Kind.Trim().ToLowerInvariant();
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
                $"unsupported pack loader '{pack.Loader.Kind}'");
        }

        EnsureRunnerAvailable();
        var platform = MojangPlatform.Current();
        JavaRuntimeInfo? javaRuntime = null;
        if (forgeKind is not null)
        {
            javaRuntime = await _java
                .SelectAsync(null, pack.GameVersion, cancellationToken)
                .ConfigureAwait(false);
        }

        var instance = await CreateInstanceAsync(
                pack.Name,
                pack.GameVersion,
                pack.Loader,
                requestedName,
                rootPath,
                cancellationToken)
            .ConfigureAwait(false);
        var shareLoaderFiles = (await _settings
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false))
            .ShareLoaderFiles;

        if (forgeKind is { } kind)
        {
            await InstallForgeGameAsync(
                    instance,
                    kind,
                    javaRuntime!.Path,
                    cancellationToken,
                    shareLoaderFiles)
                .ConfigureAwait(false);
        }
        else
        {
            var game = await _gameInstall
                .BuildAsync(
                    instance.RootPath,
                    pack.GameVersion,
                    pack.Loader.Kind,
                    pack.Loader.Version,
                    platform,
                    await ResolveSourceModeAsync(cancellationToken).ConfigureAwait(false),
                    cancellationToken,
                    shareLoaderFiles)
                .ConfigureAwait(false);

            ModuleProgress.Report(
                0,
                game.Plan.TotalBytes,
                0,
                $"Installing Minecraft {pack.GameVersion} + {game.LoaderKind}");
            var gameDownload = await _runner
                .ExecuteAsync(game.Plan, cancellationToken)
                .ConfigureAwait(false);
            if (!gameDownload.Completed)
            {
                throw DownloadIncomplete(gameDownload, "game/loader");
            }

            PackGameInstallService.CommitInstanceProfile(
                instance.RootPath,
                game.VersionJson);
        }

        // Native exports already contain every mod/config; extract them after
        // the game/loader files so an override wins on a path collision.
        var extracted = PackArchive.Extract(
            archivePath,
            instance.RootPath,
            pack.Files,
            cancellationToken,
            (done, total) =>
            {
                if (done == total || done % 64 == 0)
                {
                    ModuleProgress.Report(
                        done,
                        total,
                        0,
                        "Extracting bundled files");
                }
            });

        var result = new PackInstallResult(
            instance,
            pack.VersionId,
            source.Kind,
            0,
            0,
            extracted.Count,
            0);
        WritePackManifest(instance.RootPath, pack, source, result);
        return result;
    }

    /// <summary>
    /// Installs a provider-built index that has no archive (FTB/Technic/
    /// ATLauncher manifests already describe every file).
    /// </summary>
    public Task<PackInstallResult> InstallIndexAsync(
        ModpackIndex index,
        string sourceKind,
        string? sourceProjectId,
        string? sourceVersionId,
        string? name,
        string? rootPath,
        CancellationToken cancellationToken = default)
        => InstallFromArchiveAsync(
            null,
            new PackArchiveContents(index, [], 0, 0),
            new PackSource(sourceKind, sourceProjectId, sourceVersionId, null),
            name,
            rootPath,
            cancellationToken);

    private async Task<PackInstallResult> InstallFromArchiveAsync(
        string? archivePath,
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

        EnsureRunnerAvailable();

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
                throw DownloadIncomplete(gameDownload, "game/loader");
            }

            PackGameInstallService.CommitInstanceProfile(
                instance.RootPath,
                versionJson);
        }

        var overridePaths = contents.Overrides
            .Select(entry => entry.RelativePath)
            .ToHashSet(PathSemantics.Comparer);

        var packPlan = ModpackPlanBuilder.Build(
            instance.RootPath,
            index,
            overridePaths,
            clientSide: true,
            sourceMode: await ResolveSourceModeAsync(cancellationToken)
                .ConfigureAwait(false));

        // Overrides are extracted while index downloads run. They never target
        // the same destination: conflicts were removed from the plan above.
        // Platform manifests (FTB/Technic/ATLauncher) have no archive, so the
        // extraction phase is skipped when only index files exist.
        Task<IReadOnlyList<string>> extractTask;
        if (archivePath is null || contents.Overrides.Count == 0)
        {
            extractTask = Task.FromResult<IReadOnlyList<string>>([]);
        }
        else
        {
            extractTask = Task.Run(
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
        }

        PlanExecutionResult packDownload;
        IReadOnlyList<string> extracted;
        try
        {
            packDownload = await _runner
                .ExecuteAsync(packPlan, cancellationToken, DownloadProfile.Resource)
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
            // Keep `auto` region-driven even when the settings file cannot be
            // read; returning Auto to the plan builder would silently mean
            // "official first".
            return await DownloadSourceResolver
                .ResolveAsync("auto", _regionPolicy, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private Task<GameInstanceRecord> CreateInstanceAsync(
        ModpackIndex index,
        string? requestedName,
        string? rootPath,
        CancellationToken cancellationToken)
        => CreateInstanceAsync(
            index.Name,
            index.MinecraftVersion,
            index.Loader,
            requestedName,
            rootPath,
            cancellationToken);

    private async Task<GameInstanceRecord> CreateInstanceAsync(
        string packName,
        string gameVersion,
        ModpackLoader loader,
        string? requestedName,
        string? rootPath,
        CancellationToken cancellationToken)
    {
        var name = SanitizeInstanceName(requestedName ?? packName);
        try
        {
            return await _repository
                .CreateAsync(
                    name,
                    gameVersion,
                    loader.Kind,
                    loader.Version,
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

    /// <summary>The Rust executor is required before any plan can run.</summary>
    private void EnsureRunnerAvailable()
    {
        if (!_runner.IsAvailable)
        {
            throw new DownloadRunnerException(
                "DOWNLOADER_MISSING",
                "archoera-dl was not found; build app/modules/downloader and set ARCHOERACX_DL_BIN");
        }
    }

    /// <summary>
    /// Builds the stable "download did not finish" error shared by every plan
    /// execution in this service.
    /// </summary>
    private static PackException DownloadIncomplete(
        PlanExecutionResult download,
        string what)
        => new(
            PackErrorCode.DownloadIncomplete,
            $"{what} download {download.Status}" +
            (download.FailedCount > 0
                ? $" ({download.FailedCount} file(s) failed): " +
                  download.FailedSummary
                : string.Empty) +
            TailDetail(download.Stderr));

    private static void WritePackManifest(
        string instanceRoot,
        NativePackContents pack,
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
                writer.WriteString("format", pack.Format);
                writer.WriteString("name", pack.Name);
                writer.WriteString("versionId", pack.VersionId);
                writer.WriteString("gameVersion", pack.GameVersion);
                writer.WriteString("loader", pack.Loader.Kind);
                writer.WriteString("loaderVersion", pack.Loader.Version);
                writer.WriteString("source", source.Kind);
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
                writer.WriteString("format", source.Kind);
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
