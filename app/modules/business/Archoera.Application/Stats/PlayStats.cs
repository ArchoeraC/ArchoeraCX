using Archoera.Domain.Instances;

namespace Archoera.Application.Stats;

/// <summary>Headline numbers for the statistics overview cards.</summary>
public sealed record PlayStatsSummary(
    long TotalPlayMs,
    long TodayPlayMs,
    long WeekPlayMs,
    long LastWeekPlayMs,
    int TotalSessions,
    int WeekSessions,
    int ActiveDays,
    int StreakDays,
    int InstanceCount,
    int ModCount,
    DateTimeOffset? FirstPlayedAt,
    DateTimeOffset? LastPlayedAt,
    int RunningSessions);

/// <summary>One day of the activity heatmap. <see cref="Day"/> is local time.</summary>
public sealed record DailyPlayStat(DateOnly Day, long PlayMs, int Sessions);

/// <summary>One hour of the all-time playtime distribution (local time).</summary>
public sealed record HourlyPlayStat(int Hour, long PlayMs, int Sessions);

/// <summary>Aggregated playtime for one instance.</summary>
public sealed record InstancePlayStat(
    string InstanceId,
    string Name,
    string Loader,
    string GameVersion,
    long PlayMs,
    int Sessions,
    DateTimeOffset LastPlayedAt,
    bool Running);

/// <summary>One recently started session.</summary>
public sealed record RecentPlaySession(
    string InstanceId,
    string Name,
    string Loader,
    string GameVersion,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    long DurationMs,
    int? ExitCode);

/// <summary>Current instance count per loader family.</summary>
public sealed record LoaderInstanceStat(string Loader, int Instances);

/// <summary>Everything the statistics page renders, built from one snapshot.</summary>
public sealed record PlayStats(
    DateTimeOffset GeneratedAt,
    PlayStatsSummary Summary,
    IReadOnlyList<DailyPlayStat> Daily,
    IReadOnlyList<HourlyPlayStat> Hourly,
    IReadOnlyList<InstancePlayStat> TopInstances,
    IReadOnlyList<RecentPlaySession> RecentSessions,
    IReadOnlyList<LoaderInstanceStat> Loaders);

