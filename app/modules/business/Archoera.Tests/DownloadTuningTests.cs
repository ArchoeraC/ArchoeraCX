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
    public void Resource_profile_uses_shorter_ranges()
    {
        var settings = Settings(16, 8) with
        {
            DownloadChunkSizeKb = 4096,
            DownloadRangeThresholdKb = 8192,
        };

        var tuned = DownloadPlanRunner.ApplyTuning(
            Plan(),
            settings,
            DownloadProfile.Resource);

        Assert.Equal(512L * 1024, tuned.Options.ChunkSize);
        Assert.Equal(512L * 1024, tuned.Options.RangeThreshold);
        Assert.Equal(DownloadDurability.Fast, tuned.Options.Durability);
    }

    [Fact]
    public void Resource_profile_respects_disabled_ranges()
    {
        var settings = Settings(16, 8) with { DownloadRangeThresholdKb = 0 };

        var tuned = DownloadPlanRunner.ApplyTuning(
            Plan(),
            settings,
            DownloadProfile.Resource);

        Assert.Equal(0, tuned.Options.RangeThreshold);
    }

    [Fact]
    public void Resource_profile_raises_the_default_host_cap_for_many_files()
    {
        var file = PlanFile.Create(
            "https://cdn.example.invalid/a.jar",
            "mods/a.jar",
            DownloadHash.Create(DownloadHashAlgorithm.Sha1, new string('a', 40)),
            1024);
        var many = Plan() with { Files = [file, file with { Dest = "mods/b.jar" }] };

        var defaultCap = DownloadPlanRunner.ApplyTuning(
            many,
            Settings(16, 8),
            DownloadProfile.Resource);
        Assert.Equal(16, defaultCap.Options.PerHostConcurrency);

        var explicitLow = DownloadPlanRunner.ApplyTuning(
            many,
            Settings(16, 4),
            DownloadProfile.Resource);
        Assert.Equal(4, explicitLow.Options.PerHostConcurrency);
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
