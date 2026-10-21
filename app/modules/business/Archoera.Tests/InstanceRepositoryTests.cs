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
                       "rootPath":"{{root}}"}
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
