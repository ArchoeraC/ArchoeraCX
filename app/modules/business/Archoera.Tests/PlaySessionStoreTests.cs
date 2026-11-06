using Archoera.Application.Stats;
using Archoera.Infrastructure.Stats;

namespace Archoera.Tests;

public sealed class PlaySessionStoreTests
{
    [Fact]
    public async Task Begin_complete_and_list_round_trip()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var store = new SqlitePlaySessionStore(dataRoot);
            var startedAt = DateTimeOffset.UtcNow.AddMinutes(-90);
            var startedDay = DateOnly
                .FromDateTime(startedAt.ToLocalTime().DateTime)
                .ToString("yyyy-MM-dd");

            await store.BeginAsync(
                new PlaySessionStart(
                    "session-1",
                    "inst-1",
                    "My Pack",
                    "fabric",
                    "1.21.4",
                    startedAt));

            var running = Assert.Single(await store.ListAsync());
            Assert.Equal("session-1", running.Id);
            Assert.Null(running.EndedAt);
            Assert.Equal(0, running.DurationMs);
            Assert.Equal(startedDay, running.StartedDay);

            await store.CompleteAsync(
                "session-1",
                startedAt.AddMinutes(90),
                exitCode: 0);

            var completed = Assert.Single(await store.ListAsync());
            Assert.NotNull(completed.EndedAt);
            Assert.Equal(5_400_000, completed.DurationMs);
            Assert.Equal(0, completed.ExitCode);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Complete_is_idempotent_and_abort_removes_a_running_session()
    {
        var directory = TempDirectory();
        try
        {
            var store = new SqlitePlaySessionStore(Path.Combine(directory, "data"));
            var startedAt = DateTimeOffset.UtcNow;

            await store.BeginAsync(
                new PlaySessionStart(
                    "keep", "inst-1", "Pack", "vanilla", "1.20.1", startedAt));
            await store.CompleteAsync("keep", startedAt.AddMinutes(5), 0);
            await store.CompleteAsync("keep", startedAt.AddMinutes(60), 1);

            var kept = Assert.Single(await store.ListAsync());
            Assert.Equal(300_000, kept.DurationMs);
            Assert.Equal(0, kept.ExitCode);

            await store.BeginAsync(
                new PlaySessionStart(
                    "drop", "inst-2", "Pack 2", "forge", "1.20.1", startedAt));
            await store.AbortAsync("drop");

            Assert.Single(await store.ListAsync());
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
            "archoera-play-stats",
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
            // SQLite may still hold the file on a slow CI filesystem.
        }
    }
}
