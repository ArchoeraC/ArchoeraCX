using System.Text.Json;
using Archoera.Domain.Instances;
using Archoera.Domain.Settings;
using Archoera.Infrastructure.Instances;
using Archoera.Infrastructure.Settings;

namespace Archoera.Tests;

/// <summary>
/// Settings and instance-override storage for the pre-load, pre-launch and
/// post-exit hooks: exact JSON shape, inherit-versus-disable semantics and the
/// strict parsing that protects the first launch.
/// </summary>
public sealed class LaunchCommandSettingsTests
{
    [Fact]
    public async Task Settings_round_trip_all_three_commands()
    {
        var directory = TempDirectory();
        try
        {
            var store = new JsonAppSettingsStore(Path.Combine(directory, "data"));
            var service = new SettingsModuleService(store);

            using (var set = JsonDocument.Parse(
                       await service.HandleAsync(
                           "settings.setRuntimeOptions",
                           """
                           {"preLoadCommand":"/usr/bin/hook --phase load",
                            "preLaunchCommand":"/usr/bin/hook --phase launch \"two words\"",
                            "postExitCommand":"/usr/bin/hook --phase exit"}
                           """,
                           CancellationToken.None)))
            {
                Assert.True(set.RootElement.GetProperty("ok").GetBoolean());
                var settings = Settings(set);
                Assert.Equal(
                    "/usr/bin/hook --phase load",
                    settings.GetProperty("preLoadCommand").GetString());
                Assert.Equal(
                    "/usr/bin/hook --phase launch \"two words\"",
                    settings.GetProperty("preLaunchCommand").GetString());
                Assert.Equal(
                    "/usr/bin/hook --phase exit",
                    settings.GetProperty("postExitCommand").GetString());
            }

            var reloaded = await store.LoadAsync();
            Assert.Equal("/usr/bin/hook --phase load", reloaded.PreLoadCommand);
            Assert.Equal(
                "/usr/bin/hook --phase launch \"two words\"",
                reloaded.PreLaunchCommand);
            Assert.Equal("/usr/bin/hook --phase exit", reloaded.PostExitCommand);

            using var document = JsonDocument.Parse(
                await File.ReadAllTextAsync(store.SettingsPathValue));
            var root = document.RootElement;
            Assert.Equal(
                AppSettings.CurrentSchemaVersion,
                root.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(
                "/usr/bin/hook --phase load",
                root.GetProperty("preLoadCommand").GetString());
            Assert.Equal(
                "/usr/bin/hook --phase launch \"two words\"",
                root.GetProperty("preLaunchCommand").GetString());
            Assert.Equal(
                "/usr/bin/hook --phase exit",
                root.GetProperty("postExitCommand").GetString());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Settings_null_and_whitespace_clear_the_commands()
    {
        var directory = TempDirectory();
        try
        {
            var store = new JsonAppSettingsStore(Path.Combine(directory, "data"));
            var service = new SettingsModuleService(store);

            using (var set = JsonDocument.Parse(
                       await service.HandleAsync(
                           "settings.setRuntimeOptions",
                           """
                           {"preLoadCommand":"/bin/a",
                            "preLaunchCommand":"/bin/b",
                            "postExitCommand":"/bin/c"}
                           """,
                           CancellationToken.None)))
            {
                Assert.True(set.RootElement.GetProperty("ok").GetBoolean());
            }

            using (var clear = JsonDocument.Parse(
                       await service.HandleAsync(
                           "settings.setRuntimeOptions",
                           """{"preLoadCommand":null,"preLaunchCommand":"   ","postExitCommand":null}""",
                           CancellationToken.None)))
            {
                var settings = Settings(clear);
                Assert.Equal(
                    JsonValueKind.Null,
                    settings.GetProperty("preLoadCommand").ValueKind);
                Assert.Equal(
                    JsonValueKind.Null,
                    settings.GetProperty("preLaunchCommand").ValueKind);
                Assert.Equal(
                    JsonValueKind.Null,
                    settings.GetProperty("postExitCommand").ValueKind);
            }

            var reloaded = await store.LoadAsync();
            Assert.Null(reloaded.PreLoadCommand);
            Assert.Null(reloaded.PreLaunchCommand);
            Assert.Null(reloaded.PostExitCommand);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Theory]
    [InlineData("""{"preLoadCommand":"\"unclosed"}""")]
    [InlineData("""{"preLaunchCommand":"'unclosed"}""")]
    [InlineData("""{"postExitCommand":"line\nbreak"}""")]
    [InlineData("""{"preLoadCommand":"\"\""}""")]
    [InlineData("""{"preLaunchCommand":42}""")]
    public async Task Invalid_settings_commands_are_rejected(string payload)
    {
        var directory = TempDirectory();
        try
        {
            var service = new SettingsModuleService(
                new JsonAppSettingsStore(Path.Combine(directory, "data")));
            using var document = JsonDocument.Parse(
                await service.HandleAsync(
                    "settings.setRuntimeOptions",
                    payload,
                    CancellationToken.None));
            Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "E_INVALID",
                document.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Settings_reject_commands_longer_than_4096_characters()
    {
        var directory = TempDirectory();
        try
        {
            var service = new SettingsModuleService(
                new JsonAppSettingsStore(Path.Combine(directory, "data")));

            using (var tooLong = JsonDocument.Parse(
                       await service.HandleAsync(
                           "settings.setRuntimeOptions",
                           JsonSerializer.Serialize(new
                           {
                               preLoadCommand = new string('x', 4097),
                           }),
                           CancellationToken.None)))
            {
                Assert.False(tooLong.RootElement.GetProperty("ok").GetBoolean());
            }

            using var accepted = JsonDocument.Parse(
                await service.HandleAsync(
                    "settings.setRuntimeOptions",
                    JsonSerializer.Serialize(new
                    {
                        preLoadCommand = new string('x', 4096),
                    }),
                    CancellationToken.None));
            Assert.True(accepted.RootElement.GetProperty("ok").GetBoolean());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Schema_13_document_loads_without_commands_and_saves_current_schema()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var games = Path.Combine(directory, "games");
            Directory.CreateDirectory(dataRoot);
            Directory.CreateDirectory(games);
            await File.WriteAllTextAsync(
                Path.Combine(dataRoot, "settings.json"),
                $$"""
                  {"schemaVersion":13,"instancesRoot":{{TestJson.Quote(games)}},"jvmArgs":"-Xmx2G"}
                  """);

            var store = new JsonAppSettingsStore(dataRoot);
            var loaded = await store.LoadAsync();
            Assert.Null(loaded.PreLoadCommand);
            Assert.Null(loaded.PreLaunchCommand);
            Assert.Null(loaded.PostExitCommand);

            await store.SaveAsync(loaded);
            using var document = JsonDocument.Parse(
                await File.ReadAllTextAsync(store.SettingsPathValue));
            Assert.Equal(
                AppSettings.CurrentSchemaVersion,
                document.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.False(document.RootElement.TryGetProperty("preLoadCommand", out _));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Invalid_hand_edited_settings_commands_are_dropped()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var games = Path.Combine(directory, "games");
            Directory.CreateDirectory(dataRoot);
            Directory.CreateDirectory(games);
            await File.WriteAllTextAsync(
                Path.Combine(dataRoot, "settings.json"),
                $$"""
                  {"schemaVersion":14,
                   "instancesRoot":{{TestJson.Quote(games)}},
                   "preLoadCommand":"\"unclosed",
                   "preLaunchCommand":"/bin/ok",
                   "postExitCommand":"line\nbreak"}
                  """);

            var loaded = await new JsonAppSettingsStore(dataRoot).LoadAsync();
            Assert.Null(loaded.PreLoadCommand);
            Assert.Equal("/bin/ok", loaded.PreLaunchCommand);
            Assert.Null(loaded.PostExitCommand);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Instance_overrides_round_trip_all_three_commands()
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
            var id = await CreateAsync(service, root, "Command Overrides");

            using (var update = await UpdateOverridesAsync(
                       service,
                       id,
                       """
                       {"preLoadCommand":"/bin/load",
                        "preLaunchCommand":"/bin/launch \"two words\"",
                        "postExitCommand":"/bin/exit"}
                       """))
            {
                var overrides = Instance(update);
                Assert.Equal(
                    "/bin/load",
                    overrides.GetProperty("preLoadCommand").GetString());
                Assert.Equal(
                    "/bin/launch \"two words\"",
                    overrides.GetProperty("preLaunchCommand").GetString());
                Assert.Equal(
                    "/bin/exit",
                    overrides.GetProperty("postExitCommand").GetString());
            }

            var repository = new SqliteInstanceRepository(
                dataRoot,
                new JsonAppSettingsStore(dataRoot));
            var listed = Assert.Single(await repository.ListAsync());
            Assert.Equal("/bin/load", listed.Overrides!.PreLoadCommand);
            Assert.Equal(
                "/bin/launch \"two words\"",
                listed.Overrides.PreLaunchCommand);
            Assert.Equal("/bin/exit", listed.Overrides.PostExitCommand);

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(root, ".archoera", "instance.json")));
            var stored = document.RootElement.GetProperty("overrides");
            Assert.Equal("/bin/load", stored.GetProperty("preLoadCommand").GetString());
            Assert.Equal(
                "/bin/launch \"two words\"",
                stored.GetProperty("preLaunchCommand").GetString());
            Assert.Equal("/bin/exit", stored.GetProperty("postExitCommand").GetString());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Empty_instance_command_disables_the_global_one_and_null_inherits()
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
            var id = await CreateAsync(service, root, "Inherit Commands");

            using (var update = await UpdateOverridesAsync(
                       service,
                       id,
                       """
                       {"preLoadCommand":"/bin/load",
                        "preLaunchCommand":"",
                        "postExitCommand":"   "}
                       """))
            {
                var overrides = Instance(update);
                Assert.Equal(
                    "/bin/load",
                    overrides.GetProperty("preLoadCommand").GetString());
                Assert.Equal(
                    string.Empty,
                    overrides.GetProperty("preLaunchCommand").GetString());
                Assert.Equal(
                    string.Empty,
                    overrides.GetProperty("postExitCommand").GetString());
            }

            using (var clear = await UpdateOverridesAsync(
                       service,
                       id,
                       """{"preLoadCommand":null,"preLaunchCommand":null,"postExitCommand":null}"""))
            {
                Assert.Empty(Instance(clear).EnumerateObject());
            }
        }
        finally
        {
            Delete(directory);
        }
    }

    [Theory]
    [InlineData("""{"preLoadCommand":"\"unclosed"}""")]
    [InlineData("""{"preLaunchCommand":"line\nbreak"}""")]
    [InlineData("""{"postExitCommand":"\"\""}""")]
    [InlineData("""{"preLaunchCommand":7}""")]
    public async Task Invalid_instance_commands_are_rejected(string patch)
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
            var id = await CreateAsync(service, root, "Invalid Commands");

            using var document = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.update",
                    $$"""{"id":"{{id}}","overrides":{{patch}}}""",
                    CancellationToken.None));
            Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "E_INVALID",
                document.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Instance_rejects_commands_longer_than_4096_characters()
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
            var id = await CreateAsync(service, root, "Long Commands");

            using var document = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.update",
                    JsonSerializer.Serialize(new
                    {
                        id,
                        overrides = new { postExitCommand = new string('z', 4097) },
                    }),
                    CancellationToken.None));
            Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "E_INVALID",
                document.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Command_overrides_resolve_inherit_clear_and_value()
    {
        var global = new AppSettings(
            InstancesRoot: Path.GetTempPath(),
            DataRoot: Path.GetTempPath(),
            SettingsPath: string.Empty,
            PreLoadCommand: "global-load",
            PreLaunchCommand: "global-launch",
            PostExitCommand: "global-exit");

        var inherited = new InstanceOverrides().ApplyTo(global);
        Assert.Equal("global-load", inherited.PreLoadCommand);
        Assert.Equal("global-launch", inherited.PreLaunchCommand);
        Assert.Equal("global-exit", inherited.PostExitCommand);

        var cleared = new InstanceOverrides(
            PreLoadCommand: string.Empty,
            PreLaunchCommand: " ",
            PostExitCommand: string.Empty).ApplyTo(global);
        Assert.Null(cleared.PreLoadCommand);
        Assert.Null(cleared.PreLaunchCommand);
        Assert.Null(cleared.PostExitCommand);

        var replaced = new InstanceOverrides(
            PreLoadCommand: "instance-load",
            PreLaunchCommand: "instance-launch",
            PostExitCommand: "instance-exit").ApplyTo(global);
        Assert.Equal("instance-load", replaced.PreLoadCommand);
        Assert.Equal("instance-launch", replaced.PreLaunchCommand);
        Assert.Equal("instance-exit", replaced.PostExitCommand);
        Assert.Equal("global-load", global.PreLoadCommand);
    }

    [Fact]
    public async Task Imported_instance_commands_keep_empty_and_drop_invalid()
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
                  "id": "inst-commands",
                  "name": "Imported Commands",
                  "gameVersion": "1.21.4",
                  "loader": "vanilla",
                  "loaderVersion": "",
                  "rootPath": "/stale/path",
                  "modCount": 0,
                  "favorite": false,
                  "createdAt": "2026-01-01T00:00:00Z",
                  "updatedAt": "2026-01-01T00:00:00Z",
                  "overrides": {
                    "preLoadCommand": "/bin/imported",
                    "preLaunchCommand": "\"unclosed",
                    "postExitCommand": ""
                  }
                }
                """);

            var settings = new JsonAppSettingsStore(dataRoot);
            await settings.SaveInstancesRootAsync(Path.GetDirectoryName(root)!);
            var repository = new SqliteInstanceRepository(dataRoot, settings);
            await repository.SyncAsync(null, dryRun: false);
            var item = Assert.Single(await repository.ListAsync());

            Assert.Equal("/bin/imported", item.Overrides!.PreLoadCommand);
            Assert.Null(item.Overrides.PreLaunchCommand);
            Assert.Equal(string.Empty, item.Overrides.PostExitCommand);
        }
        finally
        {
            Delete(directory);
        }
    }

    private static JsonElement Settings(JsonDocument document)
        => document.RootElement
            .GetProperty("data")
            .GetProperty("settings");

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

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-launch-command-settings",
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
