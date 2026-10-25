using System.Text;
using System.Text.Json;
using Archoera.Application;
using Archoera.Application.Downloads;
using Archoera.Application.Loaders;
using Archoera.Application.Mojang;
using Archoera.Application.Modules;
using Archoera.Application.Packs;
using Archoera.Domain.Downloads;
using Archoera.Domain.Loaders;
using Archoera.Infrastructure.Downloads;
using Archoera.Infrastructure.Modules;
using Archoera.Infrastructure.Mojang;
using Archoera.Infrastructure.Launch;
using Archoera.Infrastructure.Packs;
using Archoera.Infrastructure.Settings;
using Archoera.Domain.Mojang;

namespace Archoera.Infrastructure.Loaders;

/// <summary>Registered handler for <c>loader.verify</c> / <c>loader.repair</c>.</summary>
public sealed class LoaderModuleService : IModuleMethodHandler
{
    private readonly string[] _supportedMethods;
    private readonly IForgeArtifactPlanner _planner;
    private readonly ILoaderArtifactVerifier _verifier;
    private readonly IForgeRepairService _repairService;
    private readonly DownloadPolicy _policy;
    private readonly JsonAppSettingsStore _settings;
    private readonly RegionPolicy _regionPolicy;

    public LoaderModuleService(
        IForgeArtifactPlanner? planner = null,
        ILoaderArtifactVerifier? verifier = null,
        IForgeRepairService? repairService = null,
        bool enableDownloads = true,
        DownloadPolicy? policy = null,
        JsonAppSettingsStore? settings = null,
        RegionPolicy? regionPolicy = null)
    {
        _policy = policy ?? new DownloadPolicy();
        _settings = settings ?? new JsonAppSettingsStore();
        _regionPolicy = regionPolicy ?? new RegionPolicy();
        _supportedMethods = enableDownloads
            ? ["loader.verify", "loader.repair", "loader.check", "loader.plan", "loader.commit"]
            : ["loader.verify", "loader.repair", "loader.check"];
        _planner = planner ?? new ForgeArtifactPlanner();
        _verifier = verifier ?? new LoaderArtifactVerifier();
        _repairService = repairService ?? new ForgeRepairService(
            new ForgeProcessorRunner(),
            _verifier);
    }

    public IReadOnlyCollection<string> Methods => _supportedMethods;

