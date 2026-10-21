namespace Archoera.Domain.Resources;

/// <summary>
/// Provider-independent search request. Pagination is zero-based (Modrinth
/// <c>offset</c>); providers cap the page size themselves.
/// </summary>
public sealed record ResourceSearchRequest
{
    /// <summary>Free-text query; may be empty when browsing by facets.</summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>Content kind to search.</summary>
    public ResourceKind Kind { get; init; } = ResourceKind.Mod;

    /// <summary>Optional instance context for game-version/loader facets.</summary>
    public ResourceTarget? Target { get; init; }

    /// <summary>Additional loaders to require (lower-case provider names).</summary>
    public IReadOnlyList<string> Loaders { get; init; } = [];

    /// <summary>Additional exact game versions to require.</summary>
    public IReadOnlyList<string> GameVersions { get; init; } = [];

    /// <summary>Categories to require.</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>Result ordering.</summary>
    public ResourceSort Sort { get; init; } = ResourceSort.Relevance;

    /// <summary>Zero-based page index.</summary>
    public int Page { get; init; }

    /// <summary>Page size, 1..100 for Modrinth.</summary>
    public int PageSize { get; init; } = 20;

    /// <summary>When false (default), snapshots/pre-releases are excluded.</summary>
    public bool IncludeSnapshots { get; init; }
}

/// <summary>Normalized search response.</summary>
public sealed record ResourceSearchResult(
    string Provider,
    long Total,
    int Page,
    int PageSize,
    IReadOnlyList<ResourceProject> Hits);
