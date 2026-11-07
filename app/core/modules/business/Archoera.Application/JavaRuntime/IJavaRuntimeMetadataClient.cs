using Archoera.Domain.Java;

namespace Archoera.Application.JavaRuntime;

/// <summary>
/// Metadata-only Java-runtime operations. Implementations fetch and verify
/// catalog/manifest documents; they never download runtime files.
/// </summary>
public interface IJavaRuntimeMetadataClient
{
    /// <summary>Fetches and parses <c>all.json</c>.</summary>
    Task<JavaRuntimeCatalog> GetCatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the component's <c>manifest.url</c>, verifies the published
    /// SHA-1 and size, then parses it.
    /// </summary>
    /// <exception cref="JavaRuntimeException">
    /// <c>MISSING_HASH</c>, <c>MISSING_SIZE</c>, <c>HASH_MISMATCH</c>, ...
    /// </exception>
    Task<JavaRuntimeManifest> GetRuntimeManifestAsync(
        JavaRuntimeComponent component,
        CancellationToken cancellationToken = default);
}
