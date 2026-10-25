using System.Text.Json;
using Archoera.Domain.Instances;
using Archoera.Domain.Settings;
using Archoera.Infrastructure.Instances;
using Archoera.Infrastructure.Settings;

namespace Archoera.Tests;

/// <summary>
/// Storage and validation for the schema-15 window/performance switches:
/// global settings, per-instance overrides, the fullscreen/maximized
/// exclusivity rule and the developer-only native-library paths.
/// </summary>
public sealed class LaunchPerformanceSettingsTests
{
    [Fact]
    public async Task Settings_round_trip_performance_and_native_overrides()
    {
        var directory = TempDirectory();
        try
        {
            var store = new JsonAppSettingsStore(Path.Combine(directory, "data"));
            var service = new SettingsModuleService(store);
            var glfw = Path.Combine(directory, "glfw.so");
            var openAl = Path.Combine(directory, "openal.so");
            var sdl = Path.Combine(directory, "libSDL2.so");

            using (var set = JsonDocument.Parse(
                       await service.HandleAsync(
                           "settings.setRuntimeOptions",
                           JsonSerializer.Serialize(new
                           {
                               maximized = true,
                               fullscreen = false,
                               enableGameMode = true,
                               enableMangoHud = true,
                               useZink = true,
                               developerMode = true,
                               nativeGlfwPath = glfw,
                               nativeOpenALPath = openAl,
                               nativeSdlPath = sdl,
                           }),
                           CancellationToken.None)))
            {
                Assert.True(set.RootElement.GetProperty("ok").GetBoolean());
                var settings = Settings(set);
                Assert.True(settings.GetProperty("maximized").GetBoolean());
                Assert.False(settings.GetProperty("fullscreen").GetBoolean());
                Assert.True(settings.GetProperty("enableGameMode").GetBoolean());
                Assert.True(settings.GetProperty("enableMangoHud").GetBoolean());
                Assert.True(settings.GetProperty("useZink").GetBoolean());
                Assert.True(settings.GetProperty("developerMode").GetBoolean());
                Assert.Equal(glfw, settings.GetProperty("nativeGlfwPath").GetString());
                Assert.Equal(openAl, settings.GetProperty("nativeOpenALPath").GetString());
                Assert.Equal(sdl, settings.GetProperty("nativeSdlPath").GetString());
                Assert.Equal(
                    AppSettings.CurrentSchemaVersion,
                    set.RootElement
                        .GetProperty("meta")
                        .GetProperty("schemaVersion")
                        .GetInt32());
            }

            var reloaded = await store.LoadAsync();
            Assert.True(reloaded.Maximized);
            Assert.False(reloaded.Fullscreen);
            Assert.True(reloaded.EnableGameMode);
            Assert.True(reloaded.EnableMangoHud);
            Assert.True(reloaded.UseZink);
            Assert.True(reloaded.DeveloperMode);
            Assert.Equal(glfw, reloaded.NativeGlfwPath);
            Assert.Equal(openAl, reloaded.NativeOpenALPath);
            Assert.Equal(sdl, reloaded.NativeSdlPath);

            // JSON null clears a native path; the other flags stay untouched.
            using (var clear = JsonDocument.Parse(
                       await service.HandleAsync(
                           "settings.setRuntimeOptions",
                           """{"nativeGlfwPath":null}""",
                           CancellationToken.None)))
            {
                Assert.True(clear.RootElement.GetProperty("ok").GetBoolean());
                Assert.Null(
                    Settings(clear).GetProperty("nativeGlfwPath").GetString());
            }

            Assert.Null((await store.LoadAsync()).NativeGlfwPath);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Schema_14_settings_load_defaults_and_save_current_schema()
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
                  {"schemaVersion":14,"instancesRoot":{{TestJson.Quote(games)}},
                   "windowWidth":1280,"windowHeight":720,"fullscreen":true}
                  """);

            var store = new JsonAppSettingsStore(dataRoot);
            var loaded = await store.LoadAsync();
            Assert.False(loaded.Maximized);
            Assert.False(loaded.EnableGameMode);
            Assert.False(loaded.EnableMangoHud);
            Assert.False(loaded.UseZink);
            Assert.False(loaded.DeveloperMode);
            Assert.Null(loaded.NativeGlfwPath);
            Assert.Null(loaded.NativeOpenALPath);
            Assert.Null(loaded.NativeSdlPath);
            Assert.True(loaded.Fullscreen);
            Assert.Equal(1280, loaded.WindowWidth);

            var saved = await store.SaveAsync(loaded);
            using var document = JsonDocument.Parse(
                await File.ReadAllTextAsync(saved.SettingsPath));
            var root = document.RootElement;
            Assert.Equal(
                AppSettings.CurrentSchemaVersion,
                root.GetProperty("schemaVersion").GetInt32());
            Assert.False(root.GetProperty("maximized").GetBoolean());
            Assert.False(root.GetProperty("enableGameMode").GetBoolean());
            Assert.False(root.GetProperty("enableMangoHud").GetBoolean());
            Assert.False(root.GetProperty("useZink").GetBoolean());
            Assert.False(root.GetProperty("developerMode").GetBoolean());
            Assert.False(root.TryGetProperty("nativeGlfwPath", out _));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Invalid_saved_native_paths_are_ignored_on_load()
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
                  {"schemaVersion":15,"instancesRoot":{{TestJson.Quote(games)}},
                   "developerMode":true,
                   "nativeGlfwPath":"relative/glfw.so",
                   "nativeOpenALPath":"line\nbreak",
                   "nativeSdlPath":42}
                  """);

            var loaded = await new JsonAppSettingsStore(dataRoot).LoadAsync();

            Assert.True(loaded.DeveloperMode);
            Assert.Null(loaded.NativeGlfwPath);
            Assert.Null(loaded.NativeOpenALPath);
            Assert.Null(loaded.NativeSdlPath);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Settings_reject_fullscreen_and_maximized_together()
    {
        var directory = TempDirectory();
        try
        {
            var service = new SettingsModuleService(
                new JsonAppSettingsStore(Path.Combine(directory, "data")));

            using (var fullscreen = JsonDocument.Parse(
                       await service.HandleAsync(
                           "settings.set",
                           """{"fullscreen":true}""",
                           CancellationToken.None)))
            {
                Assert.True(fullscreen.RootElement.GetProperty("ok").GetBoolean());
            }

            // Merging maximized onto the stored fullscreen document fails even
            // though this patch does not mention fullscreen.
            using (var conflict = JsonDocument.Parse(
                       await service.HandleAsync(
                           "settings.set",
                           """{"maximized":true}""",
                           CancellationToken.None)))
            {
                AssertError(conflict, "E_INVALID");
            }

            // A single patch that sets both is rejected as well.
            using (var both = JsonDocument.Parse(
                       await service.HandleAsync(
                           "settings.set",
                           """{"fullscreen":true,"maximized":true}""",
                           CancellationToken.None)))
            {
                AssertError(both, "E_INVALID");
            }

            // Turning fullscreen off and maximizing in one patch is valid.
            using (var switchMode = JsonDocument.Parse(
                       await service.HandleAsync(
                           "settings.set",
                           """{"fullscreen":false,"maximized":true}""",
                           CancellationToken.None)))
            {
                Assert.True(switchMode.RootElement.GetProperty("ok").GetBoolean());
                Assert.True(Settings(switchMode).GetProperty("maximized").GetBoolean());
            }
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Settings_reject_relative_native_library_paths()
    {
        var directory = TempDirectory();
        try
        {
            var service = new SettingsModuleService(
                new JsonAppSettingsStore(Path.Combine(directory, "data")));

            foreach (var field in new[]
                     {
                         "nativeGlfwPath", "nativeOpenALPath", "nativeSdlPath",
                     })
            {
                using var document = JsonDocument.Parse(
                    await service.HandleAsync(
                        "settings.set",
                        $$"""{"{{field}}":"relative/lib.so"}""",
                        CancellationToken.None));
                AssertError(document, "E_INVALID");
            }
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Instance_overrides_round_trip_performance_switches()
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
            var id = await CreateAsync(service, root, "Performance Overrides");

            using (var patch = JsonDocument.Parse(
                       await service.HandleAsync(
                           "instance.update",
                           JsonSerializer.Serialize(new
                           {
                               id,
                               overrides = new
                               {
                                   maximized = true,
                                   enableGameMode = true,
                                   enableMangoHud = false,
                                   useZink = true,
                               },
                           }),
                           CancellationToken.None)))
            {
                Assert.True(patch.RootElement.GetProperty("ok").GetBoolean());
                var overrides = Overrides(patch);
                Assert.True(overrides.GetProperty("maximized").GetBoolean());
                Assert.True(overrides.GetProperty("enableGameMode").GetBoolean());
                Assert.False(overrides.GetProperty("enableMangoHud").GetBoolean());
                Assert.True(overrides.GetProperty("useZink").GetBoolean());
            }

            var repository = new SqliteInstanceRepository(dataRoot, settings);
            var listed = Assert.Single(await repository.ListAsync());
            Assert.True(listed.Overrides!.Maximized);
            Assert.True(listed.Overrides.EnableGameMode);
            Assert.False(listed.Overrides.EnableMangoHud);
            Assert.True(listed.Overrides.UseZink);

            using (var document = JsonDocument.Parse(await File.ReadAllTextAsync(
                       Path.Combine(root, ".archoera", "instance.json"))))
            {
                var stored = document.RootElement.GetProperty("overrides");
                Assert.True(stored.GetProperty("maximized").GetBoolean());
                Assert.True(stored.GetProperty("enableGameMode").GetBoolean());
                Assert.False(stored.GetProperty("enableMangoHud").GetBoolean());
                Assert.True(stored.GetProperty("useZink").GetBoolean());
            }

            // Explicit null inherits the global value again.
            using var cleared = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.update",
                    """
                    {"id":"__ID__","overrides":
                     {"maximized":null,"enableGameMode":null,
                      "enableMangoHud":null,"useZink":null}}
                    """.Replace("__ID__", id, StringComparison.Ordinal),
                    CancellationToken.None));
            Assert.Empty(Overrides(cleared).EnumerateObject());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Instance_overrides_reject_fullscreen_and_maximized_together()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var settings = new JsonAppSettingsStore(dataRoot);
            var service = new InstanceModuleService(
                new SqliteInstanceRepository(dataRoot, settings),
                settings);
            var id = await CreateAsync(
                service,
                Path.Combine(directory, "instance"),
                "Window Modes");

            using (var fullscreen = await UpdateAsync(
                       service,
                       id,
                       """{"fullscreen":true}"""))
            {
                Assert.True(fullscreen.RootElement.GetProperty("ok").GetBoolean());
            }

            // The stored fullscreen value conflicts with this patch.
            using (var maximized = await UpdateAsync(
                       service,
                       id,
                       """{"maximized":true}"""))
            {
                AssertError(maximized, "E_INVALID");
            }

            using (var both = await UpdateAsync(
                       service,
                       id,
                       """{"fullscreen":true,"maximized":true}"""))
            {
                AssertError(both, "E_INVALID");
            }

            using (var switchMode = await UpdateAsync(
                       service,
                       id,
                       """{"fullscreen":false,"maximized":true}"""))
            {
                Assert.True(switchMode.RootElement.GetProperty("ok").GetBoolean());
                Assert.True(Overrides(switchMode).GetProperty("maximized").GetBoolean());
            }
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void ApplyTo_folds_performance_switches_and_keeps_developer_mode_global()
    {
        var global = new AppSettings(
            InstancesRoot: Path.GetTempPath(),
            DataRoot: Path.GetTempPath(),
            SettingsPath: string.Empty,
            Maximized: false,
            EnableGameMode: true,
            EnableMangoHud: false,
            UseZink: true,
            DeveloperMode: true,
            NativeGlfwPath: "/opt/glfw/libglfw.so");

        var effective = new InstanceOverrides(
            Maximized: true,
            EnableGameMode: false,
            EnableMangoHud: true,
            UseZink: null).ApplyTo(global);

        Assert.True(effective.Maximized);
        Assert.False(effective.EnableGameMode);
        Assert.True(effective.EnableMangoHud);
        Assert.True(effective.UseZink);
        // Developer-only native overrides are not per-instance fields.
        Assert.True(effective.DeveloperMode);
        Assert.Equal("/opt/glfw/libglfw.so", effective.NativeGlfwPath);
    }

    private static JsonElement Settings(JsonDocument document)
        => document.RootElement
            .GetProperty("data")
            .GetProperty("settings");

    private static JsonElement Overrides(JsonDocument document)
        => document.RootElement
            .GetProperty("data")
            .GetProperty("instance")
            .GetProperty("overrides");

    private static void AssertError(JsonDocument document, string code)
    {
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(
            code,
            document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private static async Task<JsonDocument> UpdateAsync(
        InstanceModuleService service,
        string id,
        string overridesJson)
        => JsonDocument.Parse(
            await service.HandleAsync(
                "instance.update",
                string.Concat(
                    "{\"id\":",
                    JsonSerializer.Serialize(id),
                    ",\"overrides\":",
                    overridesJson,
                    "}"),
                CancellationToken.None));

    private static async Task<string> CreateAsync(
        InstanceModuleService service,
        string rootPath,
        string name)
    {
        using var document = JsonDocument.Parse(
            await service.HandleAsync(
                "instance.create",
                JsonSerializer.Serialize(new
                {
                    name,
                    gameVersion = "1.21.4",
                    loader = "vanilla",
                    loaderVersion = string.Empty,
                    rootPath,
                }),
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
            "archoera-launch-performance-settings",
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
