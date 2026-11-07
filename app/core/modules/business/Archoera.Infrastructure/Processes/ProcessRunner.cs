using System.Diagnostics;

namespace Archoera.Infrastructure.Processes;

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// Minimal argv-only process runner. Never uses a shell and always bounds the wait.
/// </summary>
internal static class ProcessRunner
{
    public static ProcessResult? Run(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = SafeWorkingDirectory(),
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return null;
            }

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit((int)Math.Max(1, timeout.TotalMilliseconds)))
            {
                TryKill(process);
                Observe(stdout);
                Observe(stderr);
                return null;
            }

            // Output is normally complete once the process exits. Wait briefly for
            // the pipe readers so a slow flush does not lose the version banner.
            try
            {
                Task.WaitAll([stdout, stderr], 2000);
            }
            catch
            {
                // A killed/failed child can fault the readers; treat it as no output.
            }

            var standardOutput = stdout.IsCompletedSuccessfully ? stdout.Result : string.Empty;
            var standardError = stderr.IsCompletedSuccessfully ? stderr.Result : string.Empty;
            return new ProcessResult(process.ExitCode, standardOutput, standardError);
        }
        catch
        {
            // Missing executable, permission denied, invalid binary and similar
            // failures are all "not a usable Java runtime" for this scan.
            return null;
        }
    }

    private static string SafeWorkingDirectory()
    {
        var baseDirectory = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(baseDirectory) && Directory.Exists(baseDirectory))
        {
            return baseDirectory;
        }

        var temp = Path.GetTempPath();
        return Directory.Exists(temp) ? temp : string.Empty;
    }

    private static void TryKill(Process process)
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
            // Best effort.
        }

        try
        {
            process.WaitForExit(1000);
        }
        catch
        {
            // Best effort.
        }
    }

    private static void Observe(Task task)
    {
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
