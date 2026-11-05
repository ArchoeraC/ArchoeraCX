using System.Collections.Concurrent;

namespace Archoera.Infrastructure.Stats;

/// <summary>
/// In-memory set of sessions owned by this launcher process.
///
/// A row whose process exit was never observed (launcher killed, machine
/// power loss) stays open in SQLite. On the next launch it is stale: the
/// statistics query counts the launch but must not claim the game is still
/// running or invent playtime. The tracker keeps the current process's live
/// sessions distinguishable from those leftovers.
/// </summary>
internal static class PlaySessionTracker
{
    private static readonly ConcurrentDictionary<string, byte> Active =
        new(StringComparer.Ordinal);

    internal static void Activate(string sessionId)
        => Active[sessionId] = 0;

    internal static void Deactivate(string sessionId)
        => Active.TryRemove(sessionId, out _);

    internal static bool IsActive(string sessionId)
        => Active.ContainsKey(sessionId);
}
