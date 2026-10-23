using System.Text.Json;
using Archoera.Domain.Settings;
using Archoera.Infrastructure.Settings;

namespace Archoera.Tests;

public sealed class SettingsTests
{
    [Fact]
    public async Task Store_returns_defaults_then_persists_an_absolute_instances_root()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var instancesRoot = Path.Combine(directory, "games");
            var store = new JsonAppSettingsStore(dataRoot);

            var defaults = await store.LoadAsync();
            Assert.Equal(Path.Combine(dataRoot, "instances"), defaults.InstancesRoot);
            Assert.Equal(dataRoot, defaults.DataRoot);
            Assert.False(File.Exists(defaults.SettingsPath));

            var saved = await store.SaveInstancesRootAsync(instancesRoot);
            Assert.Equal(Path.GetFullPath(instancesRoot), saved.InstancesRoot);
            Assert.True(Directory.Exists(instancesRoot));
            Assert.True(File.Exists(saved.SettingsPath));

            var reloaded = await store.LoadAsync();
            Assert.Equal(saved.InstancesRoot, reloaded.InstancesRoot);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Store_persists_an_ordered_root_list_and_legacy_alias()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var first = Path.Combine(directory, "first");
            var second = Path.Combine(directory, "second");
            var store = new JsonAppSettingsStore(dataRoot);

            var saved = await store.SaveInstancesRootsAsync([first, second]);
            Assert.Equal([Path.GetFullPath(first), Path.GetFullPath(second)], saved.Roots);
            Assert.Equal(saved.Roots[0], saved.InstancesRoot);
            Assert.True(Directory.Exists(first));
            Assert.True(Directory.Exists(second));

