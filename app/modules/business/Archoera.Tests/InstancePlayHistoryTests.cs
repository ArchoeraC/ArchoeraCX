using Archoera.Application.Instances;
using Archoera.Domain.Instances;
using Archoera.Infrastructure.Instances;

namespace Archoera.Tests;

public sealed class InstancePlayHistoryTests
{
    [Fact]
    public async Task Records_the_launch_timestamp()
    {
        var at = DateTimeOffset.UtcNow;
        var repository = new RecordingInstances();

        var ok = await InstancePlayHistory.MarkPlayedAsync(repository, "pack", at);

        Assert.True(ok);
        Assert.Equal("pack", repository.UpdatedId);
        Assert.Equal(at, repository.LastPlayedAt);
    }

    [Fact]
    public async Task A_failed_write_never_fails_the_launch()
    {
        var repository = new RecordingInstances { Throw = true };

        var ok = await InstancePlayHistory.MarkPlayedAsync(
            repository,
            "pack",
            DateTimeOffset.UtcNow);

        Assert.False(ok);
    }

    [Fact]
    public async Task An_unknown_instance_reports_failure_without_throwing()
    {
        var repository = new RecordingInstances { Missing = true };

        var ok = await InstancePlayHistory.MarkPlayedAsync(
            repository,
            "gone",
            DateTimeOffset.UtcNow);

        Assert.False(ok);
    }

    private sealed class RecordingInstances : IInstanceRepository
    {
        public bool Throw { get; init; }

        public bool Missing { get; init; }

        public string? UpdatedId { get; private set; }

        public DateTimeOffset? LastPlayedAt { get; private set; }

        public Task<IReadOnlyList<GameInstanceRecord>> ListAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<GameInstanceRecord>>([]);

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
        {
            if (Throw) throw new IOException("registry is busy");
            if (Missing) return Task.FromResult<GameInstanceRecord?>(null);

            UpdatedId = id;
            LastPlayedAt = lastPlayedAt;
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult<GameInstanceRecord?>(
                new GameInstanceRecord(
                    id,
                    "pack",
                    "1.21.1",
                    "neoforge",
                    "21.1.264",
                    "/tmp/pack",
                    0,
                    false,
                    now,
                    now,
                    lastPlayedAt));
        }

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
