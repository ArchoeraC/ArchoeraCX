using System.Text;
using System.Text.Json;
using Archoera.Application;
using Archoera.Application.Instances;
using Archoera.Application.Downloads;
using Archoera.Application.Loaders;
using Archoera.Application.Modules;
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
            new ForgeProcessorRunner(),
            new LoaderArtifactVerifier());
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

            PackGameInstallService.CommitVersionJson(
                instance.RootPath,
                versionId,
                versionJson,
                shareLoaderFiles);
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