/// <summary>
/// Pure aggregation of play sessions and the current instance index. Kept in
/// the Application layer so the statistics rules are testable without SQLite.
/// </summary>
public static class PlayStatsBuilder
{
    public static PlayStats Build(
        IReadOnlyList<PlaySessionRow> sessions,
        IReadOnlyList<GameInstanceRecord> instances,
        DateTimeOffset now,
        int dailyDays = 90,
        int topInstanceLimit = 10,
        int recentLimit = 8)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentOutOfRangeException.ThrowIfLessThan(dailyDays, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(topInstanceLimit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(recentLimit, 1);

        var offset = now.Offset;
        var today = DateOnly.FromDateTime(now.DateTime);
        var weekStartDate = today.AddDays(-(((int)now.DayOfWeek + 6) % 7));
        var weekStart = new DateTimeOffset(
            weekStartDate.ToDateTime(TimeOnly.MinValue),
            offset);
        var weekStartMs = weekStart.ToUnixTimeMilliseconds();
        var weekEndMs = weekStart.AddDays(7).ToUnixTimeMilliseconds();
        var lastWeekStartMs = weekStart.AddDays(-7).ToUnixTimeMilliseconds();
        var todayStartMs = new DateTimeOffset(
            today.ToDateTime(TimeOnly.MinValue),
            offset).ToUnixTimeMilliseconds();
        var tomorrowStartMs = new DateTimeOffset(
            today.AddDays(1).ToDateTime(TimeOnly.MinValue),
            offset).ToUnixTimeMilliseconds();

        long? firstStartedMs = null;
        long? lastStartedMs = null;
        var totalSessions = 0;
        var weekSessions = 0;
        var runningSessions = 0;

        var activeDays = new HashSet<string>(StringComparer.Ordinal);
        var dailySessions = new Dictionary<string, int>(StringComparer.Ordinal);
        var hourlySessions = new int[24];
        var intervals = new List<Interval>(sessions.Count);
        var perInstance = new Dictionary<string, InstanceTotals>(StringComparer.Ordinal);
        var perInstanceIntervals =
            new Dictionary<string, List<Interval>>(StringComparer.Ordinal);

        foreach (var session in sessions)
        {
            var startedMs = session.StartedAt.ToUnixTimeMilliseconds();
            var endedMs = session.EndedAt?.ToUnixTimeMilliseconds() ?? startedMs;
            if (endedMs < startedMs) endedMs = startedMs;

            // The raw interval, not the stored per-session duration: merging
            // these is what prevents two concurrent instances from counting
            // the same wall-clock minute twice.
            var interval = new Interval(startedMs, endedMs);
            intervals.Add(interval);

            var localStart = session.StartedAt.ToOffset(offset);
            var startedDay = DayKey(DateOnly.FromDateTime(localStart.DateTime));

            totalSessions++;
            if (startedMs >= weekStartMs)
            {
                weekSessions++;
            }

            if (session.EndedAt is null) runningSessions++;
            if (firstStartedMs is null || startedMs < firstStartedMs) firstStartedMs = startedMs;
            if (lastStartedMs is null || startedMs > lastStartedMs) lastStartedMs = startedMs;

            activeDays.Add(startedDay);
            dailySessions[startedDay] = dailySessions.GetValueOrDefault(startedDay) + 1;
            hourlySessions[localStart.Hour]++;

            if (!perInstance.TryGetValue(session.InstanceId, out var totals))
            {
                totals = new InstanceTotals(session.InstanceId);
                perInstance[session.InstanceId] = totals;
                perInstanceIntervals[session.InstanceId] = [];
            }

            perInstanceIntervals[session.InstanceId].Add(interval);
            totals.Add(session);
        }

        // One wall-clock timeline: overlapping launches contribute their union,
        // not the sum of their durations.
        var totalPlayMs = UnionDurationMs(intervals);
        var todayPlayMs = UnionDurationMs(intervals, todayStartMs, tomorrowStartMs);
        var weekPlayMs = UnionDurationMs(intervals, weekStartMs, weekEndMs);
        var lastWeekPlayMs = UnionDurationMs(intervals, lastWeekStartMs, weekStartMs);

        var dailyPlayMs = BuildDailyPlayMs(intervals, offset);
        var hourlyPlayMs = BuildHourlyPlayMs(intervals, offset);

        // A session that runs past midnight keeps the player active on both
        // days even though only the launch day got a session count.
        foreach (var (day, playMs) in dailyPlayMs)
        {
            if (playMs > 0) activeDays.Add(day);
        }

        var dailyStats = new List<DailyPlayStat>(dailyDays);
        var dailyStart = today.AddDays(-(dailyDays - 1));
        for (var dayOffset = 0; dayOffset < dailyDays; dayOffset++)
        {
            var day = dailyStart.AddDays(dayOffset);
            var key = DayKey(day);
            dailyStats.Add(new DailyPlayStat(
                day,
                dailyPlayMs.GetValueOrDefault(key),
                dailySessions.GetValueOrDefault(key)));
        }

        var hourlyStats = new List<HourlyPlayStat>(24);
        for (var hour = 0; hour < 24; hour++)
        {
            hourlyStats.Add(new HourlyPlayStat(hour, hourlyPlayMs[hour], hourlySessions[hour]));
        }

        var topInstances = perInstance.Values
            .Select(item => item.ToStat(
                UnionDurationMs(perInstanceIntervals[item.InstanceId])))
            .OrderByDescending(item => item.PlayMs)
            .ThenByDescending(item => item.Sessions)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(topInstanceLimit)
            .ToArray();

        var recent = sessions
            .OrderByDescending(item => item.StartedAt)
            .Take(recentLimit)
            .Select(item => new RecentPlaySession(
                item.InstanceId,
                item.InstanceName,
                item.Loader,
                item.GameVersion,
                item.StartedAt,
                item.EndedAt,
                Math.Max(0, item.DurationMs),
                item.ExitCode))
            .ToArray();

        var loaders = instances
            .GroupBy(LoaderKey, StringComparer.Ordinal)
            .Select(group => new LoaderInstanceStat(group.Key, group.Count()))
            .OrderByDescending(item => item.Instances)
            .ThenBy(item => item.Loader, StringComparer.Ordinal)
            .ToArray();

        var summary = new PlayStatsSummary(
            TotalPlayMs: totalPlayMs,
            TodayPlayMs: todayPlayMs,
            WeekPlayMs: weekPlayMs,
            LastWeekPlayMs: lastWeekPlayMs,
            TotalSessions: totalSessions,
            WeekSessions: weekSessions,
            ActiveDays: activeDays.Count,
            StreakDays: ComputeStreak(activeDays, today),
            InstanceCount: instances.Count,
            ModCount: instances.Sum(item => Math.Max(0, item.ModCount)),
            FirstPlayedAt: firstStartedMs is { } first
                ? DateTimeOffset.FromUnixTimeMilliseconds(first)
                : null,
            LastPlayedAt: lastStartedMs is { } last
                ? DateTimeOffset.FromUnixTimeMilliseconds(last)
                : null,
            RunningSessions: runningSessions);

        return new PlayStats(
            now,
            summary,
            dailyStats,
            hourlyStats,
            topInstances,
            recent,
            loaders);
    }

    /// <summary>Consecutive local days ending today (or yesterday when today is idle).</summary>
    private static int ComputeStreak(HashSet<string> activeDays, DateOnly today)
    {
        if (activeDays.Count == 0) return 0;
        var cursor = activeDays.Contains(DayKey(today)) ? today : today.AddDays(-1);
        var streak = 0;
        while (activeDays.Contains(DayKey(cursor)))
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }

        return streak;
    }

    private static string DayKey(DateOnly day) => day.ToString("yyyy-MM-dd");

