using System.Text.Json;
using Archoera.Application.Downloads;
using Archoera.Domain.Downloads;
using Archoera.Domain.Mojang;

namespace Archoera.Application.Mojang;

/// <summary>
/// High-level vanilla install entry point: manifest → version JSON → asset index
/// → <see cref="DownloadPlan"/>. It performs metadata requests only; game files
/// are downloaded later by the Rust executor.
/// </summary>
public sealed class MojangPlanService
{
    private readonly IMojangMetadataClient _client;
    private readonly LibraryArtifactResolver? _libraryResolver;

    /// <summary>Creates the service over a metadata client.</summary>
    /// <param name="client">Mojang metadata transport.</param>
    /// <param name="libraryResolver">
    /// Optional Maven sidecar resolver for loader profiles whose libraries only
    /// carry a coordinate.
    /// </param>
    public MojangPlanService(
        IMojangMetadataClient client,
        LibraryArtifactResolver? libraryResolver = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _libraryResolver = libraryResolver;
    }

    /// <summary>
    /// Fetches everything required for <paramref name="versionId"/> and builds a
    /// vanilla plan for <paramref name="platform"/>.
    /// </summary>
    /// <param name="root">Absolute instance/shared root.</param>
    /// <param name="versionId">Exact manifest id; "latest" is never resolved implicitly.</param>
    /// <param name="platform">Target OS/arch/features.</param>
    /// <param name="cancellationToken">Cancels metadata requests.</param>
    /// <exception cref="MojangException">
    /// Propagates <c>NOT_FOUND</c>/<c>NETWORK_ERROR</c>/<c>HASH_MISMATCH</c>/...;
    /// loader versions fail with <c>UNSUPPORTED_INHERITS</c>.
    /// </exception>
    public async Task<DownloadPlan> BuildForVersionAsync(
        string root,
        string versionId,
        MojangPlatform platform,
        DownloadSourceMode sourceMode = DownloadSourceMode.Auto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platform);

        if (string.IsNullOrWhiteSpace(root))
        {
            throw new MojangException(
                MojangErrorCode.InvalidRequest,
                "root is required");
        }

        if (string.IsNullOrWhiteSpace(versionId))
        {
            throw new MojangException(
                MojangErrorCode.InvalidRequest,
                "versionId is required");
        }

        var version = await _client
            .GetVersionAsync(versionId.Trim(), cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(version.InheritsFrom))
        {
            throw new MojangException(
                MojangErrorCode.UnsupportedInherits,
                $"version '{version.Id}' inherits from '{version.InheritsFrom}'; " +
                "loader/version merging is not implemented yet");
        }

        version = await MojangLibraryPlanResolver
            .ApplyAsync(version, _libraryResolver, cancellationToken)
            .ConfigureAwait(false);

        MojangAssetIndex? assetIndex = null;
        if (version.AssetIndex is not null)
        {
            assetIndex = await _client
                .GetAssetIndexAsync(version.AssetIndex, cancellationToken)
                .ConfigureAwait(false);
        }

        return VanillaDownloadPlanBuilder.Build(
            root,
            version,
            assetIndex,
            platform,
            version.AssetIndex,
            sourceMode);
    }

    /// <summary>
    /// Builds a plan from an already merged/effective version JSON. This is
    /// the bridge for loader profiles: call <c>VersionMergeService</c> first,
    /// then pass <see cref="EffectiveVersion.Json"/> here.
    /// </summary>
    /// <exception cref="MojangException">
    /// <c>UNSUPPORTED_INHERITS</c> if the input still has an <c>inheritsFrom</c>
    /// field, or the same metadata errors as <see cref="BuildForVersionAsync"/>.
    /// </exception>
    public async Task<DownloadPlan> BuildFromVersionJsonAsync(
        string root,
        ReadOnlyMemory<byte> versionJson,
        MojangPlatform platform,
        DownloadSourceMode sourceMode = DownloadSourceMode.Auto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platform);

        if (string.IsNullOrWhiteSpace(root))
        {
            throw new MojangException(
                MojangErrorCode.InvalidRequest,
                "root is required");
        }

        MojangVersion version;
        try
        {
            using var document = JsonDocument.Parse(versionJson);
            version = MojangJson.ParseVersion(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new MojangException(
                MojangErrorCode.InvalidResponse,
                $"effective version JSON is invalid: {exception.Message}");
        }

        if (!string.IsNullOrWhiteSpace(version.InheritsFrom))
        {
            throw new MojangException(
                MojangErrorCode.UnsupportedInherits,
                $"version '{version.Id}' still inherits from " +
                $"'{version.InheritsFrom}'; run VersionMergeService first");
        }

        version = await MojangLibraryPlanResolver
            .ApplyAsync(version, _libraryResolver, cancellationToken)
            .ConfigureAwait(false);

        MojangAssetIndex? assetIndex = null;
        if (version.AssetIndex is not null)
        {
            assetIndex = await _client
                .GetAssetIndexAsync(version.AssetIndex, cancellationToken)
                .ConfigureAwait(false);
        }

        return VanillaDownloadPlanBuilder.Build(
            root,
            version,
            assetIndex,
            platform,
            version.AssetIndex,
            sourceMode);
    }

    /// <summary>Convenience overload for <see cref="EffectiveVersion"/>.</summary>
    public Task<DownloadPlan> BuildFromEffectiveAsync(
        string root,
        EffectiveVersion effective,
        MojangPlatform platform,
        DownloadSourceMode sourceMode = DownloadSourceMode.Auto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(effective);
        return BuildFromVersionJsonAsync(
            root,
            effective.Json,
            platform,
            sourceMode,
            cancellationToken);
    }

    // NEXT STEP: build a Java runtime plan from Mojang all.json
    // (https://launchermeta.mojang.com/v1/products/java-runtime/<hash>/all.json)
    // keyed by platform and version.javaVersion.component. Not part of the first
    // download slice.
}
