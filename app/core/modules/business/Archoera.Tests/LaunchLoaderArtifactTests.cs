using Archoera.Domain.Instances;
using Archoera.Domain.Mojang;
using Archoera.Infrastructure.Launch;

namespace Archoera.Tests;

/// <summary>
/// NeoForge locates its generated client and universal jars through the
/// library directory convention instead of listing them as libraries. An
/// instance prepared by another launcher must pass the preflight without an
/// installer, while a genuinely missing generated jar still requests repair.
/// </summary>
public sealed class LaunchLoaderArtifactTests
{
    [Fact]
    public void NeoForge_merged_profile_needs_the_generated_jars()
    {
        var root = CreateRoot();
        try
        {
            var instance = Instance("neoforge", "21.1.248");
            var paths = LaunchPaths.Resolve(root);
            var version = Version();

            var missing = LaunchPlanBuilder.CheckLoaderArtifacts(instance, version, paths);
            Assert.False(missing.Ok);
            Assert.Equal("LOADER_FILES_MISSING", missing.Code);

            WriteArtifact(paths.LibrariesRoot, "net/neoforged/neoforge/21.1.248", "neoforge-21.1.248-universal.jar");
            var clientMissing = LaunchPlanBuilder.CheckLoaderArtifacts(instance, version, paths);
            Assert.False(clientMissing.Ok);
            Assert.Equal("LOADER_FILES_MISSING", clientMissing.Code);

            WriteArtifact(paths.LibrariesRoot, "net/neoforged/neoforge/21.1.248", "neoforge-21.1.248-client.jar");
            Assert.True(LaunchPlanBuilder.CheckLoaderArtifacts(instance, version, paths).Ok);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Legacy_NeoForge_uses_the_forge_artifact_coordinates()
    {
        var root = CreateRoot();
        try
        {
            var instance = Instance("neoforge", "47.1.106", gameVersion: "1.20.1");
            var paths = LaunchPaths.Resolve(root);
            WriteArtifact(
                paths.LibrariesRoot,
                "net/neoforged/forge/1.20.1-47.1.106",
                "forge-1.20.1-47.1.106-universal.jar");
            WriteArtifact(
                paths.LibrariesRoot,
                "net/neoforged/forge/1.20.1-47.1.106",
                "forge-1.20.1-47.1.106-client.jar");

            Assert.True(LaunchPlanBuilder.CheckLoaderArtifacts(instance, Version(), paths).Ok);

            // Some launchers store the game-prefixed version; do not duplicate it.
            var prefixed = Instance("neoforge", "1.20.1-47.1.106", gameVersion: "1.20.1");
            Assert.True(LaunchPlanBuilder.CheckLoaderArtifacts(prefixed, Version(), paths).Ok);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Profiles_listing_the_generated_jars_skip_the_convention_check()
    {
        var root = CreateRoot();
        try
        {
            var instance = Instance("neoforge", "21.1.248");
            var paths = LaunchPaths.Resolve(root);
            var version = Version() with
            {
                Libraries =
                [
                    new MojangLibrary("net.neoforged:neoforge:21.1.248:universal"),
                ],
            };

            Assert.True(LaunchPlanBuilder.CheckLoaderArtifacts(instance, version, paths).Ok);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Forge_is_covered_by_the_library_check()
    {
        var root = CreateRoot();
        try
        {
            var instance = Instance("forge", "52.1.20");
            var paths = LaunchPaths.Resolve(root);

            Assert.True(LaunchPlanBuilder.CheckLoaderArtifacts(instance, Version(), paths).Ok);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Profile_loader_version_wins_over_the_instance_record()
    {
        var root = CreateRoot();
        try
        {
            var instance = Instance("neoforge", "21.1.248");
            var paths = LaunchPaths.Resolve(root);
            var version = Version() with
            {
                Arguments = new MojangArguments(
                    [
                        new MojangArgument(
                            [],
                            [
                                "--fml.neoForgeVersion",
                                "21.1.235",
                                "--fml.mcVersion",
                                "1.21.1",
                            ]),
                    ],
                    []),
            };

            WriteArtifact(
                paths.LibrariesRoot,
                "net/neoforged/neoforge/21.1.235",
                "neoforge-21.1.235-universal.jar");
            WriteArtifact(
                paths.LibrariesRoot,
                "net/neoforged/neoforge/21.1.235",
                "neoforge-21.1.235-client.jar");

            Assert.True(LaunchPlanBuilder.CheckLoaderArtifacts(instance, version, paths).Ok);
        }
        finally
        {
            Delete(root);
        }
    }

    private static MojangVersion Version() => new()
    {
        Id = "test",
        MainClass = "cpw.mods.bootstraplauncher.BootstrapLauncher",
    };

    private static GameInstanceRecord Instance(
        string loader,
        string loaderVersion,
        string gameVersion = "1.21.1")
        => new(
            "id",
            "instance",
            gameVersion,
            loader,
            loaderVersion,
            Path.Combine(Path.GetTempPath(), "instance"),
            0,
            false,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null);

    private static string CreateRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "archoera-loader-artifacts",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "libraries"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        return root;
    }

    private static void WriteArtifact(string librariesRoot, string relative, string name)
    {
        var directory = Path.Combine(
            librariesRoot,
            relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), "jar");
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