            var reloaded = await store.LoadAsync();
            Assert.Equal(saved.Roots, reloaded.Roots);
            Assert.Equal(saved.Roots[0], reloaded.InstancesRoot);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Settings_module_returns_and_updates_multiple_roots()
    {
        var directory = TempDirectory();
        try
        {
            var service = new SettingsModuleService(
                new JsonAppSettingsStore(Path.Combine(directory, "data")));
            var first = Path.Combine(directory, "first");
            var second = Path.Combine(directory, "second");

            var payload = $$"""{"instancesRoots":["{{first}}","{{second}}"]}""";
            using var document = JsonDocument.Parse(
                await service.HandleAsync("settings.set", payload, CancellationToken.None));
            var settings = document.RootElement
                .GetProperty("data")
                .GetProperty("settings");
            var roots = settings.GetProperty("instancesRoots")
                .EnumerateArray()
                .Select(item => item.GetString() ?? string.Empty)
                .ToArray();

            Assert.Equal([Path.GetFullPath(first), Path.GetFullPath(second)], roots);
            Assert.Equal(roots[0], settings.GetProperty("instancesRoot").GetString());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Settings_module_round_trips_download_preferences()
    {
        var directory = TempDirectory();
        try
        {
            var store = new JsonAppSettingsStore(Path.Combine(directory, "data"));
            var service = new SettingsModuleService(store);

            using var document = JsonDocument.Parse(
                await service.HandleAsync(
                    "settings.set",
                    """{"downloadSource":"mirror","downloadConcurrency":24,"downloadPerHost":4}""",
                    CancellationToken.None));
            var settings = document.RootElement
                .GetProperty("data")
                .GetProperty("settings");
            Assert.Equal("mirror", settings.GetProperty("downloadSource").GetString());
            Assert.Equal(24, settings.GetProperty("downloadConcurrency").GetInt32());
            Assert.Equal(4, settings.GetProperty("downloadPerHost").GetInt32());

            var reloaded = await store.LoadAsync();
            Assert.Equal("mirror", reloaded.DownloadSource);
            Assert.Equal(24, reloaded.DownloadConcurrency);
            Assert.Equal(4, reloaded.DownloadPerHost);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Settings_module_rejects_invalid_download_values()
    {
        var directory = TempDirectory();
        try
        {
            var service = new SettingsModuleService(
                new JsonAppSettingsStore(Path.Combine(directory, "data")));

            foreach (var payload in new[]
                     {
                         """{"downloadSource":"fastest"}""",
                         """{"downloadConcurrency":0}""",
                         """{"downloadConcurrency":65}""",
                         """{"downloadPerHost":-1}""",
                         """{"downloadPerHost":65}""",
                     })
            {
                using var document = JsonDocument.Parse(
                    await service.HandleAsync(
                        "settings.set",
                        payload,
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
    public async Task Legacy_per_host_default_is_raised_on_load()
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
                  {"schemaVersion":6,"instancesRoot":"{{games}}",
                   "downloadSource":"auto","downloadConcurrency":16,"downloadPerHost":6}
                  """);

            var loaded = await new JsonAppSettingsStore(dataRoot).LoadAsync();

            Assert.Equal(16, loaded.DownloadPerHost);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Explicit_per_host_choice_survives_the_schema_bump()
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
                  {"schemaVersion":7,"instancesRoot":"{{games}}",
                   "downloadSource":"auto","downloadConcurrency":16,"downloadPerHost":6}
                  """);

            var loaded = await new JsonAppSettingsStore(dataRoot).LoadAsync();

            Assert.Equal(6, loaded.DownloadPerHost);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Download_proxy_round_trips_and_rejects_bad_urls()
    {
        var directory = TempDirectory();
        try
        {
            var root = Path.Combine(directory, "data");
            var store = new JsonAppSettingsStore(root);
            var service = new SettingsModuleService(store);

            using (var document = JsonDocument.Parse(
                       await service.HandleAsync(
                           "settings.set",
                           """{"downloadProxy":"http://127.0.0.1:7890"}""",
                           CancellationToken.None)))
            {
                Assert.Equal(
                    "http://127.0.0.1:7890",
                    document.RootElement
                        .GetProperty("data")
                        .GetProperty("settings")
                        .GetProperty("downloadProxy")
                        .GetString());
            }

            Assert.Equal("http://127.0.0.1:7890", (await store.LoadAsync()).DownloadProxy);

            using (var document = JsonDocument.Parse(
                       await service.HandleAsync(
                           "settings.set",
                           """{"downloadProxy":"ftp://127.0.0.1:1080"}""",
                           CancellationToken.None)))
            {
                Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
                Assert.Equal(
                    "E_INVALID",
                    document.RootElement
                        .GetProperty("error")
                        .GetProperty("code")
                        .GetString());
            }

            using (var document = JsonDocument.Parse(
                       await service.HandleAsync(
                           "settings.set",
                           """{"downloadProxy":null}""",
                           CancellationToken.None)))
            {
                Assert.Equal(
                    JsonValueKind.Null,
                    document.RootElement
                        .GetProperty("data")
                        .GetProperty("settings")
                        .GetProperty("downloadProxy")
                        .ValueKind);
            }

            Assert.Null((await store.LoadAsync()).DownloadProxy);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Store_persists_java_selection_and_preserves_roots()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "games");
            var java = Path.Combine(directory, "jdk", "bin", "java");
            Directory.CreateDirectory(Path.GetDirectoryName(java)!);
            await File.WriteAllTextAsync(java, string.Empty);
            var store = new JsonAppSettingsStore(dataRoot);
            await store.SaveInstancesRootAsync(root);

            var saved = await store.SaveJavaAsync(java, javaAuto: false);
            Assert.Equal(Path.GetFullPath(java), saved.JavaPath);
            Assert.False(saved.JavaAuto);
            Assert.Equal([Path.GetFullPath(root)], saved.Roots);

            var reloaded = await store.LoadAsync();
            Assert.Equal(Path.GetFullPath(java), reloaded.JavaPath);
            Assert.False(reloaded.JavaAuto);

            var automatic = await store.SaveJavaAsync(javaPath: null, javaAuto: true);
            Assert.Null(automatic.JavaPath);
            Assert.True(automatic.JavaAuto);
            Assert.Equal([Path.GetFullPath(root)], automatic.Roots);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Store_persists_memory_and_jvm_options()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var store = new JsonAppSettingsStore(dataRoot);
            var saved = await store.SaveAsync(
                (await store.LoadAsync()) with
                {
                    MemoryMinMb = 1024,
                    MemoryMaxMb = 4096,
                    JvmArgs = "-XX:+UseG1GC",
                    GcMode = "zgc",
                    GameArgs = "--demo",
                });

            Assert.Equal(1024, saved.MemoryMinMb);
            Assert.Equal(4096, saved.MemoryMaxMb);
            Assert.Equal("-XX:+UseG1GC", saved.JvmArgs);
            Assert.Equal("zgc", saved.GcMode);
            Assert.Equal("--demo", saved.GameArgs);

            var reloaded = await store.LoadAsync();
            Assert.Equal(1024, reloaded.MemoryMinMb);
            Assert.Equal(4096, reloaded.MemoryMaxMb);
            Assert.Equal("-XX:+UseG1GC", reloaded.JvmArgs);
            Assert.Equal("zgc", reloaded.GcMode);
            Assert.Equal("--demo", reloaded.GameArgs);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Settings_module_updates_runtime_options()
    {
        var directory = TempDirectory();
        try
        {
            var service = new SettingsModuleService(
                new JsonAppSettingsStore(Path.Combine(directory, "data")));
            using var document = JsonDocument.Parse(
                await service.HandleAsync(
                    "settings.set",
                    """{"memoryMinMb":1024,"memoryMaxMb":4096,"jvmArgs":"-XX:+UseG1GC","gcMode":"zgc","gameArgs":"--demo"}""",
                    CancellationToken.None));
            var settings = document.RootElement.GetProperty("data").GetProperty("settings");
            Assert.Equal(1024, settings.GetProperty("memoryMinMb").GetInt32());
            Assert.Equal(4096, settings.GetProperty("memoryMaxMb").GetInt32());
            Assert.Equal("-XX:+UseG1GC", settings.GetProperty("jvmArgs").GetString());
            Assert.Equal("zgc", settings.GetProperty("gcMode").GetString());
            Assert.Equal("--demo", settings.GetProperty("gameArgs").GetString());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Settings_module_updates_java_without_replacing_roots()
    {
        var directory = TempDirectory();
        try
        {
            var service = new SettingsModuleService(
                new JsonAppSettingsStore(Path.Combine(directory, "data")));
            var java = Path.Combine(directory, "jdk", "bin", "java");
            Directory.CreateDirectory(Path.GetDirectoryName(java)!);
            await File.WriteAllTextAsync(java, string.Empty);

            var payload = $$"""{"javaPath":"{{java}}","javaAuto":false}""";
            using var document = JsonDocument.Parse(
                await service.HandleAsync("settings.set", payload, CancellationToken.None));
            var settings = document.RootElement.GetProperty("data").GetProperty("settings");
            Assert.Equal(Path.GetFullPath(java), settings.GetProperty("javaPath").GetString());
            Assert.False(settings.GetProperty("javaAuto").GetBoolean());
            Assert.True(settings.GetProperty("instancesRoots").GetArrayLength() >= 1);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Store_rejects_a_relative_instances_root()
    {
        var directory = TempDirectory();
        try
        {
            var store = new JsonAppSettingsStore(Path.Combine(directory, "data"));

            await Assert.ThrowsAsync<ArgumentException>(
                () => store.SaveInstancesRootAsync("relative/instances"));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Module_persists_window_and_fullscreen_settings()
    {
        var directory = TempDirectory();
        try
        {
            var store = new JsonAppSettingsStore(Path.Combine(directory, "data"));
            var service = new SettingsModuleService(store);

            using var setDocument = JsonDocument.Parse(
                await service.HandleAsync(
                    "settings.set",
                    """{"windowWidth":1920,"windowHeight":1080,"fullscreen":true}""",
                    CancellationToken.None));
            Assert.True(setDocument.RootElement.GetProperty("ok").GetBoolean());

            using var getDocument = JsonDocument.Parse(
                await service.HandleAsync("settings.get", "{}", CancellationToken.None));
            var settings = getDocument.RootElement
                .GetProperty("data")
                .GetProperty("settings");
            Assert.Equal(1920, settings.GetProperty("windowWidth").GetInt32());
            Assert.Equal(1080, settings.GetProperty("windowHeight").GetInt32());
            Assert.True(settings.GetProperty("fullscreen").GetBoolean());

            // Out-of-range values are rejected instead of silently clamped.
            using var invalid = JsonDocument.Parse(
                await service.HandleAsync(
                    "settings.set",
                    """{"windowWidth":100}""",
                    CancellationToken.None));
            Assert.False(invalid.RootElement.GetProperty("ok").GetBoolean());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Module_persists_download_tuning_and_log_retention()
    {
        var directory = TempDirectory();
        try
        {
            var service = new SettingsModuleService(
                new JsonAppSettingsStore(Path.Combine(directory, "data")));

            using var setDocument = JsonDocument.Parse(
                await service.HandleAsync(
                    "settings.set",
                    """{"downloadChunkSizeKb":2048,"downloadRangeThresholdKb":0,"logRetentionFiles":20,"logRetentionMb":64}""",
                    CancellationToken.None));
            Assert.True(setDocument.RootElement.GetProperty("ok").GetBoolean());

            using var getDocument = JsonDocument.Parse(
                await service.HandleAsync("settings.get", "{}", CancellationToken.None));
            var settings = getDocument.RootElement
                .GetProperty("data")
                .GetProperty("settings");
            Assert.Equal(2048, settings.GetProperty("downloadChunkSizeKb").GetInt32());
            Assert.Equal(0, settings.GetProperty("downloadRangeThresholdKb").GetInt32());
            Assert.Equal(20, settings.GetProperty("logRetentionFiles").GetInt32());
            Assert.Equal(64, settings.GetProperty("logRetentionMb").GetInt32());

            using var invalid = JsonDocument.Parse(
                await service.HandleAsync(
                    "settings.set",
                    """{"logRetentionFiles":500}""",
                    CancellationToken.None));
            Assert.False(invalid.RootElement.GetProperty("ok").GetBoolean());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Module_persists_the_loader_sharing_preference()
    {
        var directory = TempDirectory();
        try
        {
            var service = new SettingsModuleService(
                new JsonAppSettingsStore(Path.Combine(directory, "data")));

            using var defaults = JsonDocument.Parse(
                await service.HandleAsync("settings.get", "{}", CancellationToken.None));
            Assert.True(defaults.RootElement
                .GetProperty("data")
                .GetProperty("settings")
                .GetProperty("shareLoaderFiles")
                .GetBoolean());

            using var setDocument = JsonDocument.Parse(
                await service.HandleAsync(
                    "settings.set",
                    """{"shareLoaderFiles":false}""",
                    CancellationToken.None));
            Assert.True(setDocument.RootElement.GetProperty("ok").GetBoolean());

            using var getDocument = JsonDocument.Parse(
                await service.HandleAsync("settings.get", "{}", CancellationToken.None));
            Assert.False(getDocument.RootElement
                .GetProperty("data")
                .GetProperty("settings")
                .GetProperty("shareLoaderFiles")
                .GetBoolean());

            using var invalid = JsonDocument.Parse(
                await service.HandleAsync(
                    "settings.set",
                    """{"shareLoaderFiles":"yes"}""",
                    CancellationToken.None));
            Assert.False(invalid.RootElement.GetProperty("ok").GetBoolean());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Module_persists_memory_mode_and_game_optimization()
    {
        var directory = TempDirectory();
        try
        {
            var service = new SettingsModuleService(
                new JsonAppSettingsStore(Path.Combine(directory, "data")));

            using var defaults = JsonDocument.Parse(
                await service.HandleAsync("settings.get", "{}", CancellationToken.None));
            var initial = defaults.RootElement
                .GetProperty("data")
                .GetProperty("settings");
            Assert.Equal("auto", initial.GetProperty("memoryMode").GetString());
            Assert.True(initial.GetProperty("gameOptimization").GetBoolean());

            using var setDocument = JsonDocument.Parse(
                await service.HandleAsync(
                    "settings.set",
                    """{"memoryMode":"manual","gameOptimization":false}""",
                    CancellationToken.None));
            Assert.True(setDocument.RootElement.GetProperty("ok").GetBoolean());

            using var getDocument = JsonDocument.Parse(
                await service.HandleAsync("settings.get", "{}", CancellationToken.None));
            var settings = getDocument.RootElement
                .GetProperty("data")
                .GetProperty("settings");
            Assert.Equal("manual", settings.GetProperty("memoryMode").GetString());
            Assert.False(settings.GetProperty("gameOptimization").GetBoolean());

            using var invalid = JsonDocument.Parse(
                await service.HandleAsync(
                    "settings.set",
                    """{"memoryMode":"sometimes"}""",
                    CancellationToken.None));
            Assert.False(invalid.RootElement.GetProperty("ok").GetBoolean());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Schema_10_custom_heap_stays_manual_after_upgrade()
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
                  {"schemaVersion":10,"instancesRoot":"{{games}}",
                   "memoryMinMb":1024,"memoryMaxMb":6144}
                  """);

            var loaded = await new JsonAppSettingsStore(dataRoot).LoadAsync();

            Assert.Equal(AppSettings.MemoryModes.Manual, loaded.MemoryMode);
            Assert.Equal(6144, loaded.MemoryMaxMb);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Schema_10_default_heap_becomes_automatic()
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
                  {"schemaVersion":10,"instancesRoot":"{{games}}",
                   "memoryMinMb":512,"memoryMaxMb":2048}
                  """);

            var loaded = await new JsonAppSettingsStore(dataRoot).LoadAsync();

            Assert.Equal(AppSettings.MemoryModes.Auto, loaded.MemoryMode);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Module_returns_settings_json_and_updates_it()
    {
        var directory = TempDirectory();
        try
        {
            var service = new SettingsModuleService(
                new JsonAppSettingsStore(Path.Combine(directory, "data")));
            var instancesRoot = Path.Combine(directory, "custom-instances");

            using var getDocument = JsonDocument.Parse(
                await service.HandleAsync("settings.get", "{}", CancellationToken.None));
            Assert.True(getDocument.RootElement.GetProperty("ok").GetBoolean());

            using var setDocument = JsonDocument.Parse(
                await service.HandleAsync(
                    "settings.set",
                    $$"""{"instancesRoot":"{{instancesRoot}}"}""",
                    CancellationToken.None));
            Assert.True(setDocument.RootElement.GetProperty("ok").GetBoolean());
            var settings = setDocument.RootElement
                .GetProperty("data")
                .GetProperty("settings");
            Assert.Equal(
                Path.GetFullPath(instancesRoot),
                settings.GetProperty("instancesRoot").GetString());
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
            "archoera-settings",
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
