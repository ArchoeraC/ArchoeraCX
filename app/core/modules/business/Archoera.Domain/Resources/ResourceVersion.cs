namespace Archoera.Domain.Resources;

/// <summary>
/// One concrete provider version. Only the version-level
/// <see cref="GameVersions"/> and <see cref="Loaders"/> fields are used for
/// compatibility decisions.
/// </summary>
public sealed record ResourceVersion(
    string Id,
    string ProjectId,
    string Name,
    string VersionNumber,
    IReadOnlyList<string> GameVersions,
    IReadOnlyList<string> Loaders,
    ResourceVersionType VersionType = ResourceVersionType.Release,
    DateTimeOffset? PublishedAt = null,
    long Downloads = 0,
    IReadOnlyList<ResourceFile>? Files = null,
    IReadOnlyList<ResourceDependency>? Dependencies = null)
{
    /// <summary>Non-null file list.</summary>
    public IReadOnlyList<ResourceFile> FileList => Files ?? [];

    /// <summary>Non-null dependency list.</summary>
    public IReadOnlyList<ResourceDependency> DependencyList => Dependencies ?? [];

    /// <summary>Provider-primary file, falling back to the first listed file.</summary>
    public ResourceFile? PrimaryFile
        => FileList.FirstOrDefault(file => file.Primary) ?? FileList.FirstOrDefault();

    /// <summary>
    /// Dependencies that must be installed for this version to work. Callers
    /// resolve them recursively (not implemented in this slice).
    /// </summary>
    public IReadOnlyList<ResourceDependency> RequiredDependencies
        => DependencyList
            .Where(dependency => dependency.Kind == ResourceDependencyKind.Required)
            .ToArray();

    /// <summary>Optional suggestions; never auto-installed.</summary>
    public IReadOnlyList<ResourceDependency> OptionalDependencies
        => DependencyList
            .Where(dependency => dependency.Kind == ResourceDependencyKind.Optional)
            .ToArray();
}
