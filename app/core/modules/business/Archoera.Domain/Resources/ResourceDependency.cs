namespace Archoera.Domain.Resources;

/// <summary>
/// Dependency edge of a resource version.
/// </summary>
/// <param name="Kind">Required/optional/incompatible/embedded.</param>
/// <param name="ProjectId">Provider project id, when reported.</param>
/// <param name="VersionId">Exact provider version id, when reported.</param>
/// <param name="FileName">Provider file name, when reported.</param>
public sealed record ResourceDependency(
    ResourceDependencyKind Kind,
    string? ProjectId = null,
    string? VersionId = null,
    string? FileName = null)
{
    /// <summary>
    /// True when installing the parent without this dependency is invalid.
    /// </summary>
    public bool BlocksInstall => Kind is ResourceDependencyKind.Required
        or ResourceDependencyKind.Incompatible;
}
