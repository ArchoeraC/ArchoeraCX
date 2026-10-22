using System.Text.Json;
using Archoera.Domain.Instances;
using Archoera.Infrastructure.Launch;

namespace Archoera.Tests;

/// <summary>
/// Local-first profile recovery: a missing or unmerged instance JSON is rebuilt
/// from sibling profiles and the base version JSON without touching the network.
/// </summary>
public sealed class LaunchProfileRecoveryTests
{
    [Fact]
    public async Task Recovers_a_missing_instance_profile_from_a_local_loader_profile()
    {
        var root = CreateRoot();
        try
        {
            var versions = Path.Combine(root, "versions");
            var instanceRoot = Path.Combine(versions, "MyPack");
            Directory.CreateDirectory(instanceRoot);
            File.WriteAllText(Path.Combine(instanceRoot, "MyPack.jar"), "jar");
            WriteVersion(versions, "1.21.1", BaseJson("1.21.1"));
            WriteVersion(
                versions,
                "1.21.1-neoforge-21.1.248",
                LoaderJson("1.21.1-neoforge-21.1.248", "1.21.1"));

            var paths = LaunchPaths.Resolve(instanceRoot);
            var instance = Instance("neoforge", "21.1.248", instanceRoot);

            Assert.True(await LaunchProfileRecovery.TryRecoverAsync(instance, paths));

            var profile = Path.Combine(instanceRoot, "MyPack.json");
            using var document = JsonDocument.Parse(File.ReadAllText(profile));
            var rootElement = document.RootElement;
            Assert.Equal("MyPack", rootElement.GetProperty("id").GetString());
            Assert.False(rootElement.TryGetProperty("inheritsFrom", out _));
            Assert.Equal(
                "cpw.mods.bootstraplauncher.BootstrapLauncher",
                rootElement.GetProperty("mainClass").GetString());
            Assert.Contains(
                "net.neoforged:neoforge:21.1.248:universal",
                rootElement.GetProperty("libraries").EnumerateArray()
                    .Select(item => item.GetProperty("name").GetString()));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Merges_an_existing_unmerged_instance_profile_in_place()
    {
        var root = CreateRoot();
        try
        {
            var versions = Path.Combine(root, "versions");
            var instanceRoot = Path.Combine(versions, "MyPack");
            Directory.CreateDirectory(instanceRoot);
            File.WriteAllText(Path.Combine(instanceRoot, "MyPack.jar"), "jar");
            WriteVersion(versions, "1.21.1", BaseJson("1.21.1"));
            File.WriteAllText(
                Path.Combine(instanceRoot, "MyPack.json"),
                LoaderJson("MyPack", "1.21.1"));

            var paths = LaunchPaths.Resolve(instanceRoot);
            var instance = Instance("neoforge", "21.1.248", instanceRoot);

            Assert.True(await LaunchProfileRecovery.TryRecoverAsync(instance, paths));

            using var document = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(instanceRoot, "MyPack.json")));
            Assert.False(document.RootElement.TryGetProperty("inheritsFrom", out _));
            Assert.Equal("MyPack", document.RootElement.GetProperty("id").GetString());
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Matches_a_NeoForge_installer_profile_without_the_game_in_its_id()
    {
        var root = CreateRoot();
        try
        {
            var versions = Path.Combine(root, "versions");
            var instanceRoot = Path.Combine(versions, "MyPack");
            Directory.CreateDirectory(instanceRoot);
            File.WriteAllText(Path.Combine(instanceRoot, "MyPack.jar"), "jar");
            WriteVersion(versions, "1.21.1", BaseJson("1.21.1"));
            WriteVersion(
                versions,
                "neoforge-21.1.248",
                LoaderJson("neoforge-21.1.248", "1.21.1"));

            var paths = LaunchPaths.Resolve(instanceRoot);
            var instance = Instance("neoforge", "21.1.248", instanceRoot);

            Assert.True(await LaunchProfileRecovery.TryRecoverAsync(instance, paths));

            using var document = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(instanceRoot, "MyPack.json")));
            Assert.False(document.RootElement.TryGetProperty("inheritsFrom", out _));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Ignores_a_real_instance_in_the_versions_folder()
    {
        var root = CreateRoot();
        try
        {
            var versions = Path.Combine(root, "versions");
            var instanceRoot = Path.Combine(versions, "MyPack");
            Directory.CreateDirectory(instanceRoot);
            File.WriteAllText(Path.Combine(instanceRoot, "MyPack.jar"), "jar");
            WriteVersion(versions, "1.21.1", BaseJson("1.21.1"));
            WriteVersion(
                versions,
                "neoforge-21.1.248",
                LoaderJson("neoforge-21.1.248", "1.21.1"));

            // A sibling modpack whose name contains the game and loader must
            // not be borrowed as this instance's profile.
            var decoy = Path.Combine(versions, "The Shooting Star 1.21.1 - NeoForge");
            Directory.CreateDirectory(Path.Combine(decoy, "mods"));
            File.WriteAllText(
                Path.Combine(decoy, "The Shooting Star 1.21.1 - NeoForge.json"),
                LoaderJson("The Shooting Star 1.21.1 - NeoForge", "1.21.1")
                    .Replace("\"mainClass\"", "\"marker\":\"decoy\",\"mainClass\""));

            var paths = LaunchPaths.Resolve(instanceRoot);
            var instance = Instance("neoforge", "21.1.248", instanceRoot);

            Assert.True(await LaunchProfileRecovery.TryRecoverAsync(instance, paths));

            var text = File.ReadAllText(Path.Combine(instanceRoot, "MyPack.json"));
            Assert.DoesNotContain("decoy", text);
            using var document = JsonDocument.Parse(text);
            Assert.Equal(
                "cpw.mods.bootstraplauncher.BootstrapLauncher",
                document.RootElement.GetProperty("mainClass").GetString());
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Refuses_a_profile_with_a_different_loader_version()
    {
        var root = CreateRoot();
        try
        {
            var versions = Path.Combine(root, "versions");
            var instanceRoot = Path.Combine(versions, "MyPack");
            Directory.CreateDirectory(instanceRoot);
            File.WriteAllText(Path.Combine(instanceRoot, "MyPack.jar"), "jar");
            WriteVersion(versions, "1.21.1", BaseJson("1.21.1"));
            WriteVersion(
                versions,
                "1.21.1-forge-52.1.20",
                """{"id":"1.21.1-forge-52.1.20","inheritsFrom":"1.21.1","mainClass":"x","libraries":[]}""");

            var paths = LaunchPaths.Resolve(instanceRoot);
            var instance = Instance("neoforge", "21.1.248", instanceRoot);

            Assert.False(await LaunchProfileRecovery.TryRecoverAsync(instance, paths));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Refuses_a_profile_for_another_loader_or_game()
    {
        var root = CreateRoot();
        try
        {
            var versions = Path.Combine(root, "versions");
            var instanceRoot = Path.Combine(versions, "MyPack");
            Directory.CreateDirectory(instanceRoot);
            File.WriteAllText(Path.Combine(instanceRoot, "MyPack.jar"), "jar");
            WriteVersion(versions, "1.21.1", BaseJson("1.21.1"));
            WriteVersion(
                versions,
                "fabric-loader-0.16.14-1.21.1",
                """{"id":"fabric-loader-0.16.14-1.21.1","inheritsFrom":"1.21.1","mainClass":"x","libraries":[]}""");

            var paths = LaunchPaths.Resolve(instanceRoot);
            var instance = Instance("neoforge", "21.1.248", instanceRoot);

            Assert.False(await LaunchProfileRecovery.TryRecoverAsync(instance, paths));
            Assert.False(File.Exists(Path.Combine(instanceRoot, "MyPack.json")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Returns_false_when_only_the_base_profile_is_local()
    {
        var root = CreateRoot();
        try
        {
            var versions = Path.Combine(root, "versions");
            var instanceRoot = Path.Combine(versions, "MyPack");
            Directory.CreateDirectory(instanceRoot);
            WriteVersion(versions, "1.21.1", BaseJson("1.21.1"));

            var paths = LaunchPaths.Resolve(instanceRoot);
            var instance = Instance("neoforge", "21.1.248", instanceRoot);

            Assert.False(await LaunchProfileRecovery.TryRecoverAsync(instance, paths));
        }
        finally
        {
            Delete(root);
        }
    }

    private static string BaseJson(string id) => $$"""
        {
          "id": "{{id}}",
          "type": "release",
          "mainClass": "net.minecraft.client.main.Main",
          "assets": "17",
          "assetIndex": {
            "id": "17",
            "url": "https://piston-meta.mojang.com/v1/packages/index/17.json",
            "sha1": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "size": 10,
            "totalSize": 20
          },
          "downloads": {
            "client": {
              "url": "https://piston-data.mojang.com/v1/objects/client/client.jar",
              "sha1": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
              "size": 1000
            }
          },
          "libraries": []
        }
        """;

    private static string LoaderJson(string id, string parent) => $$"""
        {
          "id": "{{id}}",
          "inheritsFrom": "{{parent}}",
          "type": "release",
          "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
          "libraries": [
            { "name": "net.neoforged:neoforge:21.1.248:universal" }
          ],
          "arguments": {
            "game": ["--fml.neoForgeVersion", "21.1.248"]
          }
        }
        """;

    private static string CreateRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "archoera-profile-recovery",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "libraries"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        return root;
    }

    private static void WriteVersion(string versions, string id, string json)
    {
        var directory = Path.Combine(versions, id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"{id}.json"), json);
    }

    private static GameInstanceRecord Instance(
        string loader,
        string loaderVersion,
        string rootPath)
        => new(
            "id",
            "MyPack",
            "1.21.1",
            loader,
            loaderVersion,
            rootPath,
            0,
            false,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null);

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
