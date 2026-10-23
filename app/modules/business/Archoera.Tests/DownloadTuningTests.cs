using Archoera.Domain.Downloads;
using Archoera.Domain.Settings;
using Archoera.Infrastructure.Downloads;

namespace Archoera.Tests;

public sealed class DownloadTuningTests
{
    private static DownloadPlan Plan(int concurrency = 8, int perHost = 6) =>
        new()
        {
            PlanId = "tuning",
            Root = Path.GetTempPath(),
            Concurrency = concurrency,
            Options = DownloadPlanOptions.Default with { PerHostConcurrency = perHost },
            Files = [],
        };

    private static AppSettings Settings(int concurrency, int perHost) => new(
        InstancesRoot: Path.GetTempPath(),
        DataRoot: Path.GetTempPath(),
        SettingsPath: string.Empty,
        DownloadConcurrency: concurrency,
        DownloadPerHost: perHost);

    [Fact]
    public void User_tuning_overrides_producer_defaults()
    {
        var tuned = DownloadPlanRunner.ApplyTuning(Plan(), Settings(32, 4));

        Assert.Equal(32, tuned.Concurrency);
        Assert.Equal(4, tuned.Options.PerHostConcurrency);
    }

    [Fact]
    public void Out_of_range_values_are_clamped()
    {
        var tuned = DownloadPlanRunner.ApplyTuning(Plan(), Settings(999, -5));

        Assert.Equal(DownloadPlanLimits.MaxConcurrency, tuned.Concurrency);
        Assert.Equal(0, tuned.Options.PerHostConcurrency);
    }

    [Fact]
    public void Chunk_size_and_threshold_follow_settings()
    {
        var settings = Settings(16, 16) with
        {
            DownloadChunkSizeKb = 1024,
            DownloadRangeThresholdKb = 4096,
        };

        var tuned = DownloadPlanRunner.ApplyTuning(Plan(), settings);

        Assert.Equal(1024L * 1024, tuned.Options.ChunkSize);
        Assert.Equal(4096L * 1024, tuned.Options.RangeThreshold);
    }

    [Fact]
    public void Disabling_ranges_keeps_the_plan_single_stream()
    {
        var tuned = DownloadPlanRunner.ApplyTuning(
            Plan(),
            Settings(16, 16) with { DownloadRangeThresholdKb = 0 });

        Assert.Equal(0, tuned.Options.RangeThreshold);
    }

    [Fact]
    public void Zero_disables_the_per_host_cap()
    {
        var tuned = DownloadPlanRunner.ApplyTuning(Plan(), Settings(16, 0));

        Assert.Equal(0, tuned.Options.PerHostConcurrency);
        Assert.Equal(16, tuned.Concurrency);
    }
}
