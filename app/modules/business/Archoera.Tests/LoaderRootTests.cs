using Archoera.Infrastructure.Launch;
using Archoera.Infrastructure.Packs;

namespace Archoera.Tests;

/// <summary>
/// Loader files live either in the shared game root (one copy for every
/// instance of a loader build) or inside the instance. The resolver has to
/// pick the same root for planning, committing and repairing.
/// </summary>
public sealed class LoaderRootTests
{
    [Fact]
    public void Shared_mode_uses_the_game_root_that_owns_versions()
    {
        var directory = TempDirectory();
        try
        {
            var gameRoot = Path.Combine(directory, "minecraft");
            var instanceRoot = Path.Combine(gameRoot, "versions", "AutumnTown");
            Directory.CreateDirectory(instanceRoot);

            Assert.Equal(
                Path.GetFullPath(gameRoot),
                LaunchPaths.ResolveLoaderRoot(instanceRoot, shareLoaderFiles: true));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Isolated_mode_keeps_the_files_inside_the_instance()
    {
        var directory = TempDirectory();
        try
        {
            var gameRoot = Path.Combine(directory, "minecraft");
            var instanceRoot = Path.Combine(gameRoot, "versions", "AutumnTown");
            Directory.CreateDirectory(instanceRoot);

            Assert.Equal(
                Path.GetFullPath(instanceRoot),
                LaunchPaths.ResolveLoaderRoot(instanceRoot, shareLoaderFiles: false));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Auto_mode_follows_an_existing_isolated_layout()
    {
        var directory = TempDirectory();
        try
        {
            var gameRoot = Path.Combine(directory, "minecraft");
            var instanceRoot = Path.Combine(gameRoot, "versions", "AutumnTown");
            Directory.CreateDirectory(Path.Combine(instanceRoot, "libraries"));

            Assert.Equal(
                Path.GetFullPath(instanceRoot),
                LaunchPaths.ResolveLoaderRoot(instanceRoot, shareLoaderFiles: null));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Auto_mode_falls_back_to_the_shared_root()
    {
        var directory = TempDirectory();
        try
        {
            var gameRoot = Path.Combine(directory, "minecraft");
            var instanceRoot = Path.Combine(gameRoot, "versions", "AutumnTown");
            Directory.CreateDirectory(instanceRoot);

            Assert.Equal(
                Path.GetFullPath(gameRoot),
                LaunchPaths.ResolveLoaderRoot(instanceRoot, shareLoaderFiles: null));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void A_custom_instance_directory_is_its_own_root()
    {
        var directory = TempDirectory();
        try
        {
            var instanceRoot = Path.Combine(directory, "custom-instance");
            Directory.CreateDirectory(instanceRoot);

            Assert.Equal(
                Path.GetFullPath(instanceRoot),
                LaunchPaths.ResolveLoaderRoot(instanceRoot, shareLoaderFiles: true));
            Assert.Equal(
                Path.GetFullPath(instanceRoot),
                LaunchPaths.ResolveLoaderRoot(instanceRoot, shareLoaderFiles: false));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Commit_writes_the_profile_into_the_instance_folder()
    {
        var directory = TempDirectory();
        try
        {
            var gameRoot = Path.Combine(directory, "minecraft");
            var instanceRoot = Path.Combine(gameRoot, "versions", "AutumnTown");
            Directory.CreateDirectory(Path.Combine(instanceRoot, ".archoera"));
            File.WriteAllText(
                Path.Combine(instanceRoot, ".archoera", "instance.json"),
                """
                {"id":"autumn","name":"AutumnTown","gameVersion":"1.21.1",
                 "loader":"neoforge","loaderVersion":"21.1.176"}
                """);

            var profile = PackGameInstallService.CommitInstanceProfile(
                instanceRoot,
                """
                {"id":"neoforge-21.1.176","mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher",
                 "libraries":[{"name":"net.neoforged:neoforge:21.1.176"}]}
                """);

            Assert.Equal(
                Path.GetFullPath(Path.Combine(instanceRoot, "AutumnTown.json")),
                profile);
            Assert.True(File.Exists(profile));

            // The shared install root owns libraries, but must not gain a
            // loader-named version folder that discovery could mistake for an
            // extra instance.
            Assert.False(Directory.Exists(Path.Combine(
                gameRoot,
                "versions",
                "neoforge-21.1.176")));

            var discovered = Assert.Single(
                Archoera.Infrastructure.Instances.InstanceDiscovery.Discover(gameRoot));
            Assert.Equal("AutumnTown", discovered.Name);
            Assert.Equal("neoforge", discovered.Loader);
        }
        finally
        {
            Delete(directory);
        }
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-loader-root",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Delete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
