using System.Text.Json;
using Archoera.Application.Mojang;
using Archoera.Domain.Instances;
using Archoera.Domain.Mojang;
using Archoera.Infrastructure.Launch;

namespace Archoera.Tests;

/// <summary>
/// Profile identity is what keeps another instance's loader build out of this
/// instance during launch repair and the dependency check.
/// </summary>
public sealed class LoaderProfileIdentityTests
{
    [Theory]
    [InlineData("21.1.264", "21.1.2", false)]
    [InlineData("0.16.14", "0.16.1", false)]
    [InlineData("58.1.26", "58.1.2", false)]
    [InlineData("47.2.0", "47.2.0", true)]
    [InlineData("1.20.1-47.2.0", "47.2.0", true)]
    public void Compares_loader_builds_as_whole_tokens(
        string left,
        string right,
        bool expected)
        => Assert.Equal(expected, LoaderProfileIdentity.SameBuild(left, right));

    [Theory]
    [InlineData("1.21.1-forge-58.1.26", "forge", "58.1.26")]
    [InlineData("neoforge-21.1.264", "neoforge", "21.1.264")]
    [InlineData("fabric-loader-0.16.14-1.21.1", "fabric", "0.16.14")]
    [InlineData("quilt-loader-0.27.1-1.21.1", "quilt", "0.27.1")]
    [InlineData("1.21.1-neoforge", "neoforge", null)]
    public void Reads_the_loader_build_from_a_profile_id(
        string id,
        string loader,
        string? expected)
        => Assert.Equal(expected, LoaderProfileIdentity.LoaderVersionInId(id, loader));

    [Fact]
    public void A_game_version_prefix_is_not_the_game_version()
    {
        Assert.False(LoaderProfileIdentity.HasToken("1.21.1-forge-58.1.26", "1.21"));
        Assert.True(LoaderProfileIdentity.HasToken("1.21.1-forge-58.1.26", "1.21.1"));
    }

    [Fact]
    public void Matches_a_neoforge_profile_whose_id_omits_the_game_version()
    {
        var instance = Instance("neoforge", "1.21.1", "21.1.264");
        var version = Version(
            """
            {"id":"neoforge-21.1.264","mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher",
             "libraries":[{"name":"net.neoforged:neoforge:21.1.264:universal"}]}
            """);

        Assert.True(LoaderProfileIdentity.Matches(instance, version));
    }

    [Fact]
    public void Rejects_a_neoforge_profile_for_another_build()
    {
        var instance = Instance("neoforge", "1.21.1", "21.1.2");
        var version = Version(
            """
            {"id":"neoforge-21.1.264","mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher",
             "libraries":[{"name":"net.neoforged:neoforge:21.1.264:universal"}]}
            """);

        Assert.False(LoaderProfileIdentity.Matches(instance, version));
    }

    [Fact]
    public void Rejects_an_unidentifiable_neoforge_profile_without_a_loader_version()
    {
        var instance = Instance("neoforge", "1.21.1", string.Empty);
        var version = Version(
            """
            {"id":"neoforge-21.1.264","mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher",
             "libraries":[]}
            """);

        Assert.False(LoaderProfileIdentity.Matches(instance, version));
    }

    [Fact]
    public void Accepts_a_legacy_neoforge_profile_under_forge_coordinates()
    {
        var instance = Instance("neoforge", "1.20.1", "47.1.106");
        var version = Version(
            """
            {"id":"1.20.1-forge-47.1.106","inheritsFrom":"1.20.1",
             "mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher",
             "libraries":[{"name":"net.neoforged:forge:1.20.1-47.1.106:universal"}]}
            """);

        Assert.True(LoaderProfileIdentity.Matches(instance, version));
    }

    [Fact]
    public void Rejects_a_forge_profile_for_a_neoforge_instance()
    {
        var instance = Instance("neoforge", "1.20.1", "47.1.106");
        var version = Version(
            """
            {"id":"1.20.1-forge-47.1.106","inheritsFrom":"1.20.1",
             "mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher",
             "libraries":[{"name":"net.minecraftforge:forge:1.20.1-47.1.106:universal"}]}
            """);

        Assert.False(LoaderProfileIdentity.Matches(instance, version));
    }

    [Fact]
    public void Rejects_a_legacy_neoforge_profile_for_a_forge_instance()
    {
        var instance = Instance("forge", "1.20.1", "47.1.106");
        var version = Version(
            """
            {"id":"1.20.1-forge-47.1.106","inheritsFrom":"1.20.1",
             "mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher",
             "libraries":[{"name":"net.neoforged:forge:1.20.1-47.1.106:universal"}]}
            """);

        Assert.False(LoaderProfileIdentity.Matches(instance, version));
    }

    [Fact]
    public void Accepts_a_quilt_profile_that_also_lists_fabric_libraries()
    {
        var instance = Instance("quilt", "1.21.1", "0.27.1");
        var version = Version(
            """
            {"id":"quilt-loader-0.27.1-1.21.1","inheritsFrom":"1.21.1",
             "mainClass":"org.quiltmc.loader.impl.launch.knot.KnotClient",
             "libraries":[{"name":"org.quiltmc:quilt-loader:0.27.1"},
                          {"name":"net.fabricmc:intermediary:1.21.1"}]}
            """);

        Assert.True(LoaderProfileIdentity.Matches(instance, version));
    }

    private static GameInstanceRecord Instance(
        string loader,
        string gameVersion,
        string loaderVersion)
        => new(
            "id",
            "pack",
            gameVersion,
            loader,
            loaderVersion,
            Path.Combine(Path.GetTempPath(), "archoera-identity", Guid.NewGuid().ToString("N")),
            0,
            false,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null);

    private static MojangVersion Version(string json)
    {
        using var document = JsonDocument.Parse(json);
        return MojangJson.ParseVersion(document.RootElement);
    }
}
