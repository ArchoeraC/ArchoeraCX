using Archoera.Application.Stats;
using Archoera.Domain.Instances;

namespace Archoera.Tests;

public sealed class PlayStatsBuilderTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 5, 12, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Aggregates_totals_week_streak_and_rankings()
    {
        var sessions = new[]
        {
            Session(
                "s1", "a", "Alpha", "fabric", "1.21.4",
                Days(2026, 11, 2, 10), 90, "2026-11-02", 10),
            Session(
                "s2", "a", "Alpha", "fabric", "1.21.4",
                Days(2026, 11, 4, 21), 30, "2026-11-04", 21),
            // Still running: counted as a session, zero playtime.
            new PlaySessionRow(
                "s3", "b", "Beta", "vanilla", "1.20.1",
                Days(2026, 11, 5, 9), null, 0, "2026-11-05", 9, null),
            Session(
                "s4", "a", "Alpha (old)", "fabric", "1.21.1",
                Days(2026, 10, 27, 20), 60, "2026-10-27", 20),
        };
        var instances = new[]
        {
            Instance("a", "Alpha", "fabric", 3),
            Instance("b", "Beta", "vanilla", 0),
        };

        var stats = PlayStatsBuilder.Build(sessions, instances, Now);

        Assert.Equal(10_800_000, stats.Summary.TotalPlayMs);
        Assert.Equal(7_200_000, stats.Summary.WeekPlayMs);
        Assert.Equal(3_600_000, stats.Summary.LastWeekPlayMs);
        Assert.Equal(0, stats.Summary.TodayPlayMs);
        Assert.Equal(4, stats.Summary.TotalSessions);
        Assert.Equal(3, stats.Summary.WeekSessions);
        Assert.Equal(4, stats.Summary.ActiveDays);
        Assert.Equal(2, stats.Summary.StreakDays);
        Assert.Equal(2, stats.Summary.InstanceCount);
        Assert.Equal(3, stats.Summary.ModCount);
        Assert.Equal(1, stats.Summary.RunningSessions);

        var top = Assert.Single(stats.TopInstances, item => item.InstanceId == "a");
        Assert.Equal(10_800_000, top.PlayMs);
        Assert.Equal(3, top.Sessions);
        // The newest snapshot wins after a rename.
        Assert.Equal("Alpha", top.Name);
        Assert.False(top.Running);

        Assert.Equal(1, stats.Hourly[10].Sessions);
        Assert.Equal(3_600_000, stats.Hourly[10].PlayMs);
        Assert.Equal(1_800_000, stats.Hourly[11].PlayMs);
        Assert.Equal(3_600_000, stats.Hourly[20].PlayMs);
        Assert.Equal(1_800_000, stats.Hourly[21].PlayMs);
        Assert.Equal(1, stats.Hourly[9].Sessions);
        Assert.Equal(0, stats.Hourly[9].PlayMs);

        Assert.Equal(90, stats.Daily.Count);
        Assert.Equal(new DateOnly(2026, 11, 5), stats.Daily[^1].Day);
        var today = stats.Daily[^1];
        Assert.Equal(0, today.PlayMs);
        Assert.Equal(1, today.Sessions);

        Assert.Equal("b", stats.RecentSessions[0].InstanceId);
        Assert.Null(stats.RecentSessions[0].EndedAt);
        Assert.Equal("a", stats.RecentSessions[1].InstanceId);

        Assert.Equal(
            ["fabric", "vanilla"],
            stats.Loaders.Select(item => item.Loader));
    }

    [Fact]
    public void Concurrent_sessions_count_the_wall_clock_union_once()
    {
        var now = new DateTimeOffset(2026, 11, 5, 14, 0, 0, TimeSpan.FromHours(8));
        var sessions = new[]
        {
            // 10:00–12:00
            Session(
                "s1", "a", "Alpha", "fabric", "1.21.4",
                Days(2026, 11, 5, 10), 120, "2026-11-05", 10),
            // 11:00–13:00, overlapping the first by one hour.
            Session(
                "s2", "b", "Beta", "vanilla", "1.20.1",
                Days(2026, 11, 5, 11), 120, "2026-11-05", 11),
        };

        var stats = PlayStatsBuilder.Build(sessions, [], now);

        // Wall-clock union 10:00–13:00 = 3h, not 2h + 2h = 4h.
        Assert.Equal(3 * 3_600_000, stats.Summary.TotalPlayMs);
        Assert.Equal(3 * 3_600_000, stats.Summary.TodayPlayMs);
        Assert.Equal(3 * 3_600_000, stats.Summary.WeekPlayMs);
        Assert.Equal(3 * 3_600_000, stats.Daily[^1].PlayMs);

        Assert.Equal(3_600_000, stats.Hourly[10].PlayMs);
        Assert.Equal(3_600_000, stats.Hourly[11].PlayMs);
        Assert.Equal(3_600_000, stats.Hourly[12].PlayMs);
        Assert.Equal(0, stats.Hourly[13].PlayMs);

        // Per-instance totals still describe each instance's own runtime.
        Assert.Equal(2 * 3_600_000, stats.TopInstances[0].PlayMs);
        Assert.Equal(2 * 3_600_000, stats.TopInstances[1].PlayMs);
    }

    [Fact]
    public void A_session_across_midnight_counts_time_on_both_days_but_one_launch()
    {
        var sessions = new[]
        {
            // 2026-11-04 23:00 → 2026-11-05 01:00 (+08).
            Session(
                "s1", "a", "Alpha", "fabric", "1.21.4",
                Days(2026, 11, 4, 23), 120, "2026-11-04", 23),
        };

        var stats = PlayStatsBuilder.Build(sessions, [], Now);

        Assert.Equal(2 * 3_600_000, stats.Summary.TotalPlayMs);
        Assert.Equal(3_600_000, stats.Summary.TodayPlayMs);
        Assert.Equal(2, stats.Summary.ActiveDays);
        Assert.Equal(2, stats.Summary.StreakDays);

        var firstDay = stats.Daily.Single(
            item => item.Day == new DateOnly(2026, 11, 4));
        var secondDay = stats.Daily.Single(
            item => item.Day == new DateOnly(2026, 11, 5));
        Assert.Equal(3_600_000, firstDay.PlayMs);
        Assert.Equal(1, firstDay.Sessions);
        Assert.Equal(3_600_000, secondDay.PlayMs);
        // The launch happened on the 4th; the 5th only receives playtime.
        Assert.Equal(0, secondDay.Sessions);

        Assert.Equal(3_600_000, stats.Hourly[23].PlayMs);
        Assert.Equal(3_600_000, stats.Hourly[0].PlayMs);
    }

    [Fact]
    public void Streak_counts_from_yesterday_when_today_is_idle()
    {
        var sessions = new[]
        {
            Session(
                "s1", "a", "Alpha", "fabric", "1.21.4",
                Days(2026, 11, 4, 10), 30, "2026-11-04", 10),
            Session(
                "s2", "a", "Alpha", "fabric", "1.21.4",
                Days(2026, 11, 3, 10), 30, "2026-11-03", 10),
        };

        var stats = PlayStatsBuilder.Build(sessions, [], Now);

        Assert.Equal(2, stats.Summary.StreakDays);
        Assert.Equal(0, stats.Summary.TodayPlayMs);
    }

    [Fact]
    public void Empty_history_still_reports_the_current_instance_mix()
    {
        var instances = new[]
        {
            Instance("a", "Alpha", "fabric", 3),
            Instance("b", "Beta", "fabric", 1),
        };

        var stats = PlayStatsBuilder.Build([], instances, Now);

        Assert.Equal(0, stats.Summary.TotalSessions);
        Assert.Equal(0, stats.Summary.StreakDays);
        Assert.Equal(2, stats.Summary.InstanceCount);
        Assert.Equal(4, stats.Summary.ModCount);
        Assert.Empty(stats.TopInstances);
        Assert.Empty(stats.RecentSessions);
        Assert.Equal(2, stats.Loaders[0].Instances);
        Assert.Equal(90, stats.Daily.Count);
        Assert.All(stats.Daily, day => Assert.Equal(0, day.Sessions));
    }

    private static PlaySessionRow Session(
        string id,
        string instanceId,
        string name,
        string loader,
        string gameVersion,
        DateTimeOffset startedAt,
        int minutes,
        string day,
        int hour)
        => new(
            id,
            instanceId,
            name,
            loader,
            gameVersion,
            startedAt,
            startedAt.AddMinutes(minutes),
            minutes * 60_000L,
            day,
            hour,
            0);

    private static DateTimeOffset Days(int year, int month, int day, int hour)
        => new(year, month, day, hour, 0, 0, TimeSpan.FromHours(8));

    private static GameInstanceRecord Instance(
        string id,
        string name,
        string loader,
        int mods)
        => new(
            id,
            name,
            "1.21.4",
            loader,
            string.Empty,
            $"/tmp/{id}",
            mods,
            false,
            Now,
            Now,
            null);
}
