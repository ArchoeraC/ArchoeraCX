using System.Text.Json;
using Archoera.Application.Instances;
using Archoera.Application.Stats;
using Archoera.Domain.Instances;
using Archoera.Infrastructure.Stats;

namespace Archoera.Tests;

public sealed class StatsModuleTests
{
    [Fact]
    public async Task Stats_get_returns_one_consistent_snapshot()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var store = new SqlitePlaySessionStore(dataRoot);
            var startedAt = DateTimeOffset.UtcNow.AddHours(-1);
            await store.BeginAsync(
                new PlaySessionStart(
                    "s1", "inst-1", "My Pack", "fabric", "1.21.4", startedAt));
            await store.CompleteAsync("s1", startedAt.AddMinutes(30), 0);

            var now = DateTimeOffset.UtcNow;
            var instances = new FakeInstances([
                new GameInstanceRecord(
                    "inst-1",
                    "My Pack",
                    "1.21.4",
                    "fabric",
                    "0.16.10",
                    "/tmp/pack",
                    4,
                    false,
                    now,
                    now,
                    now),
            ]);
            var service = new StatsModuleService(instances, store);

            var payload = await service.HandleAsync("stats.get", null, CancellationToken.None);

            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            Assert.True(root.GetProperty("ok").GetBoolean());
            var data = root.GetProperty("data");
            var summary = data.GetProperty("summary");
            Assert.Equal(1, summary.GetProperty("totalSessions").GetInt32());
            Assert.Equal(1_800_000, summary.GetProperty("totalPlayMs").GetInt64());
            Assert.Equal(1, summary.GetProperty("instanceCount").GetInt32());
            Assert.Equal(4, summary.GetProperty("modCount").GetInt32());
            Assert.Equal(90, data.GetProperty("daily").GetArrayLength());
            Assert.Equal(24, data.GetProperty("hourly").GetArrayLength());

            var top = data.GetProperty("topInstances")[0];
            Assert.Equal("My Pack", top.GetProperty("name").GetString());
            Assert.Equal(1_800_000, top.GetProperty("playMs").GetInt64());
            Assert.Equal("fabric", top.GetProperty("loader").GetString());

            Assert.Equal(
                "fabric",
                data.GetProperty("loaders")[0].GetProperty("loader").GetString());
            Assert.Equal(1, data.GetProperty("recentSessions").GetArrayLength());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Unknown_method_is_rejected_with_a_stable_code()
    {
        var service = new StatsModuleService(
            new FakeInstances([]),
            new FakeSessions());

        var payload = await service.HandleAsync(
            "stats.unknown",
            null,
            CancellationToken.None);

        using var document = JsonDocument.Parse(payload);
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(
            "E_UNSUPPORTED",
            document.RootElement
                .GetProperty("error")
                .GetProperty("code")
                .GetString());
    }

    [Fact]
    public async Task A_session_left_open_by_a_previous_process_is_not_counted()
    {
        var startedAt = DateTimeOffset.UtcNow.AddHours(-2);
        var service = new StatsModuleService(
            new FakeInstances([]),
            new FakeSessions([
                new PlaySessionRow(
                    "stale",
                    "inst-1",
                    "Old Pack",
                    "vanilla",
                    "1.20.1",
                    startedAt,
                    null,
                    0,
                    "2026-11-05",
                    12,
                    null),
            ]));

        var payload = await service.HandleAsync("stats.get", null, CancellationToken.None);

        using var document = JsonDocument.Parse(payload);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(0, data.GetProperty("summary").GetProperty("totalSessions").GetInt32());
        Assert.Equal(0, data.GetProperty("summary").GetProperty("runningSessions").GetInt32());
        Assert.Equal(0, data.GetProperty("recentSessions").GetArrayLength());
    }

    [Fact]
    public async Task A_failed_start_does_not_add_time_or_a_session()
    {
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var service = new StatsModuleService(
            new FakeInstances([]),
            new FakeSessions([
                new PlaySessionRow(
                    "failed",
                    "inst-1",
                    "Broken Pack",
                    "forge",
                    "1.20.1",
                    startedAt,
                    startedAt.AddSeconds(5),
                    5_000,
                    "2026-11-05",
                    12,
                    1),
            ]));

        var payload = await service.HandleAsync("stats.get", null, CancellationToken.None);

        using var document = JsonDocument.Parse(payload);
        var data = document.RootElement.GetProperty("data");
        var summary = data.GetProperty("summary");
        Assert.Equal(0, summary.GetProperty("totalSessions").GetInt32());
        Assert.Equal(0, summary.GetProperty("totalPlayMs").GetInt64());
        Assert.Equal(0, data.GetProperty("topInstances").GetArrayLength());
        Assert.Equal(0, data.GetProperty("recentSessions").GetArrayLength());
    }

    [Fact]
    public async Task A_live_session_keeps_its_running_state()
    {
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        PlaySessionTracker.Activate("live");
        try
        {
            var service = new StatsModuleService(
                new FakeInstances([]),
                new FakeSessions([
                    new PlaySessionRow(
                        "live",
                        "inst-1",
                        "Live Pack",
                        "fabric",
                        "1.21.4",
                        startedAt,
                        null,
                        0,
                        "2026-11-05",
                        12,
                        null),
                ]));

            var payload = await service.HandleAsync(
                "stats.get",
                null,
                CancellationToken.None);

            using var document = JsonDocument.Parse(payload);
            var data = document.RootElement.GetProperty("data");
            Assert.Equal(
                1,
                data.GetProperty("summary").GetProperty("runningSessions").GetInt32());
            Assert.Equal(
                JsonValueKind.Null,
                data.GetProperty("recentSessions")[0].GetProperty("endedAt").ValueKind);
        }
        finally
        {
            PlaySessionTracker.Deactivate("live");
        }
    }

    private sealed class FakeInstances(IReadOnlyList<GameInstanceRecord> items)
        : IInstanceRepository
    {
        public Task<IReadOnlyList<GameInstanceRecord>> ListAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(items);

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
            string? loader = null,
            string? loaderVersion = null,
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

    private sealed class FakeSessions(IReadOnlyList<PlaySessionRow>? rows = null)
        : IPlaySessionStore
    {
        private readonly IReadOnlyList<PlaySessionRow> _rows = rows ?? [];

        public Task BeginAsync(
            PlaySessionStart session,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task CompleteAsync(
            string sessionId,
            DateTimeOffset endedAt,
            int exitCode,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task AbortAsync(
            string sessionId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<PlaySessionRow>> ListAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(_rows);
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-stats",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Delete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort on a slow CI filesystem.
        }
    }
}
