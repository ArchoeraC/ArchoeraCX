using System.Globalization;
using System.Text.Json;
using Archoera.Application.Instances;
using Archoera.Application.Modules;
using Archoera.Application.Stats;
using Archoera.Domain.Instances;
using Archoera.Infrastructure.Instances;
using Archoera.Infrastructure.Modules;

namespace Archoera.Infrastructure.Stats;

/// <summary>
/// Statistics facade for the UI. One <c>stats.get</c> snapshot keeps the cards,
/// heatmap and rankings on the same data instead of six racing requests.
/// </summary>
public sealed class StatsModuleService : IModuleMethodHandler
{
    private const string GetMethod = "stats.get";

    /// <summary>
    /// A non-zero exit inside this window is treated as a failed launch, not a
    /// play session: the game never became playable, so neither its few
    /// seconds nor the launch itself belong in the statistics.
    /// </summary>
    private const long FailedLaunchGraceMs = 60_000;

    private static readonly string[] SupportedMethods = [GetMethod];

    private readonly IInstanceRepository _instances;
    private readonly IPlaySessionStore _sessions;
    private readonly Dictionary<string, ModuleMethodDelegate> _handlers;

    public StatsModuleService(
        IInstanceRepository? instances = null,
        IPlaySessionStore? sessions = null,
        string? dataRoot = null)
    {
        _instances = instances ?? new SqliteInstanceRepository(dataRoot);
        _sessions = sessions ?? new SqlitePlaySessionStore(dataRoot);
        _handlers = new Dictionary<string, ModuleMethodDelegate>(StringComparer.Ordinal)
        {
            [GetMethod] = GetAsync,
        };
    }

    public IReadOnlyCollection<string> Methods => SupportedMethods;

    public async Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        try
        {
            return _handlers.TryGetValue(method, out var handler)
                ? await handler(paramsJson, cancellationToken).ConfigureAwait(false)
                : ModuleJson.Error("E_UNSUPPORTED", $"unsupported statistics method '{method}'");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ModuleJson.Error("E_INTERNAL", exception.Message);
        }
    }

    private async Task<byte[]> GetAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {        IReadOnlyList<GameInstanceRecord> instances;
        try
        {
            instances = await _instances.ListAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // The index may be busy while another launch writes it; the play
            // history is still useful on its own.
            instances = [];
        }

        var sessions = await _sessions.ListAsync(cancellationToken).ConfigureAwait(false);
        // A session left open by a previous launcher process has no trustworthy
        // end time and most often means the launcher or JVM died before the
        // game got going; do not turn it into a counted launch or fake time.
        // Live sessions owned by this process stay, and a launch that failed
        // within the startup grace window is dropped as well.
        var normalized = sessions
            .Where(session => session.EndedAt is not null ||
                              PlaySessionTracker.IsActive(session.Id))
            .Where(session => !IsFailedLaunch(session))
            .ToArray();
        var stats = PlayStatsBuilder.Build(normalized, instances, DateTimeOffset.Now);

        return ModuleJson.Envelope(writer => WriteStats(writer, stats));
    }

    private static bool IsFailedLaunch(PlaySessionRow session)
        => session.EndedAt is not null &&
           session.DurationMs < FailedLaunchGraceMs &&
           session.ExitCode is { } exitCode &&
           exitCode != 0;

    private static void WriteStats(Utf8JsonWriter writer, PlayStats stats)
    {
        writer.WriteNumber("generatedAt", stats.GeneratedAt.ToUnixTimeMilliseconds());

        var summary = stats.Summary;
        writer.WriteStartObject("summary");
        writer.WriteNumber("totalPlayMs", summary.TotalPlayMs);
        writer.WriteNumber("todayPlayMs", summary.TodayPlayMs);
        writer.WriteNumber("weekPlayMs", summary.WeekPlayMs);
        writer.WriteNumber("lastWeekPlayMs", summary.LastWeekPlayMs);
        writer.WriteNumber("totalSessions", summary.TotalSessions);
        writer.WriteNumber("weekSessions", summary.WeekSessions);
        writer.WriteNumber("activeDays", summary.ActiveDays);
        writer.WriteNumber("streakDays", summary.StreakDays);
        writer.WriteNumber("instanceCount", summary.InstanceCount);
        writer.WriteNumber("modCount", summary.ModCount);
        WriteTimestamp(writer, "firstPlayedAt", summary.FirstPlayedAt);
        WriteTimestamp(writer, "lastPlayedAt", summary.LastPlayedAt);
        writer.WriteNumber("runningSessions", summary.RunningSessions);
        writer.WriteEndObject();

        writer.WriteStartArray("daily");
        foreach (var day in stats.Daily)
        {
            writer.WriteStartObject();
            writer.WriteString(
                "day",
                day.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.WriteNumber("playMs", day.PlayMs);
            writer.WriteNumber("sessions", day.Sessions);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("hourly");
        foreach (var hour in stats.Hourly)
        {
            writer.WriteStartObject();
            writer.WriteNumber("hour", hour.Hour);
            writer.WriteNumber("playMs", hour.PlayMs);
            writer.WriteNumber("sessions", hour.Sessions);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("topInstances");
        foreach (var instance in stats.TopInstances)
        {
            writer.WriteStartObject();
            writer.WriteString("instanceId", instance.InstanceId);
            writer.WriteString("name", instance.Name);
            writer.WriteString("loader", instance.Loader);
            writer.WriteString("gameVersion", instance.GameVersion);
            writer.WriteNumber("playMs", instance.PlayMs);
            writer.WriteNumber("sessions", instance.Sessions);
            writer.WriteNumber(
                "lastPlayedAt",
                instance.LastPlayedAt.ToUnixTimeMilliseconds());
            writer.WriteBoolean("running", instance.Running);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("recentSessions");
        foreach (var session in stats.RecentSessions)
        {
            writer.WriteStartObject();
            writer.WriteString("instanceId", session.InstanceId);
            writer.WriteString("name", session.Name);
            writer.WriteString("loader", session.Loader);
            writer.WriteString("gameVersion", session.GameVersion);
            writer.WriteNumber(
                "startedAt",
                session.StartedAt.ToUnixTimeMilliseconds());
            if (session.EndedAt is { } endedAt)
            {
                writer.WriteNumber("endedAt", endedAt.ToUnixTimeMilliseconds());
            }
            else
            {
                writer.WriteNull("endedAt");
            }

            writer.WriteNumber("durationMs", session.DurationMs);
            if (session.ExitCode is { } exitCode)
            {
                writer.WriteNumber("exitCode", exitCode);
            }
            else
            {
                writer.WriteNull("exitCode");
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("loaders");
        foreach (var loader in stats.Loaders)
        {
            writer.WriteStartObject();
            writer.WriteString("loader", loader.Loader);
            writer.WriteNumber("instances", loader.Instances);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteTimestamp(
        Utf8JsonWriter writer,
        string name,
        DateTimeOffset? value)
    {
        if (value is { } timestamp)
        {
            writer.WriteNumber(name, timestamp.ToUnixTimeMilliseconds());
        }
        else
        {
            writer.WriteNull(name);
        }
    }
}
