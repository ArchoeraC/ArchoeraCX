using System.Text.Json;
using Archoera.Domain.Settings;
using Archoera.Infrastructure.Settings;

namespace Archoera.Tests;

/// <summary>
/// Wrapper and environment options are part of the settings document. These
/// tests pin the JSON shape, the null/empty semantics and the validation that
/// protects the child JVM.
/// </summary>
public sealed class SettingsRuntimeOptionsTests
{
    [Fact]
    public async Task Runtime_options_round_trip_through_settings_set_and_get()
    {
        var directory = TempDirectory();
        try
        {
            var store = new JsonAppSettingsStore(Path.Combine(directory, "data"));
            var service = new SettingsModuleService(store);

            using (var setDocument = JsonDocument.Parse(
                       await service.HandleAsync(
                           "settings.setRuntimeOptions",
                           """
                           {"wrapperCommand":"prime-run --keep \"my wrapper\"",
                            "environment":{"MESA_GL_VERSION_OVERRIDE":"4.6",
                                           "DRI_PRIME":"1"}}
                           """,
                           CancellationToken.None)))
            {
                Assert.True(setDocument.RootElement.GetProperty("ok").GetBoolean());
                var settings = Settings(setDocument);
                Assert.Equal(
                    "prime-run --keep \"my wrapper\"",
                    settings.GetProperty("wrapperCommand").GetString());
                var environment = settings.GetProperty("environment");
                Assert.Equal("4.6", environment.GetProperty("MESA_GL_VERSION_OVERRIDE").GetString());
                Assert.Equal("1", environment.GetProperty("DRI_PRIME").GetString());
            }

            using (var getDocument = JsonDocument.Parse(
                       await service.HandleAsync("settings.get", "{}", CancellationToken.None)))
            {
                var settings = Settings(getDocument);
                Assert.Equal(
                    "prime-run --keep \"my wrapper\"",
                    settings.GetProperty("wrapperCommand").GetString());
                Assert.Equal(
                    2,
                    settings.GetProperty("environment").EnumerateObject().Count());
            }

            var reloaded = await store.LoadAsync();
            Assert.Equal(
                "prime-run --keep \"my wrapper\"",
                reloaded.WrapperCommand);
            Assert.NotNull(reloaded.Environment);
            Assert.Equal("4.6", reloaded.Environment!["MESA_GL_VERSION_OVERRIDE"]);
            Assert.Equal("1", reloaded.Environment["DRI_PRIME"]);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Environment_null_clears_and_wrapper_empty_disables()
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
                           {"wrapperCommand":"prime-run",
                            "environment":{"A":"1"}}
                           """,
                           CancellationToken.None)))
            {
                Assert.True(set.RootElement.GetProperty("ok").GetBoolean());
            }

            using (var clear = JsonDocument.Parse(
                       await service.HandleAsync(
                           "settings.setRuntimeOptions",
                           """{"wrapperCommand":null,"environment":null}""",
                           CancellationToken.None)))
            {
                var settings = Settings(clear);
                Assert.Equal(
                    JsonValueKind.Null,
                    settings.GetProperty("wrapperCommand").ValueKind);
                Assert.Empty(settings.GetProperty("environment").EnumerateObject());
            }

            var reloaded = await store.LoadAsync();
            Assert.Null(reloaded.WrapperCommand);
            Assert.True(reloaded.Environment is null || reloaded.Environment.Count == 0);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Runtime_options_endpoint_rejects_instance_roots()
    {
        var directory = TempDirectory();
        try
        {
            var service = new SettingsModuleService(
                new JsonAppSettingsStore(Path.Combine(directory, "data")));
            using var document = JsonDocument.Parse(
                await service.HandleAsync(
                    "settings.setRuntimeOptions",
                    $$"""{"instancesRoots":["{{Path.Combine(directory, "games")}}"]}""",
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

    [Theory]
    [InlineData("""{"wrapperCommand":"\"unclosed"}""")]
    [InlineData("""{"wrapperCommand":"\"\""}""")]
    [InlineData("""{"wrapperCommand":"line\nbreak"}""")]
    [InlineData("""{"environment":{"JAVA_TOOL_OPTIONS":"-Xmx1G"}}""")]
    [InlineData("""{"environment":{"_JAVA_OPTIONS":"-Xmx1G"}}""")]
    [InlineData("""{"environment":{"JDK_JAVA_OPTIONS":"-Xmx1G"}}""")]
    [InlineData("""{"environment":{"CLASSPATH":"/tmp/a.jar"}}""")]
    [InlineData("""{"environment":{"1BAD":"x"}}""")]
    [InlineData("""{"environment":{"GOOD-KEY":"x"}}""")]
    [InlineData("""{"environment":{"GOOD":1}}""")]
    [InlineData("""{"environment":[]}""")]
    public async Task Invalid_runtime_options_are_rejected(string payload)
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
    public async Task Environment_entry_and_value_limits_are_enforced()
    {
        var directory = TempDirectory();
        try
        {
            var service = new SettingsModuleService(
                new JsonAppSettingsStore(Path.Combine(directory, "data")));

            using (var tooMany = JsonDocument.Parse(
                       await service.HandleAsync(
                           "settings.setRuntimeOptions",
                           JsonSerializer.Serialize(new
                           {
                               environment = Enumerable.Range(0, 65)
                                   .ToDictionary(i => $"K{i}", i => "v"),
                           }),
                           CancellationToken.None)))
            {
                Assert.False(tooMany.RootElement.GetProperty("ok").GetBoolean());
            }

            using (var tooLongValue = JsonDocument.Parse(
                       await service.HandleAsync(
                           "settings.setRuntimeOptions",
                           JsonSerializer.Serialize(new
                           {
                               environment = new Dictionary<string, string>
                               {
                                   ["A"] = new('x', 4097),
                               },
                           }),
                           CancellationToken.None)))
            {
                Assert.False(tooLongValue.RootElement.GetProperty("ok").GetBoolean());
            }

            using (var tooLongKey = JsonDocument.Parse(
                       await service.HandleAsync(
                           "settings.setRuntimeOptions",
                           JsonSerializer.Serialize(new
                           {
                               environment = new Dictionary<string, string>
                               {
                                   [new string('A', 129)] = "v",
                               },
                           }),
                           CancellationToken.None)))
            {
                Assert.False(tooLongKey.RootElement.GetProperty("ok").GetBoolean());
            }

            // At the limits the values are accepted.
            using var accepted = JsonDocument.Parse(
                await service.HandleAsync(
                    "settings.setRuntimeOptions",
                    JsonSerializer.Serialize(new
                    {
                        wrapperCommand = new string('w', 4096),
                        environment = new Dictionary<string, string>
                        {
                            [new string('A', 128)] = new string('v', 4096),
                        },
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
    public async Task Schema_12_document_migrates_with_empty_runtime_options()
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
                  {"schemaVersion":12,"instancesRoot":"{{games}}",
                   "jvmArgs":"-XX:+UseG1GC"}
                  """);

            var loaded = await new JsonAppSettingsStore(dataRoot).LoadAsync();

            Assert.Null(loaded.WrapperCommand);
            Assert.True(loaded.Environment is null || loaded.Environment.Count == 0);
            Assert.Equal("-XX:+UseG1GC", loaded.JvmArgs);

            // Saving the migrated document writes the current schema and an
            // environment object so the UI never needs a missing-key branch.
            var saved = await new JsonAppSettingsStore(dataRoot)
                .SaveAsync(loaded);
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(saved.SettingsPath));
            Assert.Equal(
                AppSettings.CurrentSchemaVersion,
                document.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(
                JsonValueKind.Object,
                document.RootElement.GetProperty("environment").ValueKind);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Settings_store_round_trips_runtime_options()
    {
        var directory = TempDirectory();
        try
        {
            var store = new JsonAppSettingsStore(Path.Combine(directory, "data"));
            var saved = await store.SaveAsync(
                (await store.LoadAsync()) with
                {
                    WrapperCommand = "env FOO=bar",
                    Environment = new Dictionary<string, string>
                    {
                        ["MANGOHUD"] = "1",
                    },
                });

            var reloaded = await store.LoadAsync();
            Assert.Equal("env FOO=bar", reloaded.WrapperCommand);
            Assert.Equal("1", reloaded.Environment!["MANGOHUD"]);
            Assert.Equal("env FOO=bar", saved.WrapperCommand);
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

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-settings-runtime",
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
