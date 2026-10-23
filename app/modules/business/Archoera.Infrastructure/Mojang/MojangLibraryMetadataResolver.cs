using Archoera.Application.Loaders;
using Archoera.Domain.Downloads;
using Archoera.Domain.Mojang;
using Archoera.Infrastructure.Loaders;

namespace Archoera.Infrastructure.Mojang;

/// <summary>
/// Resolves the missing hash/size of a Maven library through the repository's
/// <c>.sha1</c> sidecar and <c>Content-Length</c>. Loader profiles routinely
/// omit both (<c>fabric-loader</c>, <c>intermediary</c>, Quilt artifacts), but
/// the download plan must stay hash-verified.
/// </summary>
public sealed class MojangLibraryMetadataResolver
{
    private readonly MavenSidecarClient _sidecar;

    public MojangLibraryMetadataResolver(MavenSidecarClient sidecar)
    {
        ArgumentNullException.ThrowIfNull(sidecar);
        _sidecar = sidecar;
    }

    /// <summary>
    /// Returns a descriptor with the published SHA-1 and size, or null when the
    /// library is already complete or has no remote URL.
    /// </summary>
    public async Task<MojangDownload?> ResolveAsync(
        MojangLibrary library,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);

        var artifact = library.Downloads?.Artifact;
        if (artifact is null || string.IsNullOrWhiteSpace(artifact.Url)) return null;
        if (!string.IsNullOrWhiteSpace(artifact.Sha1) && artifact.Size is not null)
        {
            return null;
        }

        var issue = DownloadPlanPaths.TryValidateUrl(artifact.Url, out var uri);
        if (issue is not null || uri is null)
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InvalidMetadata,
                $"library '{library.Name}': {issue?.Message ?? "invalid artifact URL"}");
        }

        var sha1 = artifact.Sha1;
        var size = artifact.Size;
        if (string.IsNullOrWhiteSpace(sha1))
        {
            sha1 = await _sidecar
                .GetSha1Async(uri, cancellationToken)
                .ConfigureAwait(false);
        }

        if (size is null)
        {
            size = await _sidecar
                .GetSizeAsync(uri, cancellationToken)
                .ConfigureAwait(false);
        }

        return artifact with { Sha1 = sha1, Size = size };
    }
}