    private static string LoaderKey(GameInstanceRecord instance)
        => string.IsNullOrWhiteSpace(instance.Loader)
            ? "vanilla"
            : instance.Loader.Trim().ToLowerInvariant();

    private readonly record struct Interval(long StartMs, long EndMs);

    /// <summary>
    /// Sorts and merges overlapping or touching intervals. Zero-length rows
    /// (a launch whose exit was never observed) drop out, so they still count
    /// as a session but never as playtime.
    /// </summary>
    private static IReadOnlyList<Interval> MergeIntervals(
        IEnumerable<Interval> intervals)
    {
        var sorted = intervals
            .Where(item => item.EndMs > item.StartMs)
            .OrderBy(item => item.StartMs)
            .ThenBy(item => item.EndMs)
            .ToList();
        if (sorted.Count == 0) return [];

        var merged = new List<Interval>();
        var start = sorted[0].StartMs;
        var end = sorted[0].EndMs;
        for (var index = 1; index < sorted.Count; index++)
        {
            var next = sorted[index];
            if (next.StartMs <= end)
            {
                if (next.EndMs > end) end = next.EndMs;
                continue;
            }

            merged.Add(new Interval(start, end));
            start = next.StartMs;
            end = next.EndMs;
        }

        merged.Add(new Interval(start, end));
        return merged;
    }

    /// <summary>Union length, optionally clipped to a local-time window.</summary>
    private static long UnionDurationMs(
        IEnumerable<Interval> intervals,
        long? fromMs = null,
        long? toMs = null)
    {
        long total = 0;
        foreach (var interval in MergeIntervals(intervals))
        {
            var start = fromMs is { } from
                ? Math.Max(interval.StartMs, from)
                : interval.StartMs;
            var end = toMs is { } to
                ? Math.Min(interval.EndMs, to)
                : interval.EndMs;
            if (end > start) total += end - start;
        }

        return total;
    }

    private static Dictionary<string, long> BuildDailyPlayMs(
        IEnumerable<Interval> intervals,
        TimeSpan offset)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var interval in MergeIntervals(intervals))
        {
            var cursor = DateTimeOffset
                .FromUnixTimeMilliseconds(interval.StartMs)
                .ToOffset(offset);
            var end = DateTimeOffset
                .FromUnixTimeMilliseconds(interval.EndMs)
                .ToOffset(offset);
            while (cursor < end)
            {
                var nextMidnight = new DateTimeOffset(
                    cursor.Year,
                    cursor.Month,
                    cursor.Day,
                    0,
                    0,
                    0,
                    offset).AddDays(1);
                var segmentEnd = end < nextMidnight ? end : nextMidnight;
                var key = DayKey(DateOnly.FromDateTime(cursor.DateTime));
                result[key] = result.GetValueOrDefault(key) +
                    (long)(segmentEnd - cursor).TotalMilliseconds;
                cursor = segmentEnd;
            }
        }

        return result;
    }

    private static long[] BuildHourlyPlayMs(
        IEnumerable<Interval> intervals,
        TimeSpan offset)
    {
        var result = new long[24];
        foreach (var interval in MergeIntervals(intervals))
        {
            var cursor = DateTimeOffset
                .FromUnixTimeMilliseconds(interval.StartMs)
                .ToOffset(offset);
            var end = DateTimeOffset
                .FromUnixTimeMilliseconds(interval.EndMs)
                .ToOffset(offset);
            while (cursor < end)
            {
                var nextHour = new DateTimeOffset(
                    cursor.Year,
                    cursor.Month,
                    cursor.Day,
                    cursor.Hour,
                    0,
                    0,
                    offset).AddHours(1);
                var segmentEnd = end < nextHour ? end : nextHour;
                result[cursor.Hour] +=
                    (long)(segmentEnd - cursor).TotalMilliseconds;
                cursor = segmentEnd;
            }
        }

        return result;
    }

    private sealed class InstanceTotals
    {
        private string _name = string.Empty;
        private string _loader = string.Empty;
        private string _gameVersion = string.Empty;
        private DateTimeOffset _lastPlayedAt;

        public InstanceTotals(string instanceId)
        {
            InstanceId = instanceId;
        }

        public string InstanceId { get; }

        public void Add(PlaySessionRow session)
        {
            Sessions++;
            Running |= session.EndedAt is null;
            // Sessions can arrive out of order; the newest snapshot wins so a
            // renamed instance shows its current label.
            if (session.StartedAt >= _lastPlayedAt)
            {
                _lastPlayedAt = session.StartedAt;
                _name = session.InstanceName;
                _loader = session.Loader;
                _gameVersion = session.GameVersion;
            }
        }

        public int Sessions { get; private set; }

        public bool Running { get; private set; }

        public InstancePlayStat ToStat(long playMs)
            => new(
                InstanceId,
                _name,
                _loader,
                _gameVersion,
                playMs,
                Sessions,
                _lastPlayedAt,
                Running);
    }
}
