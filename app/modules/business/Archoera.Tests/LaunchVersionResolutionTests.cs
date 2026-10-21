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
