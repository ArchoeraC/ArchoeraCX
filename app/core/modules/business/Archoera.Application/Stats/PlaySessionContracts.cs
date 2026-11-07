namespace Archoera.Application.Stats;

/// <summary>
/// Snapshot of one started game process. The instance fields are copied at
/// launch time so renaming or removing an instance never rewrites history.
/// </summary>
public sealed record PlaySessionStart(
    string Id,
    string InstanceId,
    string InstanceName,
    string Loader,
    string GameVersion,
    DateTimeOffset StartedAt);

/// <summary>
/// One persisted play session. <see cref="DurationMs"/> is zero while the
/// process is still running (or the launcher could not observe its exit).
/// </summary>
public sealed record PlaySessionRow(
    string Id,
    string InstanceId,
    string InstanceName,
    string Loader,
    string GameVersion,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    long DurationMs,
    string StartedDay,
    int StartedHour,
    int? ExitCode);

/// <summary>Storage contract for play sessions. C# owns the SQLite write path.</summary>
public interface IPlaySessionStore
{
    /// <summary>Inserts a running session before the JVM is spawned.</summary>
    Task BeginAsync(
        PlaySessionStart session,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a running session finished. A session that was already completed
    /// is left untouched so a duplicate exit callback cannot double-count.
    /// </summary>
    Task CompleteAsync(
        string sessionId,
        DateTimeOffset endedAt,
        int exitCode,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a session that never reached a running process.</summary>
    Task AbortAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>All sessions ordered by start time (ascending).</summary>
    Task<IReadOnlyList<PlaySessionRow>> ListAsync(
        CancellationToken cancellationToken = default);
}
