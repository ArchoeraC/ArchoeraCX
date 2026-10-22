using System.Text.Json;
using Archoera.Domain.Instances;
using Archoera.Infrastructure.Launch;

namespace Archoera.Tests;

public sealed class LaunchVersionResolutionTests
{
    [Fact]
    public void Resolves_vanilla_json_from_a_version_isolated_shared_root()
    {
        var gameRoot = CreateTempDirectory();
        try
        {
            var instanceRoot = Path.Combine(gameRoot, "versions", "island");
            Directory.CreateDirectory(instanceRoot);
            var versionDirectory = Path.Combine(gameRoot, "versions", "1.21.1");
            Directory.CreateDirectory(versionDirectory);
            File.WriteAllText(
                Path.Combine(versionDirectory, "1.21.1.json"),
                MinimalVersionJson("1.21.1"));

            var instance = new GameInstanceRecord(
                "id",
                "island",
                "1.21.1",
                "vanilla",
                string.Empty,
                instanceRoot,
                0,
                false,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null);
            var paths = LaunchPaths.Resolve(instanceRoot);

            var version = LaunchModuleService.LoadVersion(instance, paths);

            Assert.Equal("1.21.1", version.Id);
            Assert.Equal(gameRoot, paths.SharedRoot);
        }
        finally
        {
            Directory.Delete(gameRoot, recursive: true);
        }
    }

    [Fact]
    public void Loader_profile_wins_over_vanilla_when_both_exist()
    {
        var gameRoot = CreateTempDirectory();
        try
        {
            var instanceRoot = Path.Combine(gameRoot, "versions", "1.21.1");
            Directory.CreateDirectory(instanceRoot);
            foreach (var id in new[] { "1.21.1", "1.21.1-forge-47.2.0" })
            {
                var directory = Path.Combine(gameRoot, "versions", id);
                Directory.CreateDirectory(directory);
                File.WriteAllText(
                    Path.Combine(directory, $"{id}.json"),
                    MinimalVersionJson(id));
            }

            var instance = new GameInstanceRecord(
                "id",
                "1.21.1",
                "1.21.1",
                "forge",
                "47.2.0",
                instanceRoot,
                0,
                false,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null);
            var paths = LaunchPaths.Resolve(instanceRoot);

            var version = LaunchModuleService.LoadVersion(instance, paths);

            Assert.Equal("1.21.1-forge-47.2.0", version.Id);
        }
        finally
        {
            Directory.Delete(gameRoot, recursive: true);
        }
    }

    [Fact]
    public void Ignores_non_version_json_in_version_directories()
    {
        var gameRoot = CreateTempDirectory();
        try
        {
            var instanceRoot = Path.Combine(gameRoot, "versions", "1.21.8");
            Directory.CreateDirectory(instanceRoot);
            File.WriteAllText(
                Path.Combine(instanceRoot, "1.21.8.json"),
                MinimalVersionJson("1.21.8"));

            // Real version directories double as game directories.
            foreach (var (name, content) in new[]
                     {
                         ("usercache.json", "[{\"name\":\"Steve\",\"uuid\":\"x\"}]"),
                         ("ops.json", "[{\"uuid\":\"x\",\"name\":\"Steve\",\"level\":4}]"),
                         ("sjmclcfg.json", "{\"gameDir\":\".\"}"),
                         ("install_profile.json", "{\"spec\":1,\"version\":\"1.21.8\"}"),
                     })
            {
                File.WriteAllText(Path.Combine(instanceRoot, name), content);
            }

            // A second version directory whose stray JSON has no profile.
            var otherVersion = Path.Combine(gameRoot, "versions", "AutumnTown");
            Directory.CreateDirectory(otherVersion);
            File.WriteAllText(
                Path.Combine(otherVersion, "AutumnTown.json"),
                MinimalVersionJson("AutumnTown"));
            File.WriteAllText(
                Path.Combine(otherVersion, "LxModCache.json"),
                "{\"files\":[]}");

            var instance = new GameInstanceRecord(
                "id",
                "1.21.8",
                "1.21.8",
                "vanilla",
                string.Empty,
                instanceRoot,
                0,
                false,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null);
            var paths = LaunchPaths.Resolve(instanceRoot);

            var version = LaunchModuleService.LoadVersion(instance, paths);

            Assert.Equal("1.21.8", version.Id);
        }
        finally
        {
            Directory.Delete(gameRoot, recursive: true);
        }
    }

