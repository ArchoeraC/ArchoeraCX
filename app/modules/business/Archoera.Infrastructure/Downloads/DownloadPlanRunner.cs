using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Archoera.Application.Downloads;
using Archoera.Domain.Settings;
using Archoera.Infrastructure.Settings;
using Archoera.Application.Modules;
using Archoera.Domain.Downloads;

namespace Archoera.Infrastructure.Downloads;

/// <summary>Raw result of one <c>archoera-dl</c> invocation.</summary>
public sealed record PlanExecutionResult(
    string Status,
    bool Ok,
    string PlanPath,
    string Stdout,
    string Stderr,
    int FailedCount,
    string? FailedSummary = null)
{
    public bool Completed => string.Equals(Status, "completed", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Raised when the Rust executor cannot be located or started.</summary>
public sealed class DownloadRunnerException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Writes a validated plan next to the instance, invokes the Rust
/// <c>archoera-dl</c> CLI and streams its progress lines into
/// <see cref="ModuleProgress"/>. C# never downloads game bytes itself.
///
/// This is the single process bridge for <c>download.execute</c> and pack
/// installs; the executor still validates the plan independently.
/// </summary>
public interface IDownloadPlanRunner
{
    bool IsAvailable { get; }
    Task<PlanExecutionResult> ExecuteAsync(
        DownloadPlan plan,
        CancellationToken cancellationToken = default);
}

public sealed class DownloadPlanRunner : IDownloadPlanRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(120);

    private readonly string _downloaderPath;

    public DownloadPlanRunner(string? downloaderPath = null)
    {
        _downloaderPath = string.IsNullOrWhiteSpace(downloaderPath)
            ? ResolveDownloaderPath()
            : downloaderPath;
    }

    public string DownloaderPath => _downloaderPath;

    public bool IsAvailable =>
        !string.IsNullOrWhiteSpace(_downloaderPath) && File.Exists(_downloaderPath);

    public async Task<PlanExecutionResult> ExecuteAsync(
        DownloadPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!IsAvailable)
        {
            throw new DownloadRunnerException(
                "DOWNLOADER_MISSING",
                "archoera-dl was not found; build app/modules/downloader and set ARCHOERACX_DL_BIN");
        }

        // User tuning wins over producer defaults (docs/10 §9.4).
        DownloadProxyOptions? manualProxy = null;
        try
        {
            var settings = await new JsonAppSettingsStore()
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false);
            plan = ApplyTuning(plan, settings);
            manualProxy = DownloadProxyResolver.FromSettings(settings.DownloadProxy);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Keep the producer defaults when settings cannot be read.
        }

        // The host resolves the proxy; the executor never reads environment
        // proxies itself, so both HTTP stacks route identically.
        if (plan.Options.Proxy is null)
        {
            var proxy = manualProxy ?? DownloadProxyResolver.Current;
            if (proxy is not null)
            {
                plan = plan with { Options = plan.Options with { Proxy = proxy } };
            }
        }

        var planJson = DownloadPlanJson.Build(plan);
        var planDirectory = Path.Combine(plan.Root, ".archoera", "plans");
        Directory.CreateDirectory(planDirectory);
        var planPath = Path.Combine(
            planDirectory,
            $"plan-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-" +
            $"{SafePlanId(plan.PlanId)}.json");
        await File.WriteAllBytesAsync(planPath, planJson, cancellationToken)
            .ConfigureAwait(false);

        var startInfo = new ProcessStartInfo
        {
            FileName = _downloaderPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(_downloaderPath)
                ?? AppContext.BaseDirectory,
        };
        startInfo.ArgumentList.Add(planPath);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new DownloadRunnerException(
                "DOWNLOADER_FAILED",
                "archoera-dl did not start");
        }

        var startedAt = Stopwatch.GetTimestamp();
        var lastProgressAt = startedAt;
        long lastDone = 0;
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        var stdoutTask = PumpLinesAsync(
            process.StandardOutput,
            line =>
            {
                if (stdout.Length < 256 * 1024) stdout.AppendLine(line);
            });
        var stderrTask = PumpLinesAsync(
            process.StandardError,
            line =>
            {
                if (stderr.Length < 64 * 1024) stderr.AppendLine(line);
                if (!TryParseProgress(line, out var done, out var total, out var current))
                {
                    return;
                }

                var now = Stopwatch.GetTimestamp();
                var elapsed = Stopwatch.GetElapsedTime(lastProgressAt, now).TotalSeconds;
                var rate = elapsed > 0
                    ? Math.Max(0, (long)((done - lastDone) / elapsed))
                    : 0;
                lastDone = Math.Max(lastDone, done);
                lastProgressAt = now;
                ModuleProgress.Report(done, total, rate, current);
            });

        using var waitSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        waitSource.CancelAfter(Timeout);
        try
        {
            await process.WaitForExitAsync(waitSource.Token).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch
            {
                // best effort
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new DownloadRunnerException(
                "DOWNLOAD_TIMEOUT",
                "download timed out");
        }

        var stdoutText = stdout.ToString();
        var stderrText = stderr.ToString();
        var resultText = LastJsonLine(stdoutText);
        if (resultText is null)
        {
            return new PlanExecutionResult(
                "failed",
                false,
                planPath,
                Tail(stdoutText, 4000),
                Tail(stderrText, 500),
                -1);
        }

        using var resultDocument = JsonDocument.Parse(resultText);
        var result = resultDocument.RootElement;
        var status = result.TryGetProperty("status", out var statusElement)
            ? statusElement.GetString() ?? "failed"
            : "failed";
        var failedCount = 0;
        string? failedSummary = null;
        if (result.TryGetProperty("failed", out var failedElement) &&
            failedElement.ValueKind == JsonValueKind.Array)
        {
            failedCount = failedElement.GetArrayLength();
            if (failedCount > 0 && failedElement[0].ValueKind == JsonValueKind.Object)
            {
                var first = failedElement[0];
                var dest = first.TryGetProperty("dest", out var destElement)
                    ? destElement.GetString()
                    : null;
                var code = first.TryGetProperty("code", out var codeElement)
                    ? codeElement.GetString()
                    : null;
                var message = first.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : null;
                failedSummary = $"{dest} ({code}): {message}";
            }
        }

        return new PlanExecutionResult(
            status,
            string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase),
            planPath,
            Tail(stdoutText, 4000),
            Tail(stderrText, 4000),
            failedCount,
            failedSummary);
    }

    /// <summary>
    /// Locates the Rust executor. Explicit env override wins; packaged and
    /// development locations are probed next.
    /// </summary>
    /// <summary>
    /// Applies the user's download tuning (concurrency, per-host cap) to a
    /// plan that was produced elsewhere. Plans stay declarative; the runner is
    /// the single place where runtime preferences win over producer defaults.
    /// </summary>
    public static DownloadPlan ApplyTuning(DownloadPlan plan, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(settings);
        var concurrency = Math.Clamp(
            settings.DownloadConcurrency,
            DownloadPlanLimits.MinConcurrency,
            DownloadPlanLimits.MaxConcurrency);
        var perHost = Math.Clamp(
            settings.DownloadPerHost,
            0,
            DownloadPlanLimits.MaxPerHostConcurrency);
        return plan with
        {
            Concurrency = concurrency,
            Options = plan.Options with { PerHostConcurrency = perHost },
        };
    }

    public static string ResolveDownloaderPath()
    {
        var configured = Environment.GetEnvironmentVariable("ARCHOERACX_DL_BIN");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        var names = OperatingSystem.IsWindows()
            ? new[] { "archoera-dl.exe", "archoera-dl" }
            : new[] { "archoera-dl" };
        var bases = new List<string>
        {
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "lib"),
            Path.Combine(AppContext.BaseDirectory, ".."),
            Path.Combine(AppContext.BaseDirectory, "..", "lib"),
        };

        var processDirectory = Environment.ProcessPath is { } processPath
            ? Path.GetDirectoryName(processPath)
            : null;
        if (!string.IsNullOrWhiteSpace(processDirectory))
        {
            bases.Add(processDirectory);
            bases.Add(Path.Combine(processDirectory, "lib"));
        }

        string? cwd = null;
        try
        {
            cwd = Environment.CurrentDirectory;
        }
        catch
        {
            // Deleted/replaced working directory; packaged paths still work.
        }

        if (!string.IsNullOrWhiteSpace(cwd))
        {
            bases.Add(Path.Combine(cwd, "build", "native"));
            bases.Add(Path.Combine(cwd, "app", "build", "native"));
            bases.Add(Path.Combine(cwd, "app", "modules", "downloader", "target", "release"));
            bases.Add(Path.Combine(cwd, "modules", "downloader", "target", "release"));
        }

        foreach (var baseDirectory in bases)
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(baseDirectory, name);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return string.Empty;
    }

    private static async Task PumpLinesAsync(
        StreamReader reader,
        Action<string> onLine)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            onLine(line);
        }
    }

    private static bool TryParseProgress(
        string line,
        out long done,
        out long total,
        out string? current)
    {
        done = 0;
        total = 0;
        current = null;
        if (line.Length == 0 || line[0] != '{') return false;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("event", out var eventElement) ||
                eventElement.ValueKind != JsonValueKind.String ||
                !string.Equals(
                    eventElement.GetString(),
                    "progress",
                    StringComparison.Ordinal))
            {
                return false;
            }

            if (!root.TryGetProperty("doneBytes", out var doneElement) ||
                !doneElement.TryGetInt64(out done) ||
                !root.TryGetProperty("totalBytes", out var totalElement) ||
                !totalElement.TryGetInt64(out total))
            {
                return false;
            }

            if (root.TryGetProperty("current", out var currentElement) &&
                currentElement.ValueKind == JsonValueKind.String)
            {
                current = currentElement.GetString();
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? LastJsonLine(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            var line = lines[index].Trim();
            if (line.StartsWith('{') && line.EndsWith('}'))
            {
                return line;
            }
        }

        return null;
    }

    private static string Tail(string text, int max)
        => text.Length <= max ? text : text[^max..];

    private static string SafePlanId(string? planId)
    {
        if (string.IsNullOrWhiteSpace(planId)) return "pack";
        var builder = new StringBuilder(Math.Min(planId.Length, 64));
        foreach (var character in planId)
        {
            if (builder.Length >= 64) break;
            if (char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            {
                builder.Append(character);
            }
        }

        return builder.Length == 0 ? "pack" : builder.ToString();
    }
}
