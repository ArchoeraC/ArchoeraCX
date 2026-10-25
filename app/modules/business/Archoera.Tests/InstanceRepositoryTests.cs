using System.Text.Json;
using Archoera.Infrastructure.Instances;
using Archoera.Infrastructure.Settings;

namespace Archoera.Tests;

public sealed class InstanceRepositoryTests
{
    [Fact]
    public async Task Create_writes_instance_json_and_lists_the_instance()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "custom-instance");
            var repository = new JsonInstanceRepository(
                dataRoot,
                new JsonAppSettingsStore(dataRoot));

            var created = await repository.CreateAsync(
                "My Instance",
                "1.21.4",
                "fabric",
                "0.16.10",
                root);

            Assert.Equal(Path.GetFullPath(root), created.RootPath);
            Assert.True(File.Exists(Path.Combine(root, ".archoera", "instance.json")));
            Assert.True(File.Exists(Path.Combine(dataRoot, "instances.json")));

            var listed = await repository.ListAsync();
            var item = Assert.Single(listed);
            Assert.Equal("My Instance", item.Name);
            Assert.Equal("fabric", item.Loader);
            Assert.Equal(created.Id, item.Id);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Create_without_a_root_uses_version_isolation()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var repository = new JsonInstanceRepository(
                dataRoot,
                new JsonAppSettingsStore(dataRoot));

            var created = await repository.CreateAsync(
                "My Instance",
                "1.21.4",
                "fabric",
                "0.16.10",
                rootPath: null);

            var marker = $"{Path.DirectorySeparatorChar}versions{Path.DirectorySeparatorChar}";
            Assert.Contains(marker, created.RootPath);
            Assert.Equal("my-instance", Path.GetFileName(created.RootPath));
            Assert.True(
                File.Exists(Path.Combine(created.RootPath, ".archoera", "instance.json")));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Update_and_delete_modify_the_registry_and_instance_file()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "instance");
            var repository = new JsonInstanceRepository(
                dataRoot,
                new JsonAppSettingsStore(dataRoot));
            var created = await repository.CreateAsync(
                "Before",
                "1.20.1",
                "forge",
                "47.2.0",
                root);

            var updated = await repository.UpdateAsync(
                created.Id,
                "After",
                favorite: true,
                lastPlayedAt: DateTimeOffset.UtcNow);
            Assert.NotNull(updated);
            Assert.Equal("After", updated!.Name);
            Assert.True(updated.Favorite);

            var json = await File.ReadAllTextAsync(
                Path.Combine(root, ".archoera", "instance.json"));
            using var document = JsonDocument.Parse(json);
            Assert.Equal(
                "After",
                document.RootElement.GetProperty("name").GetString());

            Assert.True(await repository.DeleteAsync(created.Id));
            Assert.Empty(await repository.ListAsync());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Create_uses_the_configured_instances_root_when_path_is_omitted()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var instancesRoot = Path.Combine(directory, "games");
            var settings = new JsonAppSettingsStore(dataRoot);
            await settings.SaveInstancesRootAsync(instancesRoot);
            var repository = new JsonInstanceRepository(dataRoot, settings);

            var created = await repository.CreateAsync(
                "Default Location",
                "1.21.1",
                "neoforge",
                "21.1.209",
                rootPath: null);

            Assert.StartsWith(
                Path.GetFullPath(instancesRoot),
                created.RootPath,
                StringComparison.Ordinal);
            Assert.True(Directory.Exists(created.RootPath));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Sqlite_repository_persists_and_lists_instances()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "sqlite-instance");
            var repository = new SqliteInstanceRepository(
                dataRoot,
                new JsonAppSettingsStore(dataRoot));

            var created = await repository.CreateAsync(
                "SQLite Instance",
                "1.21.4",
                "fabric",
                "0.16.10",
                root);
            Assert.True(File.Exists(Path.Combine(dataRoot, "archoera.db")));

            var listed = await repository.ListAsync();
            var item = Assert.Single(listed);
            Assert.Equal(created.Id, item.Id);
            Assert.Equal(Path.GetFullPath(root), item.RootPath);

            var updated = await repository.UpdateAsync(created.Id, "SQLite Renamed", true, null);
            Assert.Equal("SQLite Renamed", updated!.Name);
            Assert.True(updated.Favorite);
            Assert.True(await repository.DeleteAsync(created.Id));
            Assert.Empty(await repository.ListAsync());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Sync_recounts_mods_instead_of_trusting_a_stale_instance_file()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var repository = new SqliteInstanceRepository(
                dataRoot,
                new JsonAppSettingsStore(dataRoot));
            var created = await repository.CreateAsync(
                "Pack Instance",
                "1.21.1",
                "fabric",
                "0.19.3",
                rootPath: null);

            var mods = Path.Combine(created.RootPath, "mods");
            Directory.CreateDirectory(mods);
            await File.WriteAllTextAsync(Path.Combine(mods, "a.jar"), "a");
            await File.WriteAllTextAsync(Path.Combine(mods, "b.jar"), "b");

            var report = await repository.SyncAsync(null, dryRun: false);
            var item = Assert.Single(report.Items);
            Assert.Equal(2, item.ModCount);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Module_create_list_delete_round_trip()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "instance");
            var service = new InstanceModuleService(
                new JsonInstanceRepository(dataRoot, new JsonAppSettingsStore(dataRoot)));

            using var createDocument = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.create",
                    $$"""
                      {"name":"UI Instance","gameVersion":"1.21.4",
                       "loader":"fabric","loaderVersion":"0.16.10",
                       "rootPath":{{TestJson.Quote(root)}}}
                      """,
                    CancellationToken.None));
            Assert.True(createDocument.RootElement.GetProperty("ok").GetBoolean());
            var id = createDocument.RootElement
                .GetProperty("data")
                .GetProperty("instance")
                .GetProperty("id")
                .GetString();
            Assert.NotNull(id);

            using var listDocument = JsonDocument.Parse(
                await service.HandleAsync("instance.list", "{}", CancellationToken.None));
            Assert.Single(
                listDocument.RootElement.GetProperty("data").GetProperty("items")
                    .EnumerateArray());

            using var deleteDocument = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.delete",
                    $$"""{"id":"{{id}}"}""",
                    CancellationToken.None));
            Assert.True(
                deleteDocument.RootElement.GetProperty("data").GetProperty("deleted").GetBoolean());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Module_lists_toggles_and_removes_instance_mods()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "instance");
            var mods = Path.Combine(root, "mods");
            Directory.CreateDirectory(mods);
            await File.WriteAllTextAsync(Path.Combine(mods, "alpha.jar"), "a");
            await File.WriteAllTextAsync(Path.Combine(mods, "beta.jar.disabled"), "b");

            var service = new InstanceModuleService(
                new JsonInstanceRepository(dataRoot, new JsonAppSettingsStore(dataRoot)));
            using var createDocument = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.create",
                    $$"""
                      {"name":"Mods","gameVersion":"1.21.4",
                       "loader":"fabric","loaderVersion":"0.16.10",
                       "rootPath":{{TestJson.Quote(root)}}}
                      """,
                    CancellationToken.None));
            var id = createDocument.RootElement
                .GetProperty("data")
                .GetProperty("instance")
                .GetProperty("id")
                .GetString();
            Assert.NotNull(id);

            using var listDocument = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.mods.list",
                    $$"""{"instanceId":"{{id}}"}""",
                    CancellationToken.None));
            var list = listDocument.RootElement
                .GetProperty("data")
                .GetProperty("items")
                .EnumerateArray()
                .ToArray();
            Assert.Equal(2, list.Length);
            Assert.Contains(list, item => item.GetProperty("enabled").GetBoolean());

            using var disableDocument = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.mods.setEnabled",
                    $$"""
                      {"instanceId":"{{id}}","fileName":"alpha.jar","enabled":false}
                      """,
                    CancellationToken.None));
            Assert.False(
                disableDocument.RootElement
                    .GetProperty("data")
                    .GetProperty("item")
                    .GetProperty("enabled")
                    .GetBoolean());
            Assert.True(File.Exists(Path.Combine(mods, "alpha.jar.disabled")));

            using var enableDocument = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.mods.setEnabled",
                    $$"""
                      {"instanceId":"{{id}}","fileName":"beta.jar.disabled","enabled":true}
                      """,
                    CancellationToken.None));
            Assert.True(
                enableDocument.RootElement
                    .GetProperty("data")
                    .GetProperty("item")
                    .GetProperty("enabled")
                    .GetBoolean());
            Assert.True(File.Exists(Path.Combine(mods, "beta.jar")));

            using var removeDocument = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.mods.remove",
                    $$"""
                      {"instanceId":"{{id}}","fileName":"alpha.jar.disabled"}
                      """,
                    CancellationToken.None));
            Assert.True(
                removeDocument.RootElement
                    .GetProperty("data")
                    .GetProperty("removed")
                    .GetBoolean());
            Assert.False(File.Exists(Path.Combine(mods, "alpha.jar.disabled")));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Module_rejects_mod_path_traversal()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "instance");
            Directory.CreateDirectory(Path.Combine(root, "mods"));
            var service = new InstanceModuleService(
                new JsonInstanceRepository(dataRoot, new JsonAppSettingsStore(dataRoot)));
            using var createDocument = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.create",
                    $$"""
                      {"name":"Mods","gameVersion":"1.21.4",
                       "loader":"fabric","loaderVersion":"0.16.10",
                       "rootPath":{{TestJson.Quote(root)}}}
                      """,
                    CancellationToken.None));
            var id = createDocument.RootElement
                .GetProperty("data")
                .GetProperty("instance")
                .GetProperty("id")
                .GetString();
            using var document = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.mods.remove",
                    $$"""
                      {"instanceId":"{{id}}","fileName":"../escape.jar"}
                      """,
                    CancellationToken.None));
            Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "E_INVALID",
                document.RootElement
                    .GetProperty("error")
                    .GetProperty("code")
                    .GetString());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Sqlite_sync_discovers_sjmcl_versions_layout()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "minecraft");
            var instanceDir = Path.Combine(root, "versions", "Server Test");
            Directory.CreateDirectory(Path.Combine(instanceDir, "mods"));
            await File.WriteAllTextAsync(
                Path.Combine(instanceDir, "mods", "example.jar"),
                "not a real jar");
            await File.WriteAllTextAsync(
                Path.Combine(instanceDir, "sjmclcfg.json"),
                """
                {
                  "name": "Server Test",
                  "version": "1.21.8",
                  "modLoader": {
                    "loaderType": "Fabric",
                    "version": "fabric-loader-0.18.4"
                  }
                }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(instanceDir, "Server Test.json"),
                """{"id":"Server Test","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient"}""");
            await File.WriteAllTextAsync(
                Path.Combine(root, "lx_profiles.json"),
                $$"""
                {
                  "profiles": {
                    "p1": {
                      "name": "Server Test",
                      "gameDir": {{JsonSerializer.Serialize(instanceDir)}},
                      "lastUsed": "2026-10-20T12:00:00+00:00"
                    }
                  }
                }
                """);

            var settings = new JsonAppSettingsStore(dataRoot);
            await settings.SaveInstancesRootAsync(root);
            var repository = new SqliteInstanceRepository(dataRoot, settings);
            var report = await repository.SyncAsync(null, dryRun: false);

            var item = Assert.Single(report.Items);
            Assert.Equal("Server Test", item.Name);
            Assert.Equal("1.21.8", item.GameVersion);
            Assert.Equal("fabric", item.Loader);
            Assert.Equal("0.18.4", item.LoaderVersion);
            Assert.Equal(1, item.ModCount);
            Assert.Equal("inst-", item.Id[..5]);
            Assert.NotNull(item.LastPlayedAt);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Sqlite_sync_adopts_instances_from_configured_roots()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var rootA = Path.Combine(directory, "root-a");
            var rootB = Path.Combine(directory, "root-b");
            var settings = new JsonAppSettingsStore(dataRoot);
            await settings.SaveInstancesRootsAsync([rootA, rootB]);
            var seededRoot = Path.Combine(rootB, "seeded");
            var metadataDirectory = Path.Combine(seededRoot, ".archoera");
            Directory.CreateDirectory(metadataDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(metadataDirectory, "instance.json"),
                """
                {
                  "schemaVersion": 1,
                  "id": "inst-seeded",
                  "name": "Seeded",
                  "gameVersion": "1.21.1",
                  "loader": "fabric",
                  "loaderVersion": "0.16.10",
                  "rootPath": "/stale/path",
                  "modCount": 0,
                  "favorite": false,
                  "createdAt": "2026-01-01T00:00:00Z",
                  "updatedAt": "2026-01-01T00:00:00Z"
                }
                """);

            var repository = new SqliteInstanceRepository(dataRoot, settings);
            var report = await repository.SyncAsync(null, dryRun: false);

            Assert.Contains("inst-seeded", report.Adopted);
            var item = Assert.Single(report.Items);
            Assert.Equal(Path.GetFullPath(seededRoot), item.RootPath);
            Assert.Equal(2, report.Roots.Count);
            Assert.Equal(0, report.Roots[0].InstanceCount);
            Assert.Equal(1, report.Roots[1].InstanceCount);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Delete_with_files_removes_the_instance_directory()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var settings = new JsonAppSettingsStore(dataRoot);
            var instancesRoot = Path.Combine(directory, "instances");
            await settings.SaveInstancesRootAsync(instancesRoot);
            var service = new InstanceModuleService(
                new SqliteInstanceRepository(dataRoot, settings),
                settings);

            var root = Path.Combine(instancesRoot, "isolated-instance");
            var id = await CreateAsync(service, root, "With Files");
            await File.WriteAllTextAsync(Path.Combine(root, "options.txt"), "fov:70");
            Assert.True(Directory.Exists(root));

            using var document = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.delete",
                    $$"""{"id":"{{id}}","deleteFiles":true}""",
                    CancellationToken.None));
            var data = document.RootElement.GetProperty("data");
            Assert.True(data.GetProperty("deleted").GetBoolean());
            Assert.True(data.GetProperty("filesDeleted").GetBoolean());
            Assert.False(Directory.Exists(root));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Delete_keeps_an_instance_outside_the_configured_root()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var settings = new JsonAppSettingsStore(dataRoot);
            await settings.SaveInstancesRootAsync(Path.Combine(directory, "instances"));
            var service = new InstanceModuleService(
                new SqliteInstanceRepository(dataRoot, settings),
                settings);

            // A custom instance created outside the configured root still has
            // the marker, but recursive deletion must not follow an arbitrary
            // registry path.
            var root = Path.Combine(directory, "custom-instance");
            var id = await CreateAsync(service, root, "Custom Root");
            Assert.True(File.Exists(Path.Combine(root, ".archoera", "instance.json")));

            using var document = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.delete",
                    $$"""{"id":"{{id}}","deleteFiles":true}""",
                    CancellationToken.None));
            var data = document.RootElement.GetProperty("data");
            Assert.True(data.GetProperty("deleted").GetBoolean());
            Assert.False(data.GetProperty("filesDeleted").GetBoolean());
            Assert.Equal(
                "outside_root",
                data.GetProperty("filesSkippedReason").GetString());
            Assert.True(Directory.Exists(root));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Delete_keeps_a_shared_instance_root()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var sharedRoot = Path.Combine(directory, "shared");
            Directory.CreateDirectory(sharedRoot);
            var settings = new JsonAppSettingsStore(dataRoot);
            await settings.SaveInstancesRootsAsync([sharedRoot]);
            var service = new InstanceModuleService(
                new SqliteInstanceRepository(dataRoot, settings),
                settings);

            var id = await CreateAsync(service, sharedRoot, "Shared Root");
            Assert.True(File.Exists(Path.Combine(sharedRoot, ".archoera", "instance.json")));

            using var document = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.delete",
                    $$"""{"id":"{{id}}","deleteFiles":true}""",
                    CancellationToken.None));
            var data = document.RootElement.GetProperty("data");
            Assert.True(data.GetProperty("deleted").GetBoolean());
            Assert.False(data.GetProperty("filesDeleted").GetBoolean());
            Assert.Equal(
                "shared_root",
                data.GetProperty("filesSkippedReason").GetString());
            Assert.True(Directory.Exists(sharedRoot));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Delete_keeps_a_folder_without_the_instance_marker()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var settings = new JsonAppSettingsStore(dataRoot);
            var service = new InstanceModuleService(
                new SqliteInstanceRepository(dataRoot, settings),
                settings);

            var root = Path.Combine(directory, "unmanaged");
            var id = await CreateAsync(service, root, "Unmanaged");
            File.Delete(Path.Combine(root, ".archoera", "instance.json"));

            using var document = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.delete",
                    $$"""{"id":"{{id}}","deleteFiles":true}""",
                    CancellationToken.None));
            var data = document.RootElement.GetProperty("data");
            Assert.False(data.GetProperty("filesDeleted").GetBoolean());
            Assert.Equal(
                "not_managed",
                data.GetProperty("filesSkippedReason").GetString());
            Assert.True(Directory.Exists(root));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Delete_keeps_files_unless_requested()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var settings = new JsonAppSettingsStore(dataRoot);
            var service = new InstanceModuleService(
                new SqliteInstanceRepository(dataRoot, settings),
                settings);

            var root = Path.Combine(directory, "keep-me");
            var id = await CreateAsync(service, root, "Keep Files");

            using var document = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.delete",
                    $$"""{"id":"{{id}}"}""",
                    CancellationToken.None));
            var data = document.RootElement.GetProperty("data");
            Assert.True(data.GetProperty("deleted").GetBoolean());
            Assert.False(data.GetProperty("filesDeleted").GetBoolean());
            Assert.True(Directory.Exists(root));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Sqlite_sync_ignores_shared_loader_profiles()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "minecraft");

            var instanceDir = Path.Combine(root, "versions", "AutumnTown");
            Directory.CreateDirectory(Path.Combine(instanceDir, "mods"));
            await File.WriteAllTextAsync(
                Path.Combine(instanceDir, "mods", "example.jar"),
                "not a real jar");
            await File.WriteAllTextAsync(
                Path.Combine(instanceDir, "AutumnTown.json"),
                """
                {"id":"AutumnTown","inheritsFrom":"1.21.1",
                 "mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher"}
                """);

            // What installing a pack with a bundled NeoForge leaves behind: a
            // launch profile shared by every instance of that loader build.
            var profileDir = Path.Combine(root, "versions", "neoforge-21.1.176");
            Directory.CreateDirectory(profileDir);
            await File.WriteAllTextAsync(
                Path.Combine(profileDir, "neoforge-21.1.176.json"),
                """
                {"id":"neoforge-21.1.176","inheritsFrom":"1.21.1",
                 "mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher",
                 "libraries":[{"name":"net.neoforged:neoforge:21.1.176"}]}
                """);
            await File.WriteAllTextAsync(
                Path.Combine(profileDir, "neoforge-21.1.176.jar"),
                "not a real jar");

            var settings = new JsonAppSettingsStore(dataRoot);
            await settings.SaveInstancesRootAsync(root);
            var repository = new SqliteInstanceRepository(dataRoot, settings);
            var report = await repository.SyncAsync(null, dryRun: false);

            var item = Assert.Single(report.Items);
            Assert.Equal("AutumnTown", item.Name);
            Assert.Equal("1.21.1", item.GameVersion);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Sqlite_sync_keeps_a_loader_profile_that_became_an_instance()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "minecraft");
            var profileDir = Path.Combine(root, "versions", "1.21.1-forge-52.0.1");
            Directory.CreateDirectory(Path.Combine(profileDir, "config"));
            await File.WriteAllTextAsync(
                Path.Combine(profileDir, "1.21.1-forge-52.0.1.json"),
                """
                {"id":"1.21.1-forge-52.0.1","inheritsFrom":"1.21.1",
                 "mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher"}
                """);

            var settings = new JsonAppSettingsStore(dataRoot);
            await settings.SaveInstancesRootAsync(root);
            var repository = new SqliteInstanceRepository(dataRoot, settings);
            var report = await repository.SyncAsync(null, dryRun: false);

            var item = Assert.Single(report.Items);
            Assert.Equal("1.21.1-forge-52.0.1", item.Name);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Sqlite_sync_drops_a_row_that_turned_out_to_be_a_loader_profile()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "minecraft");
            var profileDir = Path.Combine(root, "versions", "neoforge-21.1.176");
            var mods = Path.Combine(profileDir, "mods");
            Directory.CreateDirectory(mods);
            await File.WriteAllTextAsync(Path.Combine(mods, "example.jar"), "not a real jar");
            await File.WriteAllTextAsync(
                Path.Combine(profileDir, "neoforge-21.1.176.json"),
                """
                {"id":"neoforge-21.1.176","inheritsFrom":"1.21.1",
                 "mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher"}
                """);

            var settings = new JsonAppSettingsStore(dataRoot);
            await settings.SaveInstancesRootAsync(root);
            var repository = new SqliteInstanceRepository(dataRoot, settings);

            // An older build adopted the profile because it carried mods.
            var adopted = await repository.SyncAsync(null, dryRun: false);
            Assert.Single(adopted.Items);

            Directory.Delete(mods, recursive: true);
            var cleaned = await repository.SyncAsync(null, dryRun: false);

            Assert.Empty(cleaned.Items);
            Assert.Empty(await repository.ListAsync());
            // The profile folder is a launch target, not data to delete.
            Assert.True(Directory.Exists(profileDir));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Json_sync_drops_a_row_that_turned_out_to_be_a_loader_profile()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "minecraft");
            var profileDir = Path.Combine(root, "versions", "neoforge-21.1.176");
            Directory.CreateDirectory(profileDir);
            await File.WriteAllTextAsync(
                Path.Combine(profileDir, "neoforge-21.1.176.json"),
                """
                {"id":"neoforge-21.1.176","inheritsFrom":"1.21.1",
                 "mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher"}
                """);
            await File.WriteAllTextAsync(
                Path.Combine(profileDir, "neoforge-21.1.176.jar"),
                "not a real jar");

            var settings = new JsonAppSettingsStore(dataRoot);
            await settings.SaveInstancesRootAsync(root);
            var repository = new JsonInstanceRepository(dataRoot, settings);
            await repository.CreateAsync(
                "neoforge-21.1.176",
                "1.21.1",
                "neoforge",
                "21.1.176",
                profileDir);

            // The registry entry survived after the metadata went away.
            Directory.Delete(Path.Combine(profileDir, ".archoera"), recursive: true);
            var cleaned = await repository.SyncAsync(null, dryRun: false);

            Assert.Empty(cleaned.Items);
            Assert.True(Directory.Exists(profileDir));
        }
        finally
        {
            Delete(directory);
        }
    }

    private static async Task<string> CreateAsync(
        InstanceModuleService service,
        string rootPath,
        string name)
    {
        using var document = JsonDocument.Parse(
            await service.HandleAsync(
                "instance.create",
                $$"""
                  {"name":"{{name}}","gameVersion":"1.21.4",
                   "loader":"vanilla","loaderVersion":"",
                   "rootPath":{{TestJson.Quote(rootPath)}}}
                  """,
                CancellationToken.None));
        var id = document.RootElement
            .GetProperty("data")
            .GetProperty("instance")
            .GetProperty("id")
            .GetString();
        Assert.NotNull(id);
        return id!;
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-instances",
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
