using Archoera.Application.Resources;
using Archoera.Domain.Resources;

namespace Archoera.Tests;

public sealed class ResourceModpackTests
{
    [Fact]
    public void Modpack_kind_round_trips_on_the_wire()
    {
        Assert.True(ResourceWire.TryParseKind("modpack", out var kind));
        Assert.Equal(ResourceKind.Modpack, kind);
        Assert.Equal("modpack", kind.ToWire());
    }

    [Theory]
    [InlineData("fabric")]
    [InlineData(null)]
    public void Modpack_compatibility_uses_game_version_and_optional_loader(
        string? loader)
    {
        var version = new ResourceVersion(
            Id: "v1",
            ProjectId: "p1",
            Name: "Pack",
            VersionNumber: "1.0",
            GameVersions: ["26.2"],
            Loaders: ["fabric"],
            VersionType: ResourceVersionType.Release,
            PublishedAt: null,
            Downloads: 0,
            Files: [],
            Dependencies: []);
        var target = new ResourceTarget("26.2", loader);

        Assert.True(ResourceCompatibility.IsCompatible(
            version,
            ResourceKind.Modpack,
            target));
        Assert.False(ResourceCompatibility.IsCompatible(
            version,
            ResourceKind.Modpack,
            new ResourceTarget("26.1", loader)));
    }

    [Fact]
    public void Modpack_target_requires_a_game_version()
    {
        Assert.False(new ResourceTarget(string.Empty).IsCompleteFor(ResourceKind.Modpack));
        Assert.True(new ResourceTarget("26.2").IsCompleteFor(ResourceKind.Modpack));
    }
}
