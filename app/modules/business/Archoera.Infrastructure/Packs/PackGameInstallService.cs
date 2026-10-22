using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Archoera.Application.Downloads;
using Archoera.Application.Loaders;
using Archoera.Application.Mojang;
using Archoera.Application.Packs;
using Archoera.Domain.Downloads;
using Archoera.Domain.Loaders;
using Archoera.Domain.Mojang;
using Archoera.Infrastructure.Launch;
using Archoera.Infrastructure.Loaders;
using Archoera.Infrastructure.Mojang;

namespace Archoera.Infrastructure.Packs;

/// <summary>Built game/loader phase for a pack install.</summary>
public sealed record PackGameInstallPlan(
    DownloadPlan Plan,
    string VersionId,
    string VersionJson,
    string LoaderKind,
    string LoaderVersion);

/// <summary>Forge/NeoForge phase: combined plan + merged version requirements.</summary>
public sealed record PackForgeInstallPlan(
    DownloadPlan Plan,
    string VersionId,
    string VersionJson,
    int? RequiredJavaMajor);

/// <summary>Loader profile endpoints, injectable for tests and mirrors.</summary>
public sealed record LoaderProfileEndpoints(Uri Fabric, Uri Quilt)
{
    public static LoaderProfileEndpoints Default { get; } = new(
        new Uri("https://meta.fabricmc.net/v2/"),
        new Uri("https://meta.quiltmc.org/v3/"));
}

/// <summary>
/// Builds the vanilla + Fabric/Quilt download plan for a new pack instance.
/// Fabric and Quilt installs are profile-JSON based and need no installer JVM;
/// Forge/NeoForge packs are rejected until the installer/processor flow is
/// wired to this entry point.
/// </summary>
public sealed class PackGameInstallService
{
    private static readonly MediaTypeWithQualityHeaderValue AcceptJson =
        new("application/json");

    private readonly IMojangMetadataClient _mojang;
    private readonly HttpClient _http;
    private readonly LoaderProfileEndpoints _endpoints;
    private readonly MojangPlanService _planService;
    private readonly VersionMergeService _merge = new();
    private readonly MavenSidecarClient _maven;

