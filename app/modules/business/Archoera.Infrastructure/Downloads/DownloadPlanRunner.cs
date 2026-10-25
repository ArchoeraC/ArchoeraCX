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
/// Stable code and message shared by every guard that reports a missing Rust
/// executor (the runner itself, the resource facade and pack installs), so
/// the wording cannot drift between call sites.
/// </summary>
public static class DownloaderMissing
{
    public const string Code = "DOWNLOADER_MISSING";
    public const string Message =
        "archoera-dl was not found; build app/modules/downloader and set ARCHOERACX_DL_BIN";
}

/// <summary>
/// Writes a validated plan next to the instance, invokes the Rust
/// <c>archoera-dl</c> CLI and streams its progress lines into
/// <see cref="ModuleProgress"/>. C# never downloads game bytes itself.
///
/// This is the single process bridge for <c>download.execute</c> and pack
/// installs; the executor still validates the plan independently.
/// </summary>
/// <summary>
/// Producer intent for runtime tuning. Resource plans contain many small
/// mod/config files, so they use shorter ranges to keep a single large file
/// parallel; game plans keep the user's normal chunk size.
/// </summary>
public enum DownloadProfile
{
    Default,
    Resource,
}

public interface IDownloadPlanRunner
{
    bool IsAvailable { get; }
    Task<PlanExecutionResult> ExecuteAsync(
        DownloadPlan plan,
        CancellationToken cancellationToken = default,
        DownloadProfile profile = DownloadProfile.Default);
}

