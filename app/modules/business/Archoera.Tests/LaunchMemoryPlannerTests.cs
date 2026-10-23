using Archoera.Domain.Instances;
using Archoera.Domain.Settings;
using Archoera.Infrastructure.Launch;

namespace Archoera.Tests;

/// <summary>
/// Automatic heap sizing: the value has to follow the game, the loader, the
/// mods and the machine, and manual values have to be honoured with a warning
/// instead of being silently clamped.
/// </summary>
public sealed class LaunchMemoryPlannerTests
{
    [Fact]
    public void Vanilla_gets_the_version_baseline()
    {
        var recommended = LaunchMemoryPlanner.Recommend(
            Instance("1.21.1", "vanilla"),
            modCount: 0,
            totalMb: 32768);

        Assert.Equal(2048, recommended);
    }

    [Fact]
    public void Old_versions_ask_for_less()
    {
        Assert.Equal(
            1024,
            LaunchMemoryPlanner.Recommend(
                Instance("1.12.2", "vanilla"),
                modCount: 0,
                totalMb: 32768));
        Assert.Equal(
            1536,
            LaunchMemoryPlanner.Recommend(
                Instance("1.16.5", "vanilla"),
                modCount: 0,
                totalMb: 32768));
    }

    [Fact]
    public void Loader_and_mods_raise_the_heap()
    {
        // 2048 (1.21) + 512 (NeoForge) + 126 * 8 = 3568, rounded to 3584.
        Assert.Equal(
            3584,
            LaunchMemoryPlanner.Recommend(
                Instance("1.21.1", "neoforge"),
                modCount: 126,
                totalMb: 32768));
    }

    [Fact]
    public void The_system_cap_wins_on_a_small_machine()
    {
        // 4 GB machine: half the RAM, not the 3.5 GB the pack asks for.
        Assert.Equal(
            2048,
            LaunchMemoryPlanner.Recommend(
                Instance("1.21.1", "neoforge"),
                modCount: 126,
                totalMb: 4096));
    }

    [Fact]
    public void The_floor_wins_on_a_tiny_machine()
    {
        Assert.Equal(
            LaunchMemoryPlanner.AutoFloorMb,
            LaunchMemoryPlanner.Recommend(
                Instance("1.21.1", "neoforge"),
                modCount: 126,
                totalMb: 2048));
    }

    [Fact]
    public void Auto_mode_reports_the_resolved_heap()
    {
        var plan = LaunchMemoryPlanner.Resolve(
            Settings("auto", 512, 2048),
            Instance("1.21.1", "vanilla"),
            instanceRoot: null,
            memory: (32768, 16384));

        Assert.Equal("auto", plan.Mode);
        Assert.Equal(2048, plan.MaxMb);
        Assert.Equal(1024, plan.MinMb);
        Assert.Null(plan.WarningCode);
    }

    [Fact]
    public void A_small_machine_warns_even_in_auto_mode()
    {
        // 3.5 GB: the heap is capped at half the RAM and the machine is worth
        // flagging even though the launcher picked the value itself.
        var plan = LaunchMemoryPlanner.Resolve(
            Settings("auto", 512, 2048),
            Instance("1.21.1", "vanilla"),
            instanceRoot: null,
            memory: (3584, 1792));

        Assert.Equal("SYSTEM_LOW_MEMORY", plan.WarningCode);
        Assert.Equal(1792, plan.MaxMb);
    }

    [Fact]
    public void Manual_below_one_gigabyte_is_flagged()
    {
        var plan = LaunchMemoryPlanner.Resolve(
            Settings("manual", 512, 512),
            Instance("1.21.1", "vanilla"),
            instanceRoot: null,
            memory: (32768, 16384));

        Assert.Equal("manual", plan.Mode);
        Assert.Equal(512, plan.MaxMb);
        Assert.Equal("LOW_MEMORY", plan.WarningCode);
    }

    [Fact]
    public void Manual_far_below_the_recommendation_is_flagged()
    {
        var plan = LaunchMemoryPlanner.Resolve(
            Settings("manual", 1024, 1024),
            Instance("1.21.1", "neoforge"),
            instanceRoot: null,
            memory: (32768, 16384));

        Assert.Equal("BELOW_RECOMMENDED", plan.WarningCode);
        Assert.True(plan.RecommendedMb > 1024);
    }

    [Fact]
    public void Manual_above_the_system_is_flagged()
    {
        var plan = LaunchMemoryPlanner.Resolve(
            Settings("manual", 4096, 8000),
            Instance("1.21.1", "vanilla"),
            instanceRoot: null,
            memory: (8192, 4096));

        Assert.Equal("OVER_SYSTEM", plan.WarningCode);
    }

    [Fact]
    public void An_instance_heap_override_means_manual_for_that_instance()
    {
        var overrides = new InstanceOverrides(MemoryMaxMb: 6144);
        var instance = Instance("1.21.1", "vanilla") with { Overrides = overrides };
        // Mirrors the launch module: overrides are merged into the effective
        // settings, and the mode still says `auto`.
        var effective = overrides.ApplyTo(Settings("auto", 512, 2048));

        var plan = LaunchMemoryPlanner.Resolve(
            effective,
            instance,
            instanceRoot: null,
            memory: (32768, 16384));

        Assert.Equal("manual", plan.Mode);
        Assert.Equal(6144, plan.MaxMb);
    }

    [Fact]
    public void Disabled_mods_are_not_counted()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "archoera-memory",
            Guid.NewGuid().ToString("N"));
        try
        {
            var mods = Path.Combine(directory, "mods");
            Directory.CreateDirectory(mods);
            File.WriteAllText(Path.Combine(mods, "a.jar"), "x");
            File.WriteAllText(Path.Combine(mods, "b.jar"), "x");
            File.WriteAllText(Path.Combine(mods, "c.jar.disabled"), "x");
            Directory.CreateDirectory(Path.Combine(mods, "nested"));
            File.WriteAllText(Path.Combine(mods, "nested", "d.jar"), "x");

            Assert.Equal(2, LaunchMemoryPlanner.CountEnabledMods(directory));
            Assert.Equal(0, LaunchMemoryPlanner.CountEnabledMods(
                Path.Combine(directory, "missing")));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static GameInstanceRecord Instance(string gameVersion, string loader) =>
        new(
            "instance",
            "Instance",
            gameVersion,
            loader,
            string.Empty,
            Path.Combine(Path.GetTempPath(), "archoera-memory", "instance"),
            0,
            false,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null);

    private static AppSettings Settings(string mode, int min, int max) =>
        new(
            InstancesRoot: Path.GetTempPath(),
            DataRoot: Path.GetTempPath(),
            SettingsPath: string.Empty,
            MemoryMinMb: min,
            MemoryMaxMb: max,
            MemoryMode: mode);
}
