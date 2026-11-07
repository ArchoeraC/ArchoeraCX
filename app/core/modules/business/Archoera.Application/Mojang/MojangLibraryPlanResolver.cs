using Archoera.Domain.Mojang;

namespace Archoera.Application.Mojang;

/// <summary>
/// Resolves a library artifact that has a remote URL but no hash/size yet.
/// Implementations fetch the Maven sidecar; callers inject them so the plan
/// builder itself stays pure and offline.
/// </summary>
public delegate Task<MojangDownload?> LibraryArtifactResolver(
    MojangLibrary library,
    CancellationToken cancellationToken);

/// <summary>
/// Fills in the artifact descriptor for libraries whose metadata only names a
/// Maven coordinate. A <see cref="LibraryArtifactResolver"/> may return null to
/// leave the entry unchanged; the plan builder then reports the usual
/// <c>MISSING_HASH</c>/<c>MISSING_SIZE</c> instead of emitting an unverifiable
/// download.
/// </summary>
public static class MojangLibraryPlanResolver
{
    /// <summary>Returns a version whose remote artifacts are fully described.</summary>
    public static async Task<MojangVersion> ApplyAsync(
        MojangVersion version,
        LibraryArtifactResolver? resolver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (resolver is null || version.Libraries.Count == 0) return version;

        List<MojangLibrary>? libraries = null;
        for (var index = 0; index < version.Libraries.Count; index++)
        {
            var library = version.Libraries[index];
            var artifact = library.Downloads?.Artifact;
            if (artifact is null || string.IsNullOrWhiteSpace(artifact.Url))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(artifact.Sha1) && artifact.Size is not null)
            {
                continue;
            }

            var resolved = await resolver(library, cancellationToken).ConfigureAwait(false);
            if (resolved is null) continue;

            libraries ??= [.. version.Libraries];
            libraries[index] = library with
            {
                Downloads = new MojangLibraryDownloads(
                    resolved,
                    library.Downloads?.Classifiers),
            };
        }

        return libraries is null ? version : version with { Libraries = libraries };
    }
}