    public async Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        if (string.Equals(method, "loader.check", StringComparison.Ordinal))
        {
            return await CheckAsync(paramsJson, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(method, "loader.plan", StringComparison.Ordinal) ||
            string.Equals(method, "loader.commit", StringComparison.Ordinal))
        {
            try
            {
                await _policy.EnsureEnabledAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DownloadPolicyException exception)
            {
                return ModuleJson.Error(exception.Code, exception.Message);
            }
        }

        if (string.Equals(method, "loader.plan", StringComparison.Ordinal))
        {
            return await PlanAsync(paramsJson, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(method, "loader.commit", StringComparison.Ordinal))
        {
            return await CommitAsync(paramsJson, cancellationToken).ConfigureAwait(false);
        }

        if (!LoaderRequestJson.TryParse(paramsJson, out var request, out _, out _))
        {
            return ModuleJson.Error("E_INVALID", "loader params are not a valid request");
        }

        try
        {
            var librariesPath = Path.Combine(request!.Root, request.LibrariesPath);
            var manifest = await _planner
                .ParseAsync(
                    request.InstallerPath,
                    request.Root,
                    librariesPath,
                    request.Kind,
                    cancellationToken)
                .ConfigureAwait(false);

            var verification = _verifier.Verify(manifest, request.Root, request.Mode);
            if (string.Equals(method, "loader.verify", StringComparison.Ordinal))
            {
                return LoaderManifestJson.BuildEnvelope(
                    manifest,
                    verification,
                    mode: request.Mode);
            }

            var result = await _repairService
                .RepairAsync(
                    manifest,
                    request.Root,
                    request.JavaPath ?? string.Empty,
                    cancellationToken)
                .ConfigureAwait(false);

            return LoaderManifestJson.BuildEnvelope(manifest, result.Verification, result);
        }
        catch (LoaderManifestException exception)
        {
            return LoaderManifestJson.BuildError(exception.Code, exception.Message);
        }
    }

    private async Task<byte[]> PlanAsync(
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
            // Shared game root (default) or per-instance files when the user
            // unticks "share loader files" in the install dialog.
            var shareLoaderFiles =
                !root.TryGetProperty("shareLoaderFiles", out var shareElement) ||
                shareElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                shareElement.GetBoolean();
            if (string.IsNullOrWhiteSpace(instanceRoot) ||
                !Path.IsPathFullyQualified(instanceRoot) ||
                string.IsNullOrWhiteSpace(kindText) ||
                string.IsNullOrWhiteSpace(gameVersion) ||
                string.IsNullOrWhiteSpace(loaderVersion))
            {
                return ModuleJson.Error(
                    "E_INVALID",
                    "loader.plan requires root, kind, gameVersion and loaderVersion");
            }

            var loaderKind = kindText.Trim().ToLowerInvariant();
            var sourceMode = await ResolveSourceModeAsync(cancellationToken)
                .ConfigureAwait(false);
            var fullInstanceRoot = Path.GetFullPath(instanceRoot);
            var loaderRoot = LaunchPaths.ResolveLoaderRoot(
                fullInstanceRoot,
                shareLoaderFiles);

            // Fabric/Quilt are profile-only loaders: no installer and no
            // processors, so they can be installed directly from the profile
            // JSON. Forge/NeoForge still need a local installer.
            if (loaderKind is "fabric" or "quilt")
            {
                var profilePlan = await new PackGameInstallService()
                    .BuildAsync(
                        fullInstanceRoot,
                        gameVersion,
                        loaderKind,
                        loaderVersion,
                        MojangPlatform.Current(),
                        sourceMode,
                        cancellationToken,
                        shareLoaderFiles)
                    .ConfigureAwait(false);
                var profilePlanJson = DownloadPlanJson.Build(profilePlan.Plan);
                using var profileDocument = JsonDocument.Parse(profilePlanJson);
                return BuildEnvelope(writer =>
                {
                    writer.WritePropertyName("plan");
                    profileDocument.RootElement.WriteTo(writer);
                    writer.WriteString("versionId", profilePlan.VersionId);
                    writer.WriteString("versionJson", profilePlan.VersionJson);
                    writer.WriteBoolean("requiresJava", false);
                    writer.WriteString("kind", profilePlan.LoaderKind);
                    writer.WriteString("installRoot", loaderRoot);
                    writer.WriteBoolean("shareLoaderFiles", shareLoaderFiles);
                });
            }

            if (string.IsNullOrWhiteSpace(installerPath) ||
                !Path.IsPathFullyQualified(installerPath) ||
                !TryParseKind(kindText, out var kind))
            {
                return ModuleJson.Error(
                    "E_INVALID",
                    "loader.plan requires installerPath for Forge/NeoForge");
            }

            using var http = new HttpClient();
            var userAgent = MojangClientOptions.CreateUserAgent(
                ArchoeraBuildInfo.Version,
                "https://github.com/BetaStudio2/ArchoeraCX");
            var mojang = new MojangClient(
                http,
                new MojangClientOptions { UserAgent = userAgent });
            var libraryResolver = new MojangLibraryMetadataResolver(
                new MavenSidecarClient(http, userAgent));
            loaderVersion = LoaderVersionText.Normalize(
                kind.ToString(),
                gameVersion,
                loaderVersion);
            var plan = await new ForgeInstallPlanService(
                    mojang,
                    libraryResolver.ResolveAsync)
                .BuildAsync(
                    loaderRoot,
                    kind,
                    gameVersion,
                    loaderVersion,
                    Path.GetFullPath(installerPath),
                    MojangPlatform.Current(),
                    cancellationToken,
                    sourceMode: sourceMode)
                .ConfigureAwait(false);

            var planJson = DownloadPlanJson.Build(plan.Plan);
            using var planDocument = JsonDocument.Parse(planJson);
            var versionJson = Encoding.UTF8.GetString(plan.Version.Json);
            // The plan writes the installer next to the libraries it installs.
            // Repair needs the absolute path, so report it instead of making
            // the caller guess the layout.
            var planInstaller = Path.Combine(
                loaderRoot,
                ForgeMavenUrls.DefaultInstallerRelativePath(kind, gameVersion, loaderVersion)
                    .Replace('/', Path.DirectorySeparatorChar));

            return BuildEnvelope(writer =>
            {
                writer.WritePropertyName("plan");
                planDocument.RootElement.WriteTo(writer);
                writer.WriteString("versionId", plan.Version.Id);
                writer.WriteString("versionJson", versionJson);
                writer.WriteBoolean("requiresJava", true);
                writer.WriteString("kind", kind.ToString().ToLowerInvariant());
                writer.WriteString("installerPath", planInstaller);
                // Callers commit and repair against this root so the loader
                // files land where the plan put them.
                writer.WriteString("installRoot", loaderRoot);
                writer.WriteBoolean("shareLoaderFiles", shareLoaderFiles);
            });
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or ArgumentException or
            ForgeDownloadException or MojangException or DownloadPlanException or
            PackException)
        {
            return ModuleJson.Error("LOADER_PLAN_FAILED", exception.Message);
        }
    }

