using System.Diagnostics;

namespace Archoera.Infrastructure.Launch;

/// <summary>
/// Tracks the game JVMs started by <see cref="LaunchModuleService"/>.
///
/// The launcher UI never inspects processes itself, so the module keeps the
/// pid and log path of the last launch per instance. That lets the log viewer
/// know whether an instance is still running (and therefore whether a log can
/// still grow) and lets it pick the active log file by default.
/// </summary>
internal static class LaunchProcessRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Running =
        new(StringComparer.Ordinal);

    internal sealed record Entry(int Pid, string LogPath, DateTimeOffset StartedAt);

    public static void Register(string instanceId, int pid, string logPath)
    {
        lock (Gate)
        {
            Running[instanceId] = new Entry(pid, logPath, DateTimeOffset.UtcNow);
        }
    }

    public static void Unregister(string instanceId, int pid)
    {
        lock (Gate)
        {
            if (Running.TryGetValue(instanceId, out var entry) && entry.Pid == pid)
            {
                Running.Remove(instanceId);
            }
        }
    }

    /// <summary>The live entry for an instance, or null when it exited.</summary>
    public static Entry? TryGet(string instanceId)
    {
        lock (Gate)
        {
            if (!Running.TryGetValue(instanceId, out var entry)) return null;
            if (!IsAlive(entry.Pid))
            {
                Running.Remove(instanceId);
                return null;
            }

            return entry;
        }
    }

    public static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