    [Fact]
    public void Fallback_skips_instance_like_version_directories()
    {
        var gameRoot = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(gameRoot, "libraries"));
            Directory.CreateDirectory(Path.Combine(gameRoot, "assets"));
            var instanceRoot = Path.Combine(gameRoot, "versions", "MyPack");
            Directory.CreateDirectory(instanceRoot);

            var standard = Path.Combine(gameRoot, "versions", "1.21.1-neoforge");
            Directory.CreateDirectory(standard);
            var standardJson = Path.Combine(standard, "1.21.1-neoforge.json");
            File.WriteAllText(standardJson, MinimalVersionJson("1.21.1-neoforge"));
            File.SetLastWriteTimeUtc(standardJson, DateTime.UtcNow.AddHours(-2));

            // A real modpack folder with a newer profile; its name also
            // contains the game and loader, but it must not be borrowed.
            var decoy = Path.Combine(
                gameRoot,
                "versions",
                "The Shooting Star 1.21.1 - NeoForge");
            Directory.CreateDirectory(Path.Combine(decoy, "mods"));
            var decoyJson = Path.Combine(
                decoy,
                "The Shooting Star 1.21.1 - NeoForge.json");
            File.WriteAllText(
                decoyJson,
                MinimalVersionJson("The Shooting Star 1.21.1 - NeoForge"));
            File.SetLastWriteTimeUtc(decoyJson, DateTime.UtcNow);

            var instance = new GameInstanceRecord(
                "id",
                "MyPack",
                "1.21.1",
                "neoforge",
                "21.1.248",
                instanceRoot,
                0,
                false,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null);

            var version = LaunchModuleService.LoadVersion(
                instance,
                LaunchPaths.Resolve(instanceRoot));

