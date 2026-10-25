using System.Text.Json;
using Archoera.Domain.Instances;
using Archoera.Infrastructure.Instances;
using Archoera.Infrastructure.Settings;

namespace Archoera.Tests;

public sealed class InstanceOverridesTests
{
    [Fact]
    public async Task Json_repository_round_trips_overrides()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "instance");
            var repository = new JsonInstanceRepository(dataRoot, new JsonAppSettingsStore(dataRoot));
            var created = await repository.CreateAsync(
                "Overrides",
                "1.21.4",
                "fabric",
                "0.16.10",
                root);

            var overrides = new InstanceOverrides(
                MemoryMinMb: 1024,
                MemoryMaxMb: 4096,
                JvmArgs: "-XX:+UseG1GC",
                GcMode: "g1gc",
                GameArgs: "--demo",
                JavaPath: "/opt/java/bin/java",
                WindowWidth: 1600,
                WindowHeight: 900,
                Fullscreen: true);
            var updated = await repository.UpdateAsync(
                created.Id,
                name: null,
                favorite: null,
                lastPlayedAt: null,
                overrides: overrides,
                setOverrides: true);
            Assert.NotNull(updated);
            Assert.Equal(overrides, updated!.Overrides);

            using (var document = await ReadInstanceJsonAsync(root))
            {
                var stored = document.RootElement.GetProperty("overrides");
                Assert.Equal(1024, stored.GetProperty("memoryMinMb").GetInt32());
                Assert.Equal(4096, stored.GetProperty("memoryMaxMb").GetInt32());
                Assert.Equal("-XX:+UseG1GC", stored.GetProperty("jvmArgs").GetString());
                Assert.Equal("g1gc", stored.GetProperty("gcMode").GetString());
                Assert.Equal("--demo", stored.GetProperty("gameArgs").GetString());
                Assert.Equal("/opt/java/bin/java", stored.GetProperty("javaPath").GetString());
                Assert.Equal(1600, stored.GetProperty("windowWidth").GetInt32());
                Assert.Equal(900, stored.GetProperty("windowHeight").GetInt32());
                Assert.True(stored.GetProperty("fullscreen").GetBoolean());
            }

            var reopened = new JsonInstanceRepository(dataRoot, new JsonAppSettingsStore(dataRoot));
            var listed = Assert.Single(await reopened.ListAsync());
            Assert.Equal(overrides, listed.Overrides);

            // A manual instance.json edit is authoritative; renaming must keep it.
            await RewriteInstanceJsonAsync(
                root,
                json => json.Replace(
                    "\"windowWidth\": 1600",
                    "\"windowWidth\": 1920",
                    StringComparison.Ordinal));
            var renamed = await reopened.UpdateAsync(created.Id, "Renamed", null, null);
            Assert.Equal(1920, renamed!.Overrides!.WindowWidth);

            // setOverrides replaces the whole object; an empty one clears it.
            var cleared = await reopened.UpdateAsync(
                created.Id,
                name: null,
                favorite: null,
                lastPlayedAt: null,
                overrides: new InstanceOverrides(),
                setOverrides: true);
            Assert.Null(cleared!.Overrides);

            using var finalDocument = await ReadInstanceJsonAsync(root);
            Assert.False(finalDocument.RootElement.TryGetProperty("overrides", out _));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Sqlite_list_prefers_overrides_from_instance_json()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "instance");
            var repository = new SqliteInstanceRepository(
                dataRoot,
                new JsonAppSettingsStore(dataRoot));
            var created = await repository.CreateAsync(
                "SQLite Overrides",
                "1.21.4",
                "fabric",
                "0.16.10",
                root);

            var updated = await repository.UpdateAsync(
                created.Id,
                name: null,
                favorite: null,
                lastPlayedAt: null,
                overrides: new InstanceOverrides(MemoryMaxMb: 4096, GcMode: "zgc"),
                setOverrides: true);
            Assert.Equal(4096, updated!.Overrides!.MemoryMaxMb);

            // A fresh repository instance reads the authoritative instance.json.
            var reopened = new SqliteInstanceRepository(
                dataRoot,
                new JsonAppSettingsStore(dataRoot));
            var listed = Assert.Single(await reopened.ListAsync());
            Assert.Equal("zgc", listed.Overrides!.GcMode);
            Assert.Equal(4096, listed.Overrides.MemoryMaxMb);

            // Renaming keeps the file-backed overrides (the SQLite row has none).
            var renamed = await reopened.UpdateAsync(created.Id, "Renamed", null, null);
            Assert.Equal("zgc", renamed!.Overrides!.GcMode);

            // instance.json wins over the index.
            await RewriteInstanceJsonAsync(
                root,
                json => json.Replace(
                    "\"gcMode\": \"zgc\"",
                    "\"gcMode\": \"shenandoah\"",
                    StringComparison.Ordinal));
            var afterEdit = Assert.Single(await reopened.ListAsync());
            Assert.Equal("shenandoah", afterEdit.Overrides!.GcMode);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Sqlite_sync_keeps_overrides_when_recounting_mods()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var repository = new SqliteInstanceRepository(
                dataRoot,
                new JsonAppSettingsStore(dataRoot));
            var created = await repository.CreateAsync(
                "Sync Overrides",
                "1.21.4",
                "fabric",
                "0.16.10",
                rootPath: null);
            await repository.UpdateAsync(
                created.Id,
                name: null,
                favorite: null,
                lastPlayedAt: null,
                overrides: new InstanceOverrides(MemoryMinMb: 2048),
                setOverrides: true);

            var mods = Path.Combine(created.RootPath, "mods");
            Directory.CreateDirectory(mods);
            await File.WriteAllTextAsync(Path.Combine(mods, "a.jar"), "a");

            var report = await repository.SyncAsync(null, dryRun: false);
            var item = Assert.Single(report.Items);
            Assert.Equal(1, item.ModCount);
            Assert.Equal(2048, item.Overrides!.MemoryMinMb);

            using var document = await ReadInstanceJsonAsync(created.RootPath);
            Assert.Equal(
                2048,
                document.RootElement.GetProperty("overrides").GetProperty("memoryMinMb").GetInt32());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Module_update_applies_set_clear_and_absent_semantics()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "instance");
            var settings = new JsonAppSettingsStore(dataRoot);
            var service = new InstanceModuleService(
                new SqliteInstanceRepository(dataRoot, settings),
                settings);
            var id = await CreateAsync(service, root, "Merge Patch");

            using (var set = await UpdateOverridesAsync(
                       service,
                       id,
                       """{"memoryMinMb":1024,"memoryMaxMb":4096,"gcMode":"g1gc"}"""))
            {
                var overrides = Instance(set);
                Assert.Equal(1024, overrides.GetProperty("memoryMinMb").GetInt32());
                Assert.Equal(4096, overrides.GetProperty("memoryMaxMb").GetInt32());
                Assert.Equal("g1gc", overrides.GetProperty("gcMode").GetString());
            }

            using (var clear = await UpdateOverridesAsync(
                       service,
                       id,
                       """{"memoryMinMb":null,"gameArgs":"--demo"}"""))
            {
                var overrides = Instance(clear);
                Assert.False(overrides.TryGetProperty("memoryMinMb", out _));
                Assert.Equal(4096, overrides.GetProperty("memoryMaxMb").GetInt32());
                Assert.Equal("g1gc", overrides.GetProperty("gcMode").GetString());
                Assert.Equal("--demo", overrides.GetProperty("gameArgs").GetString());
            }

            // A patch without the overrides key leaves them untouched.
            using (var rename = JsonDocument.Parse(
                       await service.HandleAsync(
                           "instance.update",
                           $$"""{"id":"{{id}}","name":"Renamed"}""",
                           CancellationToken.None)))
            {
                var overrides = Instance(rename);
                Assert.Equal(4096, overrides.GetProperty("memoryMaxMb").GetInt32());
                Assert.Equal("--demo", overrides.GetProperty("gameArgs").GetString());
            }

            // Clearing every key removes the stored object entirely.
            using (var reset = await UpdateOverridesAsync(
                       service,
                       id,
                       """
                       {"memoryMinMb":null,"memoryMaxMb":null,"jvmArgs":null,
                        "gcMode":null,"gameArgs":null,"javaPath":null,
                        "windowWidth":null,"windowHeight":null,"fullscreen":null}
                       """))
            {
                Assert.Empty(Instance(reset).EnumerateObject());
            }

            using var document = await ReadInstanceJsonAsync(root);
            Assert.False(document.RootElement.TryGetProperty("overrides", out _));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Module_list_and_create_always_include_overrides()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "instance");
            var settings = new JsonAppSettingsStore(dataRoot);
            var service = new InstanceModuleService(
                new SqliteInstanceRepository(dataRoot, settings),
                settings);

            using var create = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.create",
                    $$"""
                      {"name":"Listed","gameVersion":"1.21.4",
                       "loader":"vanilla","loaderVersion":"",
                       "rootPath":{{TestJson.Quote(root)}}}
                      """,
                    CancellationToken.None));
            var created = create.RootElement.GetProperty("data").GetProperty("instance");
            var id = created.GetProperty("id").GetString()!;
            Assert.Equal(JsonValueKind.Object, created.GetProperty("overrides").ValueKind);
            Assert.Empty(created.GetProperty("overrides").EnumerateObject());

            using (var list = JsonDocument.Parse(
                       await service.HandleAsync("instance.list", "{}", CancellationToken.None)))
            {
                var item = Assert.Single(
                    list.RootElement.GetProperty("data").GetProperty("items")
                        .EnumerateArray());
                Assert.Empty(item.GetProperty("overrides").EnumerateObject());
            }

            await UpdateOverridesAsync(service, id, """{"windowWidth":1600,"fullscreen":true}""");

            using var populated = JsonDocument.Parse(
                await service.HandleAsync("instance.list", "{}", CancellationToken.None));
            var populatedItem = Assert.Single(
                populated.RootElement.GetProperty("data").GetProperty("items")
                    .EnumerateArray());
            var overrides = populatedItem.GetProperty("overrides");
            Assert.Equal(1600, overrides.GetProperty("windowWidth").GetInt32());
            Assert.True(overrides.GetProperty("fullscreen").GetBoolean());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Theory]
    [InlineData("""{"memoryMinMb":127}""")]
    [InlineData("""{"memoryMinMb":65537}""")]
    [InlineData("""{"memoryMaxMb":0}""")]
    [InlineData("""{"windowWidth":639}""")]
    [InlineData("""{"windowWidth":7681}""")]
    [InlineData("""{"windowHeight":479}""")]
    [InlineData("""{"windowHeight":4321}""")]
    [InlineData("""{"gcMode":"cms"}""")]
    [InlineData("""{"javaPath":"relative/java"}""")]
    [InlineData("""{"memoryMinMb":4096,"memoryMaxMb":2048}""")]
    [InlineData("""{"memoryMinMb":"1024"}""")]
    [InlineData("""{"fullscreen":"yes"}""")]
    [InlineData("""{"windowWidth":800,"windowHeight":100}""")]
    public async Task Module_rejects_invalid_overrides(string patch)
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "instance");
            var settings = new JsonAppSettingsStore(dataRoot);
            var service = new InstanceModuleService(
                new SqliteInstanceRepository(dataRoot, settings),
                settings);
            var id = await CreateAsync(service, root, "Invalid Overrides");

            // Guard the accepted baseline so the theory only fails on the patch.
            using (var ok = await UpdateOverridesAsync(service, id, """{"windowWidth":800}"""))
            {
                Assert.True(ok.RootElement.GetProperty("ok").GetBoolean());
            }

            using var document = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.update",
                    $$"""{"id":"{{id}}","overrides":{{patch}}}""",
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
    public async Task Module_accepts_bounds_and_rejects_oversized_arguments()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "instance");
            var settings = new JsonAppSettingsStore(dataRoot);
            var service = new InstanceModuleService(
                new SqliteInstanceRepository(dataRoot, settings),
                settings);
            var id = await CreateAsync(service, root, "Bounds");

            using (var ok = JsonDocument.Parse(
                       await service.HandleAsync(
                           "instance.update",
                           JsonSerializer.Serialize(new
                           {
                               id,
                               overrides = new
                               {
                                   memoryMinMb = 128,
                                   memoryMaxMb = 65536,
                                   windowWidth = 640,
                                   windowHeight = 480,
                                   jvmArgs = new string('x', 8192),
                                   gameArgs = new string('y', 8192),
                               },
                           }),
                           CancellationToken.None)))
            {
                Assert.True(ok.RootElement.GetProperty("ok").GetBoolean());
                var overrides = Instance(ok);
                Assert.Equal(128, overrides.GetProperty("memoryMinMb").GetInt32());
                Assert.Equal(65536, overrides.GetProperty("memoryMaxMb").GetInt32());
            }

            using var tooLong = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.update",
                    JsonSerializer.Serialize(new
                    {
                        id,
                        overrides = new { jvmArgs = new string('x', 8193) },
                    }),
                    CancellationToken.None));
            Assert.False(tooLong.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "E_INVALID",
                tooLong.RootElement
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
    public async Task Module_rejects_overrides_of_the_wrong_shape()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "instance");
            var settings = new JsonAppSettingsStore(dataRoot);
            var service = new InstanceModuleService(
                new SqliteInstanceRepository(dataRoot, settings),
                settings);
            var id = await CreateAsync(service, root, "Bad Shape");

            foreach (var patch in new[] { "null", "[]", "\"text\"" })
            {
                using var document = JsonDocument.Parse(
                    await service.HandleAsync(
                        "instance.update",
                        $$"""{"id":"{{id}}","overrides":{{patch}}}""",
                        CancellationToken.None));
                Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
                Assert.Equal(
                    "E_INVALID",
                    document.RootElement
                        .GetProperty("error")
                        .GetProperty("code")
                        .GetString());
            }
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Module_rejects_an_update_without_any_field()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "instance");
            var settings = new JsonAppSettingsStore(dataRoot);
            var service = new InstanceModuleService(
                new SqliteInstanceRepository(dataRoot, settings),
                settings);
            var id = await CreateAsync(service, root, "Empty Update");

            using var document = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.update",
                    $$"""{"id":"{{id}}"}""",
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
    public async Task Reading_instance_json_ignores_unknown_and_empty_overrides()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "instance");
            Directory.CreateDirectory(Path.Combine(root, ".archoera"));
            await File.WriteAllTextAsync(
                Path.Combine(root, ".archoera", "instance.json"),
                """
                {
                  "schemaVersion": 2,
                  "id": "inst-manual",
                  "name": "Manual",
                  "gameVersion": "1.21.4",
                  "loader": "vanilla",
                  "loaderVersion": "",
                  "rootPath": "/stale/path",
                  "modCount": 0,
                  "favorite": false,
                  "createdAt": "2026-01-01T00:00:00Z",
                  "updatedAt": "2026-01-01T00:00:00Z",
                  "overrides": {
                    "windowHeight": 900,
                    "futureKey": {"nested": true},
                    "memoryMinMb": null
                  }
                }
                """);
            var settings = new JsonAppSettingsStore(dataRoot);
            await settings.SaveInstancesRootAsync(Path.GetDirectoryName(root)!);
            var repository = new SqliteInstanceRepository(dataRoot, settings);
            var report = await repository.SyncAsync(null, dryRun: false);
            var item = Assert.Single(report.Items);
            Assert.Equal(900, item.Overrides!.WindowHeight);
            Assert.Null(item.Overrides.MemoryMinMb);

            await RewriteInstanceJsonAsync(
                root,
                json => json.Replace(
                    "\"windowHeight\": 900",
                    "\"windowHeight\": null",
                    StringComparison.Ordinal));
            var cleared = Assert.Single(await repository.ListAsync());
            Assert.Null(cleared.Overrides);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Imported_overrides_ignore_invalid_java_path_and_memory_values()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "instance");
            Directory.CreateDirectory(Path.Combine(root, ".archoera"));
            var instanceJson = Path.Combine(root, ".archoera", "instance.json");
            await File.WriteAllTextAsync(
                instanceJson,
                """
                {
                  "schemaVersion": 2,
                  "id": "inst-imported",
                  "name": "Imported",
                  "gameVersion": "1.21.4",
                  "loader": "vanilla",
                  "loaderVersion": "",
                  "rootPath": "/stale/path",
                  "modCount": 0,
                  "favorite": false,
                  "createdAt": "2026-01-01T00:00:00Z",
                  "updatedAt": "2026-01-01T00:00:00Z",
                  "overrides": {
                    "javaPath": "relative/java",
                    "memoryMinMb": 64,
                    "memoryMaxMb": 999999
                  }
                }
                """);

            var settings = new JsonAppSettingsStore(dataRoot);
            await settings.SaveInstancesRootAsync(Path.GetDirectoryName(root)!);
            var repository = new SqliteInstanceRepository(dataRoot, settings);
            await repository.SyncAsync(null, dryRun: false);
            var invalid = Assert.Single(await repository.ListAsync());
            Assert.Null(invalid.Overrides);

            var java = Path.Combine(directory, "runtime", "bin", "java");
            Directory.CreateDirectory(Path.GetDirectoryName(java)!);
            await File.WriteAllTextAsync(java, "#!/bin/sh\n");
            await File.WriteAllTextAsync(
                instanceJson,
                $$"""
                {
                  "schemaVersion": 2,
                  "id": "inst-imported",
                  "name": "Imported",
                  "gameVersion": "1.21.4",
                  "loader": "vanilla",
                  "loaderVersion": "",
                  "rootPath": "/stale/path",
                  "modCount": 0,
                  "favorite": false,
                  "createdAt": "2026-01-01T00:00:00Z",
                  "updatedAt": "2026-01-01T00:00:00Z",
                  "overrides": {
                    "javaPath": "{{java.Replace("\\", "\\\\", StringComparison.Ordinal)}}",
                    "memoryMinMb": 512,
                    "memoryMaxMb": 4096
                  }
                }
                """);

            var valid = Assert.Single(await repository.ListAsync());
            Assert.Equal(Path.GetFullPath(java), valid.Overrides!.JavaPath);
            Assert.Equal(512, valid.Overrides.MemoryMinMb);
            Assert.Equal(4096, valid.Overrides.MemoryMaxMb);
        }
        finally
        {
            Delete(directory);
        }
    }

    private static JsonElement Instance(JsonDocument document)
        => document.RootElement
            .GetProperty("data")
            .GetProperty("instance")
            .GetProperty("overrides");

    private static async Task<JsonDocument> UpdateOverridesAsync(
        InstanceModuleService service,
        string id,
        string overridesJson)
        => JsonDocument.Parse(
            await service.HandleAsync(
                "instance.update",
                $$"""{"id":"{{id}}","overrides":{{overridesJson}}}""",
                CancellationToken.None));

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

    private static async Task<JsonDocument> ReadInstanceJsonAsync(string root)
        => JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(root, ".archoera", "instance.json")));

    private static async Task RewriteInstanceJsonAsync(
        string root,
        Func<string, string> rewrite)
    {
        var path = Path.Combine(root, ".archoera", "instance.json");
        var json = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, rewrite(json));
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-instance-overrides",
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
