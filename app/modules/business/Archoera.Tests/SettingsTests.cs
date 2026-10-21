using System.Text.Json;
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
