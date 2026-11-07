using Archoera.Domain.Resources;

namespace Archoera.Application.Resources;

/// <summary>
/// Resolves Modrinth-style required dependencies recursively for one selected
/// version. Dependencies are resolved with their own project kind, which is
/// essential for resource packs that require mods (for example EMF/ETF).
/// Optional and incompatible edges are reported, never auto-installed.
/// </summary>
public static class ResourceDependencyResolver
{
    public const int MaxDepth = 8;

    public sealed record ResolvedDependency(
        ResourceVersion Version,
        ResourceKind Kind,
        ResourceTarget Target);

    public sealed record Result(
        IReadOnlyList<ResolvedDependency> Required,
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

        var required = new List<ResolvedDependency>();
        var optional = new List<ResourceDependency>();
        var incompatible = new List<ResourceDependency>();
        var warnings = new List<string>();
        var visitedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            root.ProjectId,
        };
        var projectKinds = new Dictionary<string, ResourceKind>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(ResourceVersion Version, ResourceKind Kind, ResourceTarget Target, int Depth)>();
        queue.Enqueue((root, kind, target, 0));

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = queue.Dequeue();
            if (current.Depth >= MaxDepth)
            {
                warnings.Add($"dependency depth limit reached at {current.Version.ProjectId}");
                continue;
            }

            foreach (var dependency in current.Version.DependencyList)
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
                        $"required dependency of {current.Version.ProjectId} has no project id");
                    continue;
                }

                if (!visitedProjects.Add(dependency.ProjectId))
                {
                    continue;
                }

                ResourceKind dependencyKind;
                if (projectKinds.TryGetValue(dependency.ProjectId, out var cachedKind))
                {
                    dependencyKind = cachedKind;
                }
                else
                {
                    try
                    {
                        var project = await provider
                            .GetProjectAsync(dependency.ProjectId, cancellationToken)
                            .ConfigureAwait(false);
                        dependencyKind = project.Kind;
                        projectKinds[dependency.ProjectId] = dependencyKind;
                    }
                    catch (ResourceProviderException exception)
                    {
                        warnings.Add(
                            $"required dependency {dependency.ProjectId} could not be resolved: {exception.Message}");
                        continue;
                    }
                }

                var dependencyTarget = TargetFor(
                    dependencyKind,
                    current.Target);
                if (dependencyTarget is null)
                {
                    warnings.Add(
                        $"required dependency {dependency.ProjectId} needs a loader target that is not available");
                    continue;
                }

                ResourceVersion? resolved = null;
                if (!string.IsNullOrWhiteSpace(dependency.VersionId))
                {
                    var pinned = await provider
                        .GetVersionAsync(dependency.VersionId, cancellationToken)
                        .ConfigureAwait(false);
                    if (ResourceCompatibility.IsCompatible(
                            pinned,
                            dependencyKind,
                            dependencyTarget))
                    {
                        resolved = pinned;
                    }
                    else
                    {
                        warnings.Add(
                            $"pinned dependency {dependency.ProjectId}@{dependency.VersionId} is not compatible; selecting a compatible version");
                    }
                }

                if (resolved is null)
                {
                    var versions = await provider
                        .GetVersionsAsync(
                            dependency.ProjectId,
                            dependencyKind,
                            dependencyTarget,
                            cancellationToken)
                        .ConfigureAwait(false);
                    resolved = versions.FirstOrDefault(
                                   candidate => candidate.VersionType == ResourceVersionType.Release)
                               ?? versions.FirstOrDefault();
                }

                if (resolved is null)
                {
                    warnings.Add(
                        $"required dependency {dependency.ProjectId} has no compatible version for {dependencyKind.ToWire()}/{dependencyTarget.GameVersion}");
                    continue;
                }

                required.Add(new ResolvedDependency(
                    resolved,
                    dependencyKind,
                    dependencyTarget));
                queue.Enqueue((resolved, dependencyKind, dependencyTarget, current.Depth + 1));
            }
        }

        return new Result(required, optional, incompatible, warnings);
    }

    private static ResourceTarget? TargetFor(
        ResourceKind kind,
        ResourceTarget parent)
    {
        return kind switch
        {
            ResourceKind.Mod => string.IsNullOrWhiteSpace(parent.NormalizedLoader)
                ? null
                : ResourceTarget.ForMod(parent.GameVersion, parent.NormalizedLoader),
            ResourceKind.Shader => ResourceTarget.ForShader(
                parent.GameVersion,
                parent.NormalizedShaderLoader ?? "iris"),
            ResourceKind.ResourcePack => ResourceTarget.ForResourcePack(parent.GameVersion),
            _ => null,
        };
    }
}
