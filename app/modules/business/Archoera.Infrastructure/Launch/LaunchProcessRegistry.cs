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
    private static readonly Dictionary<string, TaskCompletionSource<int>> ExitWaiters =
        new(StringComparer.Ordinal);

    internal sealed record Entry(int Pid, string LogPath, DateTimeOffset StartedAt);

    public static void Register(string instanceId, int pid, string logPath)
    {
        lock (Gate)
        {
            Running[instanceId] = new Entry(pid, logPath, DateTimeOffset.UtcNow);
            if (ExitWaiters.Remove(instanceId, out var previous))
            {
                previous.TrySetCanceled();
            }

            ExitWaiters[instanceId] = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>
    /// Waits for the launch currently registered for <paramref name="instanceId"/>.
    /// Returns null when no launch is running, so a late caller can fall back to
    /// the stored analysis instead of hanging.
    /// </summary>
    public static Task<int>? TryWaitForExit(string instanceId)
    {
        lock (Gate)
        {
            return ExitWaiters.TryGetValue(instanceId, out var waiter)
                ? waiter.Task
                : null;
        }
    }

    /// <summary>Completes the waiter created by <see cref="Register"/>.</summary>
    public static void CompleteExit(string instanceId, int pid, int exitCode)
    {
        lock (Gate)
        {
            if (Running.TryGetValue(instanceId, out var entry) && entry.Pid == pid)
            {
                Running.Remove(instanceId);
            }

            if (ExitWaiters.Remove(instanceId, out var waiter))
            {
                waiter.TrySetResult(exitCode);
            }
        }
    }

    /// <summary>Cancels a waiter when the JVM could not be started.</summary>
    public static void CancelExitWait(string instanceId)
    {
        lock (Gate)
        {
            if (ExitWaiters.Remove(instanceId, out var waiter))
            {
                waiter.TrySetCanceled();
            }
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

    /// <summary>
    /// Force-terminates the game process registered for [instanceId].
    ///
    /// The pid always comes from this registry, never from the caller, so the
    /// UI cannot ask the module to kill an arbitrary process. The Minecraft
    /// client has no graceful shutdown channel, therefore this is a hard kill
    /// of the process tree (SIGKILL on Unix, TerminateProcess on Windows).
    /// Returns false when no game is registered or it already exited.
    /// </summary>
    public static bool TryStop(string instanceId, out int pid)
    {
        pid = 0;
        var entry = TryGet(instanceId);
        if (entry is null) return false;
        pid = entry.Pid;

        try
        {
            using var process = Process.GetProcessById(entry.Pid);
            if (process.HasExited)
            {
                Unregister(instanceId, entry.Pid);
                return false;
            }

            process.Kill(entireProcessTree: true);
            // Give the Exited handler a moment to drain the piped output and
            // write the exit marker before the viewer reloads the log.
            process.WaitForExit(5000);
        }
        catch (ArgumentException)
        {
            // The process disappeared between TryGet and GetProcessById.
            Unregister(instanceId, entry.Pid);
            return false;
        }
        catch (InvalidOperationException)
        {
            Unregister(instanceId, entry.Pid);
            return false;
        }

        Unregister(instanceId, entry.Pid);
        return true;
    }
}