public sealed class DownloadPlanRunner : IDownloadPlanRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(120);

    /// <summary>
    /// Plans are transient now that they can embed proxy credentials. A failed
    /// plan is kept briefly for diagnosis; anything older is swept on the next
    /// run.
    /// </summary>
    private static readonly TimeSpan PlanRetention = TimeSpan.FromDays(7);

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
        CancellationToken cancellationToken = default,
        DownloadProfile profile = DownloadProfile.Default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!IsAvailable)
        {
            throw new DownloadRunnerException(DownloaderMissing.Code, DownloaderMissing.Message);
        }

        // User tuning wins over producer defaults (docs/10 §9.4).
        DownloadProxyOptions? manualProxy = null;
        try
        {
            var settings = await new JsonAppSettingsStore()
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false);
            plan = ApplyTuning(plan, settings, profile);
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
        CleanupExpiredPlans(planDirectory, DateTimeOffset.UtcNow, PlanRetention);
        var planPath = Path.Combine(
            planDirectory,
            $"plan-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-" +
            $"{SafePlanId(plan.PlanId)}-{Guid.NewGuid():N}.json");
        await WritePlanFileAsync(planPath, planJson, cancellationToken)
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

        var executionResult = new PlanExecutionResult(
            status,
            string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase),
            planPath,
            Tail(stdoutText, 4000),
            Tail(stderrText, 4000),
            failedCount,
            failedSummary);
        if (executionResult.Completed)
        {
            // Completed plans have no diagnostic value and may carry proxy
            // credentials. Failed plans stay (mode 0600) for inspection.
            TryDeletePlanFile(planPath);
        }

        return executionResult;
    }

    /// <summary>
    /// Applies the user's download tuning (concurrency, per-host cap) to a
    /// plan that was produced elsewhere. Plans stay declarative; the runner is
    /// the single place where runtime preferences win over producer defaults.
    /// </summary>
    public static DownloadPlan ApplyTuning(
        DownloadPlan plan,
        AppSettings settings,
        DownloadProfile profile = DownloadProfile.Default)
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
        var chunkSizeKb = Math.Clamp(settings.DownloadChunkSizeKb, 128, 8192);
        var rangeThresholdKb = Math.Clamp(settings.DownloadRangeThresholdKb, 0, 65536);
        if (profile == DownloadProfile.Resource)
        {
            // Mods and pack configs are usually only a few MiB. A 512 KiB
            // range lets one file use several connections instead of waiting
            // for a single 4 MiB stream, while a disabled range setting stays
            // disabled.
            chunkSizeKb = Math.Min(chunkSizeKb, 512);
            if (rangeThresholdKb > 0)
            {
                rangeThresholdKb = Math.Min(rangeThresholdKb, 512);
            }
            // Resource packs contain many files from the same CDN host. When
            // the user kept the historical default of 8, allow the plan to use
            // the global budget (still capped by concurrency) instead of
            // leaving half of it idle. An explicit lower cap is preserved.
            if (plan.Files.Count > 1 &&
                perHost == DownloadPlanLimits.DefaultPerHostConcurrency)
            {
                perHost = Math.Min(concurrency, 16);
            }
        }
        return plan with
        {
            Concurrency = concurrency,
            Options = plan.Options with
            {
                PerHostConcurrency = perHost,
                ChunkSize = chunkSizeKb * 1024L,
                RangeThreshold = rangeThresholdKb * 1024L,
                // Thousands of tiny mod/config files must not turn into one
                // fsync stall per file; a lost file is re-downloaded and
                // verified, so the resource profile uses the fast durability
                // mode already supported by the executor.
                Durability = profile == DownloadProfile.Resource
                    ? DownloadDurability.Fast
                    : plan.Options.Durability,
            },
        };
    }

    /// <summary>
    /// Locates the Rust executor. Only an explicit env override and the
    /// application directory (plus its <c>lib</c>/parent variants) are trusted
    /// in release builds; development builds also probe the repository.
    /// </summary>
    public static string ResolveDownloaderPath()
    {
        var configured = Environment.GetEnvironmentVariable("ARCHOERACX_DL_BIN");
        if (!string.IsNullOrWhiteSpace(configured) &&
            TryExistingFile(configured) is { } explicitPath)
        {
            return explicitPath;
        }

        var names = OperatingSystem.IsWindows()
            ? new[] { "archoera-dl.exe", "archoera-dl" }
            : new[] { "archoera-dl" };

        // Development builds also probe the repository working directory; a
        // packaged build must never load an executable from a location the
        // current user or another local user can plant files in.
        string? cwd = null;
        var includeDevelopmentProbes = false;
#if DEBUG
        includeDevelopmentProbes = true;
        try
        {
            cwd = Environment.CurrentDirectory;
        }
        catch
        {
            // Deleted/replaced working directory; packaged paths still work.
        }
#endif

        foreach (var baseDirectory in BuildDownloaderDirectories(
                     AppContext.BaseDirectory,
                     cwd,
                     includeDevelopmentProbes))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(baseDirectory, name);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Candidate directories in probe order. Pure so tests can assert that a
    /// release build never consults the (potentially writable) working
    /// directory.
    /// </summary>
    internal static IReadOnlyList<string> BuildDownloaderDirectories(
        string baseDirectory,
        string? currentDirectory,
        bool includeDevelopmentProbes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        var bases = new List<string>
        {
            baseDirectory,
            Path.Combine(baseDirectory, "lib"),
            Path.Combine(baseDirectory, ".."),
            Path.Combine(baseDirectory, "..", "lib"),
        };

        if (includeDevelopmentProbes && !string.IsNullOrWhiteSpace(currentDirectory))
        {
            bases.Add(Path.Combine(currentDirectory, "build", "native"));
            bases.Add(Path.Combine(currentDirectory, "app", "build", "native"));
            bases.Add(Path.Combine(
                currentDirectory, "app", "modules", "downloader", "target", "release"));
            bases.Add(Path.Combine(
                currentDirectory, "modules", "downloader", "target", "release"));
        }

        return bases;
    }

    private static string? TryExistingFile(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            return File.Exists(full) ? full : null;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes a plan as an owner-only file on Unix. The file contains the
    /// resolved proxy URL, which may embed credentials.
    /// </summary>
    internal static async Task WritePlanFileAsync(
        string planPath,
        byte[] json,
        CancellationToken cancellationToken)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        await using var stream = new FileStream(planPath, options);
        await stream.WriteAsync(json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes `plan-*.json` files older than <paramref name="maxAge"/> and
    /// returns the number removed. Only launcher-owned plan files are touched.
    /// </summary>
    internal static int CleanupExpiredPlans(
        string planDirectory,
        DateTimeOffset now,
        TimeSpan maxAge)
    {
        if (!Directory.Exists(planDirectory)) return 0;

        var cutoff = now.UtcDateTime - maxAge;
        var removed = 0;
        try
        {
            foreach (var path in Directory.EnumerateFiles(
                         planDirectory,
                         "plan-*.json",
                         SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var info = new FileInfo(path);
                    if (info.LastWriteTimeUtc >= cutoff) continue;
                    info.Delete();
                    removed++;
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    // Best effort; the next run retries.
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // The instance folder may be read-only; execution will report it.
        }

        return removed;
    }

    private static void TryDeletePlanFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Best effort; the retention sweep removes it later.
        }
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
