namespace Archoera.Domain.Resources;

/// <summary>
/// Read-only provider surface used by search/browse and by the download-plan
/// producer. Implementations own HTTP, rate limits and DTO mapping; they never
/// touch the UI and never execute downloads.
/// </summary>
public interface IResourceProvider
{
    /// <summary>Stable provider id, for example <c>modrinth</c>.</summary>
    string Id { get; }

    /// <summary>Searches projects for a kind and optional target.</summary>
    Task<ResourceSearchResult> SearchAsync(
        ResourceSearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Loads project details by id or slug.</summary>
    Task<ResourceProject> GetProjectAsync(
        string projectIdOrSlug,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads all versions for a project and returns only the versions whose
    /// version-level fields satisfy <paramref name="target"/>.
    /// </summary>
    /// <param name="projectIdOrSlug">Provider project id or slug.</param>
    /// <param name="kind">Kind of the project, needed to interpret loaders.</param>
    /// <param name="target">Exact game version plus loader/shader loader.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<ResourceVersion>> GetVersionsAsync(
        string projectIdOrSlug,
        ResourceKind kind,
        ResourceTarget target,
        CancellationToken cancellationToken = default);

    /// <summary>Loads one version, including files and dependencies.</summary>
    Task<ResourceVersion> GetVersionAsync(
        string versionId,
        CancellationToken cancellationToken = default);
}
