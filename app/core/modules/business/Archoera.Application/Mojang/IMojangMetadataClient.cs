using Archoera.Domain.Mojang;

namespace Archoera.Application.Mojang;

/// <summary>
/// Metadata-only Mojang operations used by <see cref="MojangPlanService"/>.
/// Implementations fetch and verify metadata but never download game files.
/// </summary>
public interface IMojangMetadataClient
{
    /// <summary>Fetches and parses <c>version_manifest_v2.json</c>.</summary>
    Task<MojangVersionManifest> GetManifestAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves <paramref name="versionId"/> through the manifest, verifies the
    /// version JSON against the manifest SHA-1 and parses it.
    /// </summary>
    /// <exception cref="MojangException"><c>NOT_FOUND</c>, <c>HASH_MISMATCH</c>, ...</exception>
    Task<MojangVersion> GetVersionAsync(
        string versionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the raw version JSON bytes after verifying the manifest SHA-1.
    /// Loader merge needs the original document, not a re-serialized model.
    /// </summary>
    Task<byte[]> GetVersionJsonAsync(
        string versionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the asset index, verifies it against the ref's SHA-1/size and
    /// parses its <c>objects</c> map.
    /// </summary>
    /// <exception cref="MojangException"><c>MISSING_HASH</c>, <c>HASH_MISMATCH</c>, ...</exception>
    Task<MojangAssetIndex> GetAssetIndexAsync(
        MojangAssetIndexRef index,
        CancellationToken cancellationToken = default);
}
