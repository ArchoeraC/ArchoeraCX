using System.Text.Json;
using Archoera.Application.Instances;
using Archoera.Domain.Instances;
using Archoera.Domain.Settings;
using Archoera.Infrastructure.Launch;
using Archoera.Infrastructure.Settings;

namespace Archoera.Tests;

/// <summary>
/// `launch.memory` is the preview the settings panel, the instance dialog and
/// the pre-launch warning share; it has to report the resolved heap and the
/// warning code for the instance it was asked about.
/// </summary>
public sealed class LaunchMemoryModuleTests
{
    [Fact]
    public async Task Preview_reports_the_automatic_heap_and_mod_count()
    {
        var directory = TempDirectory();
        try
        {
            var instanceRoot = Path.Combine(directory, "instance");
            var mods = Path.Combine(instanceRoot, "mods");
            Directory.CreateDirectory(mods);
            File.WriteAllText(Path.Combine(mods, "a.jar"), "x");
            File.WriteAllText(Path.Combine(mods, "b.jar"), "x");

            var service = new LaunchModuleService(
                instances: new StubInstances(Instance(instanceRoot)),
                dataRoot: directory);

            using var document = JsonDocument.Parse(
                await service.HandleAsync(
                    "launch.memory",
                    """{"instanceId":"instance"}""",
                    CancellationToken.None));
            Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
            var data = document.RootElement.GetProperty("data");
            Assert.Equal("auto", data.GetProperty("mode").GetString());
            Assert.Equal(2, data.GetProperty("modCount").GetInt32());
            // 2048 (1.21) + 512 (NeoForge) + 2 mods, rounded to 256 MB. The
            // machine cap can only lower it, never below the floor.
            var recommended = data.GetProperty("recommendedMb").GetInt32();
            Assert.True(recommended >= LaunchMemoryPlanner.AutoFloorMb);
            Assert.Equal(0, recommended % 256);
            Assert.True(data.GetProperty("systemTotalMb").GetInt64() > 0);
            Assert.Equal(JsonValueKind.Null, data.GetProperty("warningCode").ValueKind);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Preview_warns_when_the_manual_heap_is_too_small()
    {
        var directory = TempDirectory();
        try
        {
            var instanceRoot = Path.Combine(directory, "instance");
            Directory.CreateDirectory(instanceRoot);
            var store = new JsonAppSettingsStore(directory);
            var current = await store.LoadAsync();
            await store.SaveAsync(current with
            {
                MemoryMode = AppSettings.MemoryModes.Manual,
                MemoryMinMb = 512,
                MemoryMaxMb = 512,
            });

            var service = new LaunchModuleService(
                instances: new StubInstances(Instance(instanceRoot)),
                dataRoot: directory);

            using var document = JsonDocument.Parse(
                await service.HandleAsync(
                    "launch.memory",
                    """{"instanceId":"instance"}""",
                    CancellationToken.None));
            var data = document.RootElement.GetProperty("data");
            Assert.Equal("manual", data.GetProperty("mode").GetString());
            Assert.Equal(512, data.GetProperty("maxMb").GetInt32());
            Assert.Equal("LOW_MEMORY", data.GetProperty("warningCode").GetString());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Preview_rejects_an_unknown_instance()
    {
        var directory = TempDirectory();
        try
        {
            var service = new LaunchModuleService(
                instances: new StubInstances(
                    Instance(Path.Combine(directory, "instance"))),
                dataRoot: directory);

            using var document = JsonDocument.Parse(
                await service.HandleAsync(
                    "launch.memory",
                    """{"instanceId":"missing"}""",
                    CancellationToken.None));
            Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "INSTANCE_NOT_FOUND",
                document.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            Delete(directory);
        }
    }

    private static GameInstanceRecord Instance(string rootPath) =>
        new(
            "instance",
            "Instance",
            "1.21.1",
            "neoforge",
            "21.1.176",
            rootPath,
            0,
            false,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null);

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-launch-memory",
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

    private sealed class StubInstances(GameInstanceRecord instance) : IInstanceRepository
    {
        public Task<IReadOnlyList<GameInstanceRecord>> ListAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<GameInstanceRecord>>([instance]);

        public Task<GameInstanceRecord> CreateAsync(
            string name,
            string gameVersion,
            string loader,
            string loaderVersion,
            string? rootPath,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<GameInstanceRecord?> UpdateAsync(
            string id,
            string? name,
            bool? favorite,
            DateTimeOffset? lastPlayedAt,
            InstanceOverrides? overrides = null,
            bool setOverrides = false,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> DeleteAsync(
            string id,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<InstanceSyncReport> SyncAsync(
            IReadOnlyList<string>? roots,
            bool dryRun,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