            Assert.Equal("1.21.1-neoforge", version.Id);
        }
        finally
        {
            Directory.Delete(gameRoot, recursive: true);
        }
    }

    [Fact]
    public void Loader_instance_without_a_loader_profile_is_reported_as_not_installed()
    {
        var gameRoot = CreateTempDirectory();
        try
        {
            // Only the vanilla profile exists: launching it would silently boot
            // the unmodded game, so resolution must fail loudly instead.
            var versionDirectory = Path.Combine(gameRoot, "versions", "1.21.8");
            Directory.CreateDirectory(versionDirectory);
            File.WriteAllText(
                Path.Combine(versionDirectory, "1.21.8.json"),
                MinimalVersionJson("1.21.8"));
            var instanceRoot = Path.Combine(gameRoot, "versions", "1.21.8-forgetest");
            Directory.CreateDirectory(instanceRoot);

            var instance = new GameInstanceRecord(
                "id",
                "1.21.8 - ForgeTest",
                "1.21.8",
                "forge",
                "58.1.26",
                instanceRoot,
                0,
                false,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null);

            var exception = Assert.Throws<LaunchException>(
                () => LaunchModuleService.LoadVersion(
                    instance,
                    LaunchPaths.Resolve(instanceRoot)));

            Assert.Equal("LOADER_NOT_INSTALLED", exception.Code);
        }
        finally
        {
            Directory.Delete(gameRoot, recursive: true);
        }
    }

    [Fact]
    public void Loader_profile_without_listed_libraries_is_still_a_loader_profile()
    {
        var gameRoot = CreateTempDirectory();
        try
        {
            var versionDirectory = Path.Combine(gameRoot, "versions", "1.21.1-neoforge-21.1.264");
            Directory.CreateDirectory(versionDirectory);
            File.WriteAllText(
                Path.Combine(versionDirectory, "1.21.1-neoforge-21.1.264.json"),
                MinimalVersionJson("1.21.1-neoforge-21.1.264"));
            var instanceRoot = Path.Combine(gameRoot, "versions", "1.21.1-neoforge-21.1.264");
            Directory.CreateDirectory(instanceRoot);

            var instance = new GameInstanceRecord(
                "id",
                "1.21.1 - NeoForge",
                "1.21.1",
                "neoforge",
                "21.1.264",
                instanceRoot,
                0,
                false,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null);

            var version = LaunchModuleService.LoadVersion(
                instance,
                LaunchPaths.Resolve(instanceRoot));

            Assert.Equal("1.21.1-neoforge-21.1.264", version.Id);
        }
        finally
        {
            Directory.Delete(gameRoot, recursive: true);
        }
    }

    [Fact]
    public void Finds_the_neoforge_profile_whose_id_omits_the_game_version()
    {
        var gameRoot = CreateTempDirectory();
        try
        {
            // NeoForge installs "versions/neoforge-21.1.264" next to the
            // vanilla profile; the id carries no game version.
            var vanilla = Path.Combine(gameRoot, "versions", "1.21.1");
            Directory.CreateDirectory(vanilla);
            File.WriteAllText(
                Path.Combine(vanilla, "1.21.1.json"),
                MinimalVersionJson("1.21.1"));
            var loader = Path.Combine(gameRoot, "versions", "neoforge-21.1.264");
            Directory.CreateDirectory(loader);
            File.WriteAllText(
                Path.Combine(loader, "neoforge-21.1.264.json"),
                MinimalVersionJson("neoforge-21.1.264"));

            var instanceRoot = Path.Combine(gameRoot, "versions", "1.21.1-neoforge-21.1.264");
            Directory.CreateDirectory(instanceRoot);

            var instance = new GameInstanceRecord(
                "id",
                "1.21.1 - NeoForge",
                "1.21.1",
                "neoforge",
                "21.1.264",
                instanceRoot,
                0,
                false,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null);

            var version = LaunchModuleService.LoadVersion(
                instance,
                LaunchPaths.Resolve(instanceRoot));

            Assert.Equal("neoforge-21.1.264", version.Id);
        }
        finally
        {
            Directory.Delete(gameRoot, recursive: true);
        }
    }

    [Fact]
    public void Finds_the_forge_profile_by_its_installer_id()
    {
        var gameRoot = CreateTempDirectory();
        try
        {
            var vanilla = Path.Combine(gameRoot, "versions", "1.21.8");
            Directory.CreateDirectory(vanilla);
            File.WriteAllText(
                Path.Combine(vanilla, "1.21.8.json"),
                MinimalVersionJson("1.21.8"));
            var loader = Path.Combine(gameRoot, "versions", "1.21.8-forge-58.1.26");
            Directory.CreateDirectory(loader);
            File.WriteAllText(
                Path.Combine(loader, "1.21.8-forge-58.1.26.json"),
                MinimalVersionJson("1.21.8-forge-58.1.26"));

            var instanceRoot = Path.Combine(gameRoot, "versions", "1.21.8-forgetest");
            Directory.CreateDirectory(instanceRoot);

            var instance = new GameInstanceRecord(
                "id",
                "1.21.8 - ForgeTest",
                "1.21.8",
                "forge",
                "58.1.26",
                instanceRoot,
                0,
                false,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null);

            var version = LaunchModuleService.LoadVersion(
                instance,
                LaunchPaths.Resolve(instanceRoot));

            Assert.Equal("1.21.8-forge-58.1.26", version.Id);
        }
        finally
        {
            Directory.Delete(gameRoot, recursive: true);
        }
    }

    [Fact]
    public void Does_not_borrow_a_profile_for_a_different_loader_version()
    {
        var gameRoot = CreateTempDirectory();
        try
        {
            var vanilla = Path.Combine(gameRoot, "versions", "1.21.1");
            Directory.CreateDirectory(vanilla);
            File.WriteAllText(
                Path.Combine(vanilla, "1.21.1.json"),
                MinimalVersionJson("1.21.1"));
            var other = Path.Combine(gameRoot, "versions", "neoforge-21.1.248");
            Directory.CreateDirectory(other);
            File.WriteAllText(
                Path.Combine(other, "neoforge-21.1.248.json"),
                MinimalVersionJson("neoforge-21.1.248"));

            var instanceRoot = Path.Combine(gameRoot, "versions", "pack");
            Directory.CreateDirectory(instanceRoot);

            var instance = new GameInstanceRecord(
                "id",
                "pack",
                "1.21.1",
                "neoforge",
                "21.1.264",
                instanceRoot,
                0,
                false,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null);

            var exception = Assert.Throws<LaunchException>(
                () => LaunchModuleService.LoadVersion(
                    instance,
                    LaunchPaths.Resolve(instanceRoot)));

            Assert.Equal("LOADER_NOT_INSTALLED", exception.Code);
        }
        finally
        {
            Directory.Delete(gameRoot, recursive: true);
        }
    }

    private static string MinimalVersionJson(string id)
        => JsonSerializer.Serialize(new
        {
            id,
            type = "release",
            mainClass = "net.minecraft.client.main.Main",
            assets = "5",
            assetIndex = new
            {
                id = "5",
                url = "https://piston-meta.mojang.com/v1/packages/5.json",
                sha1 = new string('a', 40),
                size = 1,
            },
            downloads = new
            {
                client = new
                {
                    url = "https://piston-data.mojang.com/v1/objects/client.jar",
                    sha1 = new string('b', 40),
                    size = 1,
                },
            },
            libraries = Array.Empty<object>(),
        });

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-launch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
