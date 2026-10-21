using Archoera.Application.Resources;
using Archoera.Domain.Resources;

namespace Archoera.Tests;

public sealed class ResourceDependencyResolverTests
{
    [Fact]
    public async Task Resolve_walks_required_transitives_and_reports_optional()
    {
        var versions = new Dictionary<string, ResourceVersion>
        {
            ["root"] = Version(
                "root",
                "root-project",
                Required("dep-a", "a1"),
                Optional("optional-project")),
            ["a1"] = Version(
                "a1",
                "dep-a",
                Required("dep-b", "b1")),
            ["b1"] = Version("b1", "dep-b"),
        };
        var provider = new FakeProvider(versions);

        var result = await ResourceDependencyResolver.ResolveAsync(
            provider,
            ResourceKind.Mod,
            Target(),
            versions["root"]);

        Assert.Equal(["a1", "b1"], result.Required.Select(version => version.Id));
        Assert.Single(result.Optional);
        Assert.Empty(result.Incompatible);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Resolve_stops_cycles_and_incompatible_edges()
    {
        var versions = new Dictionary<string, ResourceVersion>
        {
            ["root"] = Version(
                "root",
                "root-project",
                Required("dep-a", "a1"),
                new ResourceDependency(ResourceDependencyKind.Incompatible, "bad")),
            ["a1"] = Version(
                "a1",
                "dep-a",
                Required("root-project", "root")),
        };
        var provider = new FakeProvider(versions);

        var result = await ResourceDependencyResolver.ResolveAsync(
            provider,
            ResourceKind.Mod,
            Target(),
            versions["root"]);

        Assert.Single(result.Required);
        Assert.Single(result.Incompatible);
        Assert.Empty(result.Warnings);
    }

    private static ResourceVersion Version(
        string id,
        string project,
        params ResourceDependency[] dependencies)
        => new(
            id,
            project,
            id,
            id,
            ["1.21.1"],
            ["fabric"],
            Files: [new ResourceFile("file.jar", "https://example.invalid/file.jar", 1, Sha1: new string('a', 40))],
            Dependencies: dependencies);

    private static ResourceDependency Required(string project, string? version = null)
        => new(ResourceDependencyKind.Required, project, version);

    private static ResourceDependency Optional(string project)
        => new(ResourceDependencyKind.Optional, project);

    private static ResourceTarget Target()
        => new("1.21.1", "fabric");

    private sealed class FakeProvider(IReadOnlyDictionary<string, ResourceVersion> versions)
        : IResourceProvider
    {
        public string Id => "fake";

        public Task<ResourceSearchResult> SearchAsync(
            ResourceSearchRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResourceProject> GetProjectAsync(
            string projectIdOrSlug,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ResourceVersion>> GetVersionsAsync(
            string projectIdOrSlug,
            ResourceKind kind,
            ResourceTarget target,
            CancellationToken cancellationToken = default)
        {
            var found = versions.Values
                .Where(version => string.Equals(version.ProjectId, projectIdOrSlug, StringComparison.Ordinal))
                .ToArray();
            return Task.FromResult<IReadOnlyList<ResourceVersion>>(found);
        }

        public Task<ResourceVersion> GetVersionAsync(
            string versionId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(versions[versionId]);
    }
}