    /// <summary>
    /// Resolve the user's source choice against the shared region policy so a
    /// Forge/NeoForge plan uses the same host order as vanilla and resource
    /// downloads. `Auto` must never reach the plan builder unresolved.
    /// </summary>
    private async Task<DownloadSourceMode> ResolveSourceModeAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var settings = await _settings
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false);
            return await DownloadSourceResolver
                .ResolveAsync(settings.DownloadSource, _regionPolicy, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return await DownloadSourceResolver
                .ResolveAsync("auto", _regionPolicy, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private Task<byte[]> CommitAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(paramsJson ?? "{}");
            var root = document.RootElement;
            var instanceRoot = ReadString(root, "root");
            var versionId = ReadString(root, "versionId");
            var versionJson = ReadString(root, "versionJson");
            if (string.IsNullOrWhiteSpace(instanceRoot) ||
                !Path.IsPathFullyQualified(instanceRoot) ||
                string.IsNullOrWhiteSpace(versionId) ||
                versionId.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0 ||
                string.IsNullOrWhiteSpace(versionJson))
            {
                return Task.FromResult(ModuleJson.Error(
                    "E_INVALID",
                    "loader.commit requires root, versionId and versionJson"));
            }

            var versionDirectory = Path.Combine(instanceRoot, "versions", versionId);
            Directory.CreateDirectory(versionDirectory);
            var versionPath = Path.Combine(versionDirectory, $"{versionId}.json");
            var temporary = versionPath + ".tmp";
            File.WriteAllText(
                temporary,
                versionJson,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, versionPath, overwrite: true);
            return Task.FromResult(BuildEnvelope(writer =>
            {
                writer.WriteString("versionId", versionId);
                writer.WriteString("versionPath", versionPath);
            }));
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return Task.FromResult(ModuleJson.Error("LOADER_COMMIT_FAILED", exception.Message));
        }
    }

    private static byte[] BuildEnvelope(Action<Utf8JsonWriter> data)
    {
        using var memory = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memory, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", true);
            writer.WriteStartObject("data");
            data(writer);
            writer.WriteEndObject();
            writer.WriteStartObject("meta");
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return memory.ToArray();
    }

    private async Task<byte[]> CheckAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(paramsJson ?? "{}");
            var rootElement = document.RootElement;
            var instanceRoot = ReadString(rootElement, "root");
            var kindText = ReadString(rootElement, "kind");
            var gameVersion = ReadString(rootElement, "gameVersion");
            var loaderVersion = ReadString(rootElement, "loaderVersion");
            var loaderName = kindText?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(instanceRoot) ||
                !Path.IsPathFullyQualified(instanceRoot) ||
                string.IsNullOrWhiteSpace(gameVersion) ||
                (loaderName != "vanilla" && string.IsNullOrWhiteSpace(loaderVersion)) ||
                !IsCheckableLoader(loaderName))
            {
                return ModuleJson.Error(
                    "E_INVALID",
                    "loader.check requires root, kind (vanilla/fabric/quilt/forge/neoforge), " +
                    "gameVersion and loaderVersion");
            }

            var modeText = ReadString(rootElement, "mode");
            if (modeText is not null &&
                !LoaderVerifyModeExtensions.TryParse(modeText, out _))
            {
                return ModuleJson.Error("E_INVALID", "mode must be 'quick' or 'full'");
            }
            LoaderVerifyModeExtensions.TryParse(modeText, out var mode);

            var launchFiles = CheckLaunchFiles(
                Path.GetFullPath(instanceRoot),
                loaderName!,
                gameVersion,
                loaderVersion ?? string.Empty);

            if (loaderName is not ("forge" or "neoforge"))
            {
                return LoaderManifestJson.BuildLaunchOnlyEnvelope(
                    mode,
                    launchFiles.Ok,
                    launchFiles.Code,
                    launchFiles.Message);
            }

            var kind = loaderName == "forge" ? LoaderKind.Forge : LoaderKind.NeoForge;
            var relative = ForgeMavenUrls.DefaultInstallerRelativePath(
                kind,
                gameVersion,
                loaderVersion!);
            // Libraries live either in the instance (isolated install) or in
            // the shared game root; follow whichever layout is on disk.
            var loaderRoot = LaunchPaths.ResolveLoaderRoot(instanceRoot, null);
            // The installer follows the plan root, but pack installs keep it in
            // the instance, so accept both locations.
            var installerPath = new[]
                {
                    Path.GetFullPath(instanceRoot),
                    loaderRoot,
                }
                .Select(root => Path.Combine(
                    root,
                    relative.Replace('/', Path.DirectorySeparatorChar)))
                .FirstOrDefault(File.Exists);
            if (installerPath is null)
            {
                return LoaderManifestJson.BuildError(
                    "MANIFEST_MISSING",
                    $"installer not found: {Path.Combine(
                        Path.GetFullPath(instanceRoot),
                        relative.Replace('/', Path.DirectorySeparatorChar))}");
            }

            var librariesPath = Path.Combine(loaderRoot, "libraries");
            var manifest = await _planner
                .ParseAsync(installerPath, loaderRoot, librariesPath, kind, cancellationToken)
                .ConfigureAwait(false);
            var verification = _verifier.Verify(manifest, loaderRoot, mode);
            return LoaderManifestJson.BuildEnvelope(
                manifest,
                verification,
                mode: mode,
                launchFilesOk: launchFiles.Ok,
                launchFilesCode: launchFiles.Code,
                launchFilesMessage: launchFiles.Message);
        }
        catch (LoaderManifestException exception)
        {
            return LoaderManifestJson.BuildError(exception.Code, exception.Message);
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or ArgumentException)
        {
            return LoaderManifestJson.BuildError("E_INVALID", exception.Message);
        }
    }

    private static bool IsCheckableLoader(string? loaderName)
        => loaderName is "vanilla" or "fabric" or "quilt" or "forge" or "neoforge";

    private static LaunchFileCheck CheckLaunchFiles(
        string instanceRoot,
        string loaderName,
        string gameVersion,
        string loaderVersion)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var record = new Archoera.Domain.Instances.GameInstanceRecord(
                "dependencies",
                Path.GetFileName(Path.TrimEndingDirectorySeparator(instanceRoot)),
                gameVersion,
                loaderName,
                loaderVersion,
                Path.GetFullPath(instanceRoot),
                0,
                false,
                now,
                now,
                null);
            var paths = LaunchPaths.Resolve(record.RootPath);
            var version = LaunchModuleService.LoadVersion(record, paths);
            return LaunchPlanBuilder.CheckFiles(version, paths);
        }
        catch (LaunchException exception)
        {
            return new LaunchFileCheck(false, exception.Code, exception.Message);
        }
        catch (Exception exception) when (
            exception is IOException or ArgumentException or InvalidOperationException)
        {
            return new LaunchFileCheck(false, "LAUNCH_FILES_UNREADABLE", exception.Message);
        }
    }

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) &&
           element.ValueKind == JsonValueKind.String &&
           !string.IsNullOrWhiteSpace(element.GetString())
            ? element.GetString()
            : null;

    private static bool TryParseKind(string? value, out LoaderKind kind)
    {
        if (string.Equals(value, "forge", StringComparison.OrdinalIgnoreCase))
        {
            kind = LoaderKind.Forge;
            return true;
        }

        if (string.Equals(value, "neoforge", StringComparison.OrdinalIgnoreCase))
        {
            kind = LoaderKind.NeoForge;
            return true;
        }

        kind = default;
        return false;
    }
}
