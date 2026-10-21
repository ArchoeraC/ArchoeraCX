namespace Archoera.Domain.Resources;

/// <summary>
/// Provider-independent project metadata. <see cref="Loaders"/> and
/// <see cref="GameVersions"/> are historical unions reported by the provider
/// and must not be used to pick an install candidate.
/// </summary>
public sealed record ResourceProject(
    string Id,
    string Slug,
    string Name,
    string Summary,
    ResourceKind Kind,
    long Downloads = 0,
    long Follows = 0,
    DateTimeOffset? UpdatedAt = null,
    string? Author = null,
    string? IconUrl = null,
    IReadOnlyList<string>? Categories = null,
    IReadOnlyList<string>? Loaders = null,
    IReadOnlyList<string>? GameVersions = null,
    string? ClientSide = null,
    string? License = null)
{
    /// <summary>Non-null category list.</summary>
    public IReadOnlyList<string> CategoryList => Categories ?? [];

    /// <summary>Non-null loader list (union, not install candidates).</summary>
    public IReadOnlyList<string> LoaderList => Loaders ?? [];

    /// <summary>Non-null game-version list (union, not install candidates).</summary>
    public IReadOnlyList<string> GameVersionList => GameVersions ?? [];
}
