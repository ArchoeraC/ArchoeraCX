using Archoera.Application.Loaders;
using Archoera.Domain.Loaders;

namespace Archoera.Tests;

/// <summary>
/// Imported instances store the full loader profile id, while Maven paths and
/// manifest coordinates need the bare build. Repair has to normalize both.
/// </summary>
public sealed class LoaderVersionTextTests
{
    [Theory]
    [InlineData("forge", "1.20.1", "47.4.18", "47.4.18")]
    [InlineData("forge", "1.20.1", "1.20.1-47.4.18", "47.4.18")]
    [InlineData("forge", "1.20.1", "1.20.1-forge-47.4.18", "47.4.18")]
    [InlineData("forge", "1.20.1", "forge-47.4.18", "47.4.18")]
    [InlineData("forge", "1.20.1", "forge-1.20.1-47.4.18", "47.4.18")]
    [InlineData("neoforge", "1.21.1", "21.1.248", "21.1.248")]
    [InlineData("neoforge", "1.21.1", "1.21.1-neoforge-21.1.248", "21.1.248")]
    [InlineData("fabric", "1.21.8", "0.18.4", "0.18.4")]
    [InlineData("fabric", "1.21.8", "fabric-loader-0.18.4", "0.18.4")]
    [InlineData("fabric", "1.21.8", "fabric-loader-0.18.4-1.21.8", "0.18.4")]
    [InlineData("quilt", "1.21.1", "quilt-loader-0.27.1-1.21.1", "0.27.1")]
    public void Normalize_strips_profile_and_game_prefixes(
        string loader,
        string game,
        string value,
        string expected)
    {
        Assert.Equal(expected, LoaderVersionText.Normalize(loader, game, value));
    }

    [Fact]
    public void Forge_installer_url_accepts_the_profile_id_stored_by_imports()
    {
        var path = ForgeMavenUrls.InstallerPath(
            LoaderKind.Forge,
            "1.20.1",
            "1.20.1-forge-47.4.18");

        Assert.Equal(
            "net/minecraftforge/forge/1.20.1-47.4.18/" +
            "forge-1.20.1-47.4.18-installer.jar",
            path);
    }
}