    public PackGameInstallService(
        IMojangMetadataClient? mojang = null,
        HttpClient? httpClient = null,
        LoaderProfileEndpoints? endpoints = null)
    {
        _mojang = mojang ?? new MojangClient(
            new HttpClient(),
            new MojangClientOptions
            {
                UserAgent = MojangClientOptions.CreateUserAgent(
                    "0.0.1+1",
                    "https://github.com/BetaStudio2/ArchoeraCX"),
            });
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        if (httpClient is null)
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "ArchoeraCX/0.0.1+1 (https://github.com/BetaStudio2/ArchoeraCX)");
        }

        _endpoints = endpoints ?? LoaderProfileEndpoints.Default;
        _planService = new MojangPlanService(_mojang);
        _maven = new MavenSidecarClient(
            _http,
            ModrinthUserAgent);
    }

    private const string ModrinthUserAgent =
        "ArchoeraCX/0.0.1+1 (https://github.com/BetaStudio2/ArchoeraCX)";

    /// <summary>Builds the plan and the version JSON to commit afterwards.</summary>
    /// <param name="instanceRoot">Absolute instance directory (must exist).</param>
    /// <param name="gameVersion">Exact Minecraft version from the pack.</param>
    /// <param name="loaderKind">vanilla, fabric or quilt.</param>
    /// <param name="loaderVersion">Exact loader build (empty for vanilla).</param>
    /// <param name="platform">Target OS/arch.</param>
    public async Task<PackGameInstallPlan> BuildAsync(
        string instanceRoot,
        string gameVersion,
        string loaderKind,
        string loaderVersion,
        MojangPlatform platform,
        DownloadSourceMode sourceMode = DownloadSourceMode.Auto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameVersion);

        var kind = loaderKind.Trim().ToLowerInvariant();
        var installRoot = LaunchPaths.ResolveInstallRoot(instanceRoot);

        if (kind == "vanilla")
        {
            var vanillaPlan = await _planService
                .BuildForVersionAsync(
                    installRoot,
                    gameVersion,
                    platform,
                    sourceMode,
                    cancellationToken)
                .ConfigureAwait(false);
            var json = await _mojang
                .GetVersionJsonAsync(gameVersion, cancellationToken)
                .ConfigureAwait(false);
            return new PackGameInstallPlan(
                vanillaPlan,
                gameVersion,
                Encoding.UTF8.GetString(json).TrimStart('\uFEFF'),
                "vanilla",
                string.Empty);
        }

        if (kind is not ("fabric" or "quilt"))
        {
            throw new PackException(
                PackErrorCode.LoaderUnsupported,
                $"{loaderKind} packs are not supported yet; " +
                "install the loader manually or use a Fabric/Quilt/vanilla pack");
        }

        if (string.IsNullOrWhiteSpace(loaderVersion))
        {
            throw new PackException(
                PackErrorCode.Invalid,
                $"pack declares {kind} but no loader version");
        }

        var profileJson = await FetchProfileAsync(
            kind,
            gameVersion,
            loaderVersion,
            cancellationToken).ConfigureAwait(false);
        var profileId = ReadProfileId(profileJson)
            ?? $"{kind}-loader-{loaderVersion}-{gameVersion}";
        if (!DownloadPlanPaths.IsPlainFileName(profileId))
        {
            throw new PackException(
                PackErrorCode.Invalid,
                $"loader profile id '{profileId}' is not a safe file name");
        }

        var inherits = ReadInheritsFrom(profileJson);
        if (string.IsNullOrWhiteSpace(inherits) ||
            !string.Equals(inherits, gameVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new PackException(
                PackErrorCode.Invalid,
                $"loader profile inherits '{inherits ?? "<none>"}', " +
                $"expected '{gameVersion}'");
        }

        var vanillaJson = await _mojang
            .GetVersionJsonAsync(gameVersion, cancellationToken)
            .ConfigureAwait(false);

        var effective = await _merge
            .MergeAsync(
                profileId,
                (id, _) =>
                {
                    if (string.Equals(id, profileId, StringComparison.Ordinal))
                    {
                        return Task.FromResult<VersionLayer?>(
                            new VersionLayer(profileId, profileJson, "loader"));
                    }

                    if (string.Equals(id, gameVersion, StringComparison.Ordinal))
                    {
                        return Task.FromResult<VersionLayer?>(
                            new VersionLayer(
                                gameVersion,
                                Encoding.UTF8.GetString(vanillaJson).TrimStart('\uFEFF'),
                                "mojang"));
                    }

                    return Task.FromResult<VersionLayer?>(null);
                },
                cancellationToken)
            .ConfigureAwait(false);

        var plan = await _planService
            .BuildFromEffectiveAsync(
                installRoot,
                effective,
                platform,
                sourceMode,
                cancellationToken)
            .ConfigureAwait(false);

        return new PackGameInstallPlan(
            plan,
            profileId,
            Encoding.UTF8.GetString(effective.Json).TrimStart('\uFEFF'),
            kind,
            loaderVersion);
    }

    /// <summary>Writes the committed version JSON under <c>versions/&lt;id&gt;/</c>.</summary>
    public static string CommitVersionJson(
        string instanceRoot,
        string versionId,
        string versionJson)
    {
        if (!DownloadPlanPaths.IsPlainFileName(versionId))
        {
            throw new PackException(
                PackErrorCode.Invalid,
                $"version id '{versionId}' is not a safe file name");
        }

        var installRoot = LaunchPaths.ResolveInstallRoot(instanceRoot);
        var directory = Path.Combine(installRoot, "versions", versionId);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{versionId}.json");
        var temporary = path + ".tmp";
        File.WriteAllText(
            temporary,
            versionJson,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, path, overwrite: true);
        return path;
    }

    /// <summary>
    /// Resolves the official Forge/NeoForge installer URL, SHA-1 and size. The
    /// caller downloads it (through Rust when possible) before the combined
    /// plan can be built.
    /// </summary>
    public async Task<ForgeInstallerDescriptor> ResolveForgeInstallerAsync(
        LoaderKind kind,
        string gameVersion,
        string loaderVersion,
        CancellationToken cancellationToken = default)
    {
        var uri = ForgeMavenUrls.Installer(kind, gameVersion, loaderVersion);
        var metadata = await _maven
            .GetMetadataAsync(uri, cancellationToken)
            .ConfigureAwait(false);
        return new ForgeInstallerDescriptor(
            uri.AbsoluteUri,
            metadata.Sha1,
            metadata.Size);
    }

    /// <summary>
    /// Builds the vanilla + Forge/NeoForge plan from an already downloaded
    /// installer. Rust still performs every download; this method only reads
    /// metadata and constructs the validated plan.
    /// </summary>
    public async Task<PackForgeInstallPlan> BuildForgeFromInstallerAsync(
        string instanceRoot,
        LoaderKind kind,
        string gameVersion,
        string loaderVersion,
        string installerPath,
        MojangPlatform platform,
        bool preferMavenMirror,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);

        var installRoot = LaunchPaths.ResolveInstallRoot(instanceRoot);
        var options = preferMavenMirror
            ? new ForgeDownloadPlanOptions { MavenMirror = ForgeMavenMirror.Bmclapi }
            : null;

        var result = await new ForgeInstallPlanService(_mojang)
            .BuildAsync(
                installRoot,
                kind,
                gameVersion,
                loaderVersion,
                installerPath,
                platform,
                cancellationToken,
                options)
            .ConfigureAwait(false);

        var plan = result.Plan;
        if (preferMavenMirror)
        {
            plan = ForgePlanMirror.Apply(plan, ForgeMavenMirror.Bmclapi);
        }

        return new PackForgeInstallPlan(
            plan,
            result.Version.Id,
            Encoding.UTF8.GetString(result.Version.Json).TrimStart('\uFEFF'),
            result.Version.Version.JavaVersion?.MajorVersion);
    }

    private async Task<string> FetchProfileAsync(
        string kind,
        string gameVersion,
        string loaderVersion,
        CancellationToken cancellationToken)
    {
        var baseAddress = kind == "fabric" ? _endpoints.Fabric : _endpoints.Quilt;
        var endpoint = new Uri(
            baseAddress,
            $"versions/loader/{Uri.EscapeDataString(gameVersion)}/" +
            $"{Uri.EscapeDataString(loaderVersion)}/profile/json");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.Add(AcceptJson);

        try
        {
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new PackException(
                    PackErrorCode.LoaderMetadata,
                    $"{kind} profile request failed with HTTP " +
                    $"{(int)response.StatusCode}");
            }

            await using var stream = await response.Content
                .ReadAsStreamAsync(timeout.Token)
                .ConfigureAwait(false);
            using var memory = new MemoryStream();
            var buffer = new byte[16 * 1024];
            while (true)
            {
                var read = await stream
                    .ReadAsync(buffer, timeout.Token)
                    .ConfigureAwait(false);
                if (read == 0) break;
                if (memory.Length + read > 4 * 1024 * 1024)
                {
                    throw new PackException(
                        PackErrorCode.LoaderMetadata,
                        $"{kind} profile response is too large");
                }

                memory.Write(buffer, 0, read);
            }

            return Encoding.UTF8
                .GetString(memory.ToArray())
                .TrimStart('\uFEFF');
        }
        catch (PackException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or
            OperationCanceledException or JsonException)
        {
            throw new PackException(
                PackErrorCode.LoaderMetadata,
                $"{kind} profile request failed: {exception.Message}",
                exception);
        }
    }

    private static string? ReadProfileId(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                   root.TryGetProperty("id", out var id) &&
                   id.ValueKind == JsonValueKind.String &&
                   !string.IsNullOrWhiteSpace(id.GetString())
                ? id.GetString()!.Trim()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadInheritsFrom(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                   root.TryGetProperty("inheritsFrom", out var parent) &&
                   parent.ValueKind == JsonValueKind.String
                ? parent.GetString()?.Trim()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
