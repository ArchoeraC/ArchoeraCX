using Archoera.Domain.Resources;

namespace Archoera.Application.Resources;

/// <summary>
/// Resolves Modrinth-style required dependencies recursively for one selected
/// version. Optional and incompatible edges are reported, never auto-installed.
/// </summary>
public static class ResourceDependencyResolver
{
    public const int MaxDepth = 8;

    public sealed record Result(
        IReadOnlyList<ResourceVersion> Required,
        IReadOnlyList<ResourceDependency> Optional,
        IReadOnlyList<ResourceDependency> Incompatible,
        IReadOnlyList<string> Warnings);

    public static async Task<Result> ResolveAsync(
        IResourceProvider provider,
        ResourceKind kind,
        ResourceTarget target,
        ResourceVersion root,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(root);

        var required = new List<ResourceVersion>();
        var optional = new List<ResourceDependency>();
        var incompatible = new List<ResourceDependency>();
        var warnings = new List<string>();
        var visitedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            root.ProjectId,
        };
        var queue = new Queue<(ResourceVersion Version, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (version, depth) = queue.Dequeue();
            if (depth >= MaxDepth)
            {
                warnings.Add($"dependency depth limit reached at {version.ProjectId}");
                continue;
            }

            foreach (var dependency in version.DependencyList)
            {
                switch (dependency.Kind)
                {
                    case ResourceDependencyKind.Optional:
                        optional.Add(dependency);
                        continue;
                    case ResourceDependencyKind.Incompatible:
                        incompatible.Add(dependency);
                        continue;
                    case ResourceDependencyKind.Embedded:
                        continue;
                    case ResourceDependencyKind.Required:
                        break;
                    default:
                        continue;
                }

                if (string.IsNullOrWhiteSpace(dependency.ProjectId))
                {
                    warnings.Add(
                        $"required dependency of {version.ProjectId} has no project id");
                    continue;
                }

                if (!visitedProjects.Add(dependency.ProjectId))
                {
                    continue;
                }

                ResourceVersion resolved;
                if (!string.IsNullOrWhiteSpace(dependency.VersionId))
                {
                    resolved = await provider
                        .GetVersionAsync(dependency.VersionId, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    var versions = await provider
                        .GetVersionsAsync(
                            dependency.ProjectId,
                            kind,
                            target,
                            cancellationToken)
                        .ConfigureAwait(false);
                    resolved = versions.FirstOrDefault(
                                   candidate => candidate.VersionType == ResourceVersionType.Release)
                               ?? versions.FirstOrDefault()
                               ?? throw new ResourceProviderException(
                                   ResourceErrorCode.NoCompatibleBuild,
                                   $"required dependency {dependency.ProjectId} has no compatible version");
                }

                required.Add(resolved);
                queue.Enqueue((resolved, depth + 1));
            }
        }

        return new Result(required, optional, incompatible, warnings);
    }
}
