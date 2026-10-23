using System.Diagnostics;

namespace Archoera.Infrastructure.Launch;

/// <summary>
/// Spawns the JVM for a built launch plan: log rotation, argument/env wiring
/// (with hostile JVM env vars stripped) and process registry bookkeeping.
/// </summary>
internal static class LaunchProcessStarter
{
    internal static (Process Process, string LogPath) Start(
        string instanceId,
        LaunchPlan plan,
        int logRetentionFiles,
        int logRetentionMegabytes)
    {
        LaunchLogFiles.Prepare(
            plan.LogDirectory,
            logRetentionFiles,
            logRetentionMegabytes * 1024L * 1024L);
        var startInfo = new ProcessStartInfo
        {
            FileName = plan.JavaPath,
            WorkingDirectory = plan.GameDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in plan.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var name in new[]
                 {
                     "JAVA_TOOL_OPTIONS", "_JAVA_OPTIONS", "JDK_JAVA_OPTIONS", "CLASSPATH",
                 })
        {
            startInfo.Environment.Remove(name);
        }

        // Optimization environment (PRIME offload, DRI_PRIME) is decided by the
        // launch module and applied here so the child JVM inherits it.
        foreach (var pair in plan.Environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

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
        };
        LaunchProcessRegistry.Register(instanceId, pid, logPath);
        return (process, logPath);
    }
}
