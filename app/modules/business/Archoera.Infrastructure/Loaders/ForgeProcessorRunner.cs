using System.Diagnostics;
using Archoera.Application.Loaders;
using Archoera.Domain.Loaders;

namespace Archoera.Infrastructure.Loaders;

/// <summary>
/// Runs a single Forge/NeoForge processor with an argv list. Never invokes a
/// shell, clears JVM option environment variables, and kills the process tree
/// when the timeout expires or cancellation is requested.
/// </summary>
public sealed class ForgeProcessorRunner : IProcessorRunner
{
    private const int MaxCapturedCharacters = 64 * 1024;

    public async Task<ProcessorRunResult> RunAsync(
        ProcessorStep step,
        string javaPath,
        string workingDir,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentException.ThrowIfNullOrWhiteSpace(javaPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDir);
        cancellationToken.ThrowIfCancellationRequested();

        if (timeout <= TimeSpan.Zero)
        {
            timeout = TimeSpan.FromMinutes(5);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = javaPath,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // Inherited tool options can inject agents/classpath entries and break
        // processor isolation. Remove them instead of trying to out-quote them.
        startInfo.Environment.Remove("JAVA_TOOL_OPTIONS");
        startInfo.Environment.Remove("_JAVA_OPTIONS");
        startInfo.Environment.Remove("JDK_JAVA_OPTIONS");
        startInfo.Environment.Remove("CLASSPATH");

        startInfo.ArgumentList.Add("-cp");
        var classPath = new List<string>(step.ClassPath.Count + 1);
        classPath.Add(step.JarRelativePath);
        classPath.AddRange(step.ClassPath);
        startInfo.ArgumentList.Add(string.Join(Path.PathSeparator, classPath));

        if (string.IsNullOrWhiteSpace(step.MainClass))
        {
            return new ProcessorRunResult(
                -1,
                string.Empty,
                "processor main class is unknown (processor jar missing or has no Main-Class)",
                TimedOut: false);
        }

        startInfo.ArgumentList.Add(step.MainClass);
        foreach (var argument in step.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return new ProcessorRunResult(-1, string.Empty, "process did not start", false);
            }
        }
        catch (Exception exception)
        {
            return new ProcessorRunResult(-1, string.Empty, exception.Message, false);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        var timedOut = false;
        using var waitSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        waitSource.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(waitSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                KillTree(process);
                await DrainAsync(stdoutTask, stderrTask).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            timedOut = true;
            KillTree(process);
        }
        catch
        {
            KillTree(process);
            await DrainAsync(stdoutTask, stderrTask).ConfigureAwait(false);
            throw;
        }

        var (standardOutput, standardError) = await DrainAsync(stdoutTask, stderrTask).ConfigureAwait(false);

        if (timedOut)
        {
            return new ProcessorRunResult(
                -1,
                standardOutput,
                string.IsNullOrEmpty(standardError)
                    ? "processor timed out"
                    : $"{standardError}\nprocessor timed out",
                TimedOut: true);
        }

        return new ProcessorRunResult(
            process.ExitCode,
            Truncate(standardOutput),
            Truncate(standardError),
            TimedOut: false);
    }

    private static async Task<(string StandardOutput, string StandardError)> DrainAsync(
        Task<string> stdout,
        Task<string> stderr)
    {
        string output;
        string error;

        try
        {
            output = await stdout.ConfigureAwait(false);
        }
        catch
        {
            output = string.Empty;
        }

        try
        {
            error = await stderr.ConfigureAwait(false);
        }
        catch
        {
            error = string.Empty;
        }

        return (Truncate(output), Truncate(error));
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort: the process may have exited between the check and kill.
        }

        try
        {
            process.WaitForExit(2000);
        }
        catch
        {
            // Best effort.
        }
    }

    private static string Truncate(string value)
    {
        if (value.Length <= MaxCapturedCharacters)
        {
            return value;
        }

        return value[^MaxCapturedCharacters..];
    }
}
