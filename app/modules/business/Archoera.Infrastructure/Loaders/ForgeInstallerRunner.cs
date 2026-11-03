using System.Diagnostics;

namespace Archoera.Infrastructure.Loaders;

/// <summary>Result of one official Forge/NeoForge installer run.</summary>
public sealed record ForgeInstallerRunResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut);

/// <summary>
/// Runs the official Forge/NeoForge installer with <c>--installClient</c>.
///
/// Pure Java mode delegates the whole client install to the vendor installer:
/// it downloads libraries, runs its own processors and writes
/// <c>versions/&lt;id&gt;/&lt;id&gt;.json</c>. The module only prepares the
/// launcher profile file and imports the generated version afterwards.
/// </summary>
public sealed class ForgeInstallerRunner
{
    private const int MaxCapturedCharacters = 64 * 1024;

    /// <summary>
    /// True when the installer supports the client CLI. Pre-1.7 installers
    /// only expose <c>--installServer</c>/<c>--extract</c> and have no
    /// <c>version.json</c>, so pure Java mode cannot drive them.
    /// </summary>
    public static bool SupportsClientInstall(string installerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);
        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(installerPath);
            return archive.Entries.Any(entry =>
                string.Equals(entry.FullName, "version.json", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public async Task<ForgeInstallerRunResult> RunAsync(
        string javaPath,
        string installerPath,
        string root,
        Uri? mirror,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(javaPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        cancellationToken.ThrowIfCancellationRequested();

        if (timeout <= TimeSpan.Zero)
        {
            timeout = TimeSpan.FromMinutes(30);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = javaPath,
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // Inherited tool options can inject agents/classpath entries and break
        // the installer's own processor classpath.
        startInfo.Environment.Remove("JAVA_TOOL_OPTIONS");
        startInfo.Environment.Remove("_JAVA_OPTIONS");
        startInfo.Environment.Remove("JDK_JAVA_OPTIONS");
        startInfo.Environment.Remove("CLASSPATH");

        startInfo.ArgumentList.Add("-jar");
        startInfo.ArgumentList.Add(installerPath);
        startInfo.ArgumentList.Add("--installClient");
        startInfo.ArgumentList.Add(root);
        if (mirror is not null)
        {
            startInfo.ArgumentList.Add("--mirror");
            startInfo.ArgumentList.Add(mirror.AbsoluteUri);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return new ForgeInstallerRunResult(
                    -1,
                    string.Empty,
                    "installer process did not start",
                    TimedOut: false);
            }
        }
        catch (Exception exception)
        {
            return new ForgeInstallerRunResult(
                -1,
                string.Empty,
                exception.Message,
                TimedOut: false);
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

        var (standardOutput, standardError) = await DrainAsync(stdoutTask, stderrTask)
            .ConfigureAwait(false);

        return new ForgeInstallerRunResult(
            timedOut ? -1 : process.ExitCode,
            Truncate(standardOutput),
            timedOut
                ? Truncate(string.IsNullOrEmpty(standardError)
                    ? "installer timed out"
                    : $"{standardError}\ninstaller timed out")
                : Truncate(standardError),
            timedOut);
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
        => value.Length <= MaxCapturedCharacters
            ? value
            : value[^MaxCapturedCharacters..];
}
