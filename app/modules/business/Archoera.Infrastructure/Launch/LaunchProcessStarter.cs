using System.Diagnostics;
using Archoera.Domain.Settings;

namespace Archoera.Infrastructure.Launch;

/// <summary>
/// Spawns the JVM for a built launch plan: log rotation, argument/env wiring
/// (with hostile JVM env vars stripped) and process registry bookkeeping.
///
/// <para>A wrapper command is placed before the Java executable on the same
/// command line, not around a shell: the wrapper receives the Java path and
/// every JVM/game argument as ordinary arguments. The registered pid is the
/// wrapper process, and stopping kills its whole tree.</para>
/// </summary>
internal static class LaunchProcessStarter
{
    internal static (Process Process, string LogPath) Start(
        string instanceId,
        LaunchPlan plan,
        int logRetentionFiles,
        int logRetentionMegabytes,
        Action<int>? onExited = null)
    {
        LaunchLogFiles.Prepare(
            plan.LogDirectory,
            logRetentionFiles,
            logRetentionMegabytes * 1024L * 1024L);
        var startInfo = CreateStartInfo(plan);

        var logPath = Path.Combine(
            plan.LogDirectory,
            $"launch-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.log");
        var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new LaunchException("LAUNCH_FAILED", "JVM did not start");
        }

        var pid = process.Id;
        var log = new LaunchLogWriter(logPath);
        log.WriteLine($"[archoera] java: {plan.JavaPath}");
        if (plan.Environment.Count > 0)
        {
            // Keys only: values may contain user-provided paths or credentials.
            log.WriteLine(
                "[archoera] env: " +
                string.Join(
                    ", ",
                    plan.Environment.Keys.OrderBy(key => key, StringComparer.Ordinal)));
        }
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null) log.WriteLine(args.Data);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null) log.WriteLine(args.Data);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            // WaitForExit() also drains the asynchronous output handlers, so the
            // exit marker is the last line instead of racing the remaining
            // output.
            try
            {
                process.WaitForExit();
            }
            catch (InvalidOperationException)
            {
                // Already reaped elsewhere.
            }

            log.Complete(process.ExitCode);

            // The log viewer polls the registry to know when a follow can end.
            LaunchProcessRegistry.Unregister(instanceId, pid);

            // Launcher-side post-exit hook. It must never block or break the
            // JVM exit handler; the runner handles its own failures/logging.
            if (onExited is not null)
            {
                try
                {
                    onExited(process.ExitCode);
                }
                catch
                {
                    // Best effort: a hook failure cannot unwind the exit event.
                }
            }
        };
        LaunchProcessRegistry.Register(instanceId, pid, logPath);
        return (process, logPath);
    }

    /// <summary>
    /// Builds the child process description without starting it. Kept separate
    /// so argument order and environment stripping can be asserted in tests.
    /// </summary>
    internal static ProcessStartInfo CreateStartInfo(LaunchPlan plan)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = plan.Wrapper.Count > 0 ? plan.Wrapper[0] : plan.JavaPath,
            WorkingDirectory = plan.GameDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        if (plan.Wrapper.Count > 0)
        {
            for (var index = 1; index < plan.Wrapper.Count; index++)
            {
                startInfo.ArgumentList.Add(plan.Wrapper[index]);
            }

            // The wrapper becomes the executable, so Java must be passed as
            // its first argument. Without a wrapper the Java path is already
            // FileName and must not be repeated: doing so makes the JVM treat
            // its own path as the main class (ClassNotFoundException).
            startInfo.ArgumentList.Add(plan.JavaPath);
        }

        foreach (var argument in plan.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        RemoveHostileEnvironment(startInfo);
        foreach (var pair in plan.Environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        // Strip again after applying the plan. The plan builder already filters
        // hostile names, but this guarantees the final child environment even
        // for a hand-built plan or a legacy settings file.
        RemoveHostileEnvironment(startInfo);
        return startInfo;
    }

    private static void RemoveHostileEnvironment(ProcessStartInfo startInfo)
    {
        foreach (var name in LaunchEnvironmentRules.HostileNames)
        {
            startInfo.Environment.Remove(name);
        }
    }
}
