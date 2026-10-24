using System.Diagnostics;
using System.Globalization;
using System.Text;
using Archoera.Domain.Instances;
using Archoera.Domain.Settings;

namespace Archoera.Infrastructure.Launch;

/// <summary>
/// The three launcher-side command hooks. They run around the game, not as
/// part of the JVM command line: <see cref="PreLoad"/> before versions and
/// libraries are resolved, <see cref="PreLaunch"/> immediately before the JVM
/// starts, and <see cref="PostExit"/> after it exits.
/// </summary>
internal enum LaunchCommandPhase
{
    PreLoad,
    PreLaunch,
    PostExit,
}

/// <summary>
/// Everything a hook needs to build its child process and its <c>INST_*</c>
/// environment. Captured from the launch plan so the post-exit hook still has
/// the values after the JVM is gone.
/// </summary>
internal sealed record LaunchCommandContext(
    LaunchCommandPhase Phase,
    string Command,
    string InstanceId,
    string InstanceName,
    string InstanceRoot,
    string GameDirectory,
    string JavaPath,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string>? Environment);

internal enum LaunchCommandFailure
{
    None,
    NonZeroExit,
    Timeout,
    StartFailed,
    InvalidCommand,
}

internal sealed record LaunchCommandResult(
    LaunchCommandPhase Phase,
    string LogPath,
    int? ExitCode,
    LaunchCommandFailure Failure,
    string StandardErrorTail,
    string? Detail,
    TimeSpan Timeout)
{
    public bool Succeeded => Failure == LaunchCommandFailure.None;
}

/// <summary>
/// Executes the explicit pre-load / pre-launch / post-exit commands.
///
/// <para>The command text is user configuration, tokenized with
/// <see cref="CommandLineTokenizer"/> and handed to the OS as a program plus
/// arguments. There is no shell, no string concatenation and no expansion of
/// <c>$INST_*</c>; those are real environment variables the command can read.
/// A user who needs shell features must invoke a shell explicitly.</para>
///
/// <para>Output is captured to <c>&lt;instance&gt;/.archoera/logs/</c> with a
/// 32 KiB budget per stream, so a chatty command cannot grow memory without
/// bound. Pre-load and pre-launch failures become
/// <see cref="LaunchException"/> with code <c>LAUNCH_COMMAND_FAILED</c>;
/// post-exit failures are logged and swallowed.</para>
/// </summary>
internal static class LaunchCommandRunner
{
    /// <summary>Captured bytes per stream; further output is still drained.</summary>
    internal const int MaxCapturedBytesPerStream = 32 * 1024;

    /// <summary>Timeout for a pre-load hook.</summary>
    internal static readonly TimeSpan PreLoadTimeout = TimeSpan.FromSeconds(300);

    /// <summary>Timeout for a pre-launch hook.</summary>
    internal static readonly TimeSpan PreLaunchTimeout = TimeSpan.FromSeconds(300);

    /// <summary>Timeout for a post-exit hook.</summary>
    internal static readonly TimeSpan PostExitTimeout = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Argument names whose value is an account secret. They are redacted from
    /// <c>INST_JAVA_ARGS</c>; tokens and client ids must never reach a hook's
    /// environment.
    /// </summary>
    private static readonly string[] SecretArgumentNames =
    [
        "--accessToken",
        "--clientId",
        "--clientid",
        "--auth_access_token",
        "--auth_xuid",
        "--xuid",
    ];

    public static LaunchCommandContext Create(
        LaunchCommandPhase phase,
        string command,
        GameInstanceRecord instance,
        string gameDirectory,
        string? javaPath,
        IReadOnlyList<string>? arguments,
        IReadOnlyDictionary<string, string>? environment)
        => new(
            phase,
            command,
            instance.Id,
            instance.Name,
            instance.RootPath,
            gameDirectory,
            javaPath ?? string.Empty,
            arguments ?? [],
            environment);

    public static LaunchCommandContext Create(
        LaunchCommandPhase phase,
        string command,
        GameInstanceRecord instance,
        LaunchPlan plan)
        => Create(
            phase,
            command,
            instance,
            plan.GameDirectory,
            plan.JavaPath,
            plan.Arguments,
            plan.Environment);

    /// <summary>
    /// Runs the configured pre-load hook. Called after the instance record and
    /// effective settings are known but before version or library resolution.
    /// <c>INST_JAVA</c> is the pinned Java path when one is configured;
    /// automatic selection is not available that early.
    /// </summary>
    public static async Task RunPreLoadAsync(
        GameInstanceRecord instance,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.PreLoadCommand)) return;
        var context = Create(
            LaunchCommandPhase.PreLoad,
            settings.PreLoadCommand,
            instance,
            instance.RootPath,
            settings.JavaAuto ? null : settings.JavaPath,
            null,
            settings.Environment);
        var result = await RunAsync(
                context,
                PreLoadTimeout,
                cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(result);
    }

    /// <summary>
    /// Runs the configured pre-launch hook immediately before the JVM starts.
    /// Any failure aborts the launch with <c>LAUNCH_COMMAND_FAILED</c>.
    /// </summary>
    public static async Task RunPreLaunchAsync(
        GameInstanceRecord instance,
        AppSettings settings,
        LaunchPlan plan,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.PreLaunchCommand)) return;
        var context = Create(
            LaunchCommandPhase.PreLaunch,
            settings.PreLaunchCommand,
            instance,
            plan);
        var result = await RunAsync(
                context,
                PreLaunchTimeout,
                cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(result);
    }

    /// <summary>
    /// Builds the post-exit callback for <see cref="LaunchProcessStarter"/>.
    /// The callback starts the hook without waiting, so the JVM exit handler is
    /// never blocked; <c>null</c> means no hook is configured.
    /// </summary>
    public static Action<int>? CreatePostExitCallback(
        GameInstanceRecord instance,
        AppSettings settings,
        LaunchPlan plan)
    {
        if (string.IsNullOrWhiteSpace(settings.PostExitCommand)) return null;
        var context = Create(
            LaunchCommandPhase.PostExit,
            settings.PostExitCommand,
            instance,
            plan);
        return _ => RunPostExit(context);
    }

    /// <summary>
    /// Runs a hook and returns its bounded result. Command failures are
    /// represented in the result instead of exceptions so the post-exit path
    /// can log them without unwinding the launcher. Cancellation still throws
    /// after the process tree is killed.
    /// </summary>
    public static async Task<LaunchCommandResult> RunAsync(
        LaunchCommandContext context,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var directory = LogDirectory(context.InstanceRoot);
        Directory.CreateDirectory(directory);
        var logPath = UniqueLogPath(directory, context.Phase);
        using var log = new CommandLog(logPath);
        log.WriteLine($"[archoera] working directory: {context.InstanceRoot}");

        ProcessStartInfo startInfo;
        try
        {
            startInfo = BuildStartInfo(context);
        }
        catch (Exception exception) when (
            exception is FormatException or ArgumentException)
        {
            log.WriteLine($"[archoera] invalid command: {exception.Message}");
            log.Complete();
            return new LaunchCommandResult(
                context.Phase,
                logPath,
                null,
                LaunchCommandFailure.InvalidCommand,
                string.Empty,
                exception.Message,
                timeout);
        }

        // Log only the resolved executable: the configured argument list may
        // contain paths or secrets the user did not intend to persist.
        log.WriteLine($"[archoera] {Wire(context.Phase)} command: {startInfo.FileName}");

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                log.WriteLine("[archoera] command did not start");
                log.Complete();
                return new LaunchCommandResult(
                    context.Phase,
                    logPath,
                    null,
                    LaunchCommandFailure.StartFailed,
                    string.Empty,
                    "the process did not start",
                    timeout);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            log.WriteLine($"[archoera] start failed: {exception.Message}");
            log.Complete();
            return new LaunchCommandResult(
                context.Phase,
                logPath,
                null,
                LaunchCommandFailure.StartFailed,
                string.Empty,
                exception.Message,
                timeout);
        }

        log.WriteLine($"[archoera] pid {process.Id}");
        var standardOutput = CaptureAsync(
            process.StandardOutput,
            "stdout",
            log,
            cancellationToken);
        var standardError = CaptureAsync(
            process.StandardError,
            "stderr",
            log,
            cancellationToken);

        var timedOut = false;
        var cancelled = false;
        try
        {
            await process
                .WaitForExitAsync(cancellationToken)
                .WaitAsync(timeout)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            timedOut = true;
            TryKill(process);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            TryKill(process);
        }

        await DrainAsync(standardOutput, standardError).ConfigureAwait(false);
        if (cancelled)
        {
            log.WriteLine("[archoera] command cancelled; process tree killed");
            log.Complete();
            throw new OperationCanceledException(cancellationToken);
        }

        if (timedOut)
        {
            log.WriteLine(
                $"[archoera] timed out after {FormatSeconds(timeout)}; process tree killed");
            log.Complete();
            return new LaunchCommandResult(
                context.Phase,
                logPath,
                null,
                LaunchCommandFailure.Timeout,
                Tail(standardError),
                null,
                timeout);
        }

        var exitCode = process.ExitCode;
        var errorTail = Tail(standardError);
        log.WriteLine($"[archoera] exited with code {exitCode}");
        log.Complete();
        return new LaunchCommandResult(
            context.Phase,
            logPath,
            exitCode,
            exitCode == 0
                ? LaunchCommandFailure.None
                : LaunchCommandFailure.NonZeroExit,
            errorTail,
            null,
            timeout);
    }

    /// <summary>
    /// Converts a failed pre-load or pre-launch result to the stable launch
    /// error the UI reports. The message names the phase, the exit status and a
    /// short stderr tail (never credentials) and points at the full log.
    /// </summary>
    public static void EnsureSuccess(LaunchCommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Succeeded) return;

        var phase = Wire(result.Phase);
        var reason = result.Failure switch
        {
            LaunchCommandFailure.Timeout =>
                $"timed out after {FormatSeconds(result.Timeout)}",
            LaunchCommandFailure.StartFailed => "could not start",
            LaunchCommandFailure.InvalidCommand => "is invalid",
            _ => $"exited with code {result.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "?"}",
        };

        var message = $"{phase} command {reason}";
        var detail = !string.IsNullOrWhiteSpace(result.StandardErrorTail)
            ? result.StandardErrorTail
            : result.Detail;
        if (!string.IsNullOrWhiteSpace(detail))
        {
            message += $": {detail}";
        }

        message += $" [log: {result.LogPath}]";
        throw new LaunchException("LAUNCH_COMMAND_FAILED", message);
    }

    /// <summary>
    /// Fire-and-forget post-exit hook. The returned task is intentionally not
    /// awaited by the JVM exit handler; callers that need to observe it (tests)
    /// can await the return value instead. It never throws.
    /// </summary>
    public static void RunPostExit(LaunchCommandContext context)
        => _ = Task.Run(() => RunPostExitAsync(context));

    internal static async Task<LaunchCommandResult?> RunPostExitAsync(
        LaunchCommandContext context)
    {
        try
        {
            // Failure details are already written to the command log by RunAsync.
            return await RunAsync(context, PostExitTimeout, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            TryLogUnexpectedPostExitFailure(context, exception);
            return null;
        }
    }

    /// <summary>
    /// Builds the child process description without starting it. Kept separate
    /// so the argument list, working directory and <c>INST_*</c> environment can
    /// be asserted without executing arbitrary user commands.
    /// </summary>
    public static ProcessStartInfo BuildStartInfo(LaunchCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var tokens = CommandLineTokenizer.Split(context.Command);
        if (tokens.Count == 0 || tokens[0].Length == 0)
        {
            throw new FormatException("command must start with a non-empty executable");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = tokens[0],
            WorkingDirectory = context.InstanceRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        for (var index = 1; index < tokens.Count; index++)
        {
            startInfo.ArgumentList.Add(tokens[index]);
        }

        // Inherit the launcher environment, then layer the effective launch
        // environment on top. Hostile JVM option variables stay stripped.
        if (context.Environment is not null)
        {
            foreach (var pair in context.Environment)
            {
                if (LaunchEnvironmentRules.IsHostile(pair.Key)) continue;
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        startInfo.Environment["INST_NAME"] = EnvironmentValue(context.InstanceName);
        startInfo.Environment["INST_ID"] = EnvironmentValue(context.InstanceId);
        startInfo.Environment["INST_DIR"] = EnvironmentValue(context.InstanceRoot);
        startInfo.Environment["INST_MC_DIR"] = EnvironmentValue(context.GameDirectory);
        startInfo.Environment["INST_JAVA"] = EnvironmentValue(context.JavaPath);
        startInfo.Environment["INST_JAVA_ARGS"] = JoinArguments(context.Arguments);
        return startInfo;
    }

    /// <summary>
    /// Joins JVM and game arguments for <c>INST_JAVA_ARGS</c>. This is a
    /// best-effort convenience value: spaces inside an argument are ambiguous
    /// and should not be parsed back. Account secrets are redacted.
    /// </summary>
    internal static string JoinArguments(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0) return string.Empty;

        var result = new List<string>(arguments.Count);
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            var separator = argument.IndexOf('=');
            var name = separator > 0 ? argument[..separator] : argument;
            if (IsSecretArgument(name))
            {
                if (separator > 0)
                {
                    result.Add(name + "=<redacted>");
                }
                else
                {
                    result.Add(argument);
                    if (index + 1 < arguments.Count)
                    {
                        result.Add("<redacted>");
                        index++;
                    }
                }

                continue;
            }

            result.Add(argument);
        }

        return string.Join(' ', result);
    }

    internal static string LogDirectory(string instanceRoot)
        => Path.Combine(instanceRoot, ".archoera", "logs");

    internal static string Wire(LaunchCommandPhase phase)
        => phase switch
        {
            LaunchCommandPhase.PreLoad => "pre-load",
            LaunchCommandPhase.PreLaunch => "pre-launch",
            _ => "post-exit",
        };

    private static async Task<string> CaptureAsync(
        StreamReader reader,
        string channel,
        CommandLog log,
        CancellationToken cancellationToken)
    {
        var tail = new StringBuilder();
        var buffer = new char[4096];
        try
        {
            while (true)
            {
                var read = await reader
                    .ReadAsync(buffer.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0) break;
                var chunk = new string(buffer, 0, read);
                log.WriteChunk(channel, chunk);
                AppendTail(tail, chunk, 1024);
            }
        }
        catch (Exception exception) when (
            exception is IOException or ObjectDisposedException or
            InvalidOperationException or OperationCanceledException)
        {
            // A killed child can close the pipe mid-read; the exit status is the
            // authoritative result.
        }

        return tail.ToString();
    }

    private static async Task DrainAsync(params Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks)
                .WaitAsync(TimeSpan.FromSeconds(2))
                .ConfigureAwait(false);
        }
        catch
        {
            // The pipe readers are best effort; the log already has what arrived.
        }
    }

    private static string Tail(Task<string> task)
    {
        if (!task.IsCompletedSuccessfully) return string.Empty;
        var text = task.Result
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return text.Length <= 400 ? text : text[^400..];
    }

    private static void AppendTail(StringBuilder tail, string chunk, int limit)
    {
        tail.Append(chunk);
        if (tail.Length <= limit) return;
        tail.Remove(0, tail.Length - limit);
    }

    private static bool IsSecretArgument(string name)
    {
        foreach (var secret in SecretArgumentNames)
        {
            if (string.Equals(secret, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string EnvironmentValue(string value)
        => value.Replace("\0", string.Empty, StringComparison.Ordinal);

    private static string UniqueLogPath(string directory, LaunchCommandPhase phase)
    {
        var stamp = DateTimeOffset.UtcNow.ToString(
            "yyyyMMdd-HHmmss-fff",
            CultureInfo.InvariantCulture);
        var name = $"launch-command-{Wire(phase)}-{stamp}.log";
        var path = Path.Combine(directory, name);
        var suffix = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(directory, $"{name[..^4]}-{suffix++}.log");
        }

        return path;
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
        catch (Exception)
        {
            // Best effort: the process may have exited between the check and kill.
        }
    }

    private static void TryLogUnexpectedPostExitFailure(
        LaunchCommandContext context,
        Exception exception)
    {
        try
        {
            var directory = LogDirectory(context.InstanceRoot);
            Directory.CreateDirectory(directory);
            var path = UniqueLogPath(directory, LaunchCommandPhase.PostExit);
            File.WriteAllText(
                path,
                $"[archoera] post-exit command handler failed: {exception.Message}{Environment.NewLine}");
        }
        catch
        {
            // A best-effort hook must never take the launcher down.
        }
    }

    private static string FormatSeconds(TimeSpan value)
        => value.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + "s";

    /// <summary>
    /// Per-run log with a fixed byte budget for each stream. The file is opened
    /// immediately so an invalid or failing command still leaves an inspectable
    /// record. Info lines are not charged to either stream budget.
    /// </summary>
    private sealed class CommandLog : IDisposable
    {
        private readonly object _gate = new();
        private readonly StreamWriter _writer;
        private readonly Dictionary<string, long> _captured = new(StringComparer.Ordinal);
        private readonly HashSet<string> _announced = new(StringComparer.Ordinal);
        private readonly HashSet<string> _truncated = new(StringComparer.Ordinal);
        private bool _closed;
        private bool _atLineStart = true;

        public CommandLog(string path)
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.Read,
            };
            if (!OperatingSystem.IsWindows())
            {
                // Command output is user data and can contain credentials; the
                // owner-only mode matches the settings and vault files.
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            var stream = new FileStream(path, options);
            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        }

        public void WriteLine(string line)
        {
            lock (_gate)
            {
                if (_closed) return;
                if (!_atLineStart) WriteUnlocked(Environment.NewLine);
                WriteUnlocked(line);
                WriteUnlocked(Environment.NewLine);
                _atLineStart = true;
            }
        }

        public void WriteChunk(string channel, string text)
        {
            lock (_gate)
            {
                if (_closed || text.Length == 0) return;
                if (_announced.Add(channel))
                {
                    // First chunk of this stream gets a header once.
                    if (!_atLineStart) WriteUnlocked(Environment.NewLine);
                    WriteUnlocked($"[{channel}] ");
                    _atLineStart = false;
                }

                var captured = _captured.GetValueOrDefault(channel);
                var bytes = Encoding.UTF8.GetByteCount(text);
                if (captured + bytes <= MaxCapturedBytesPerStream)
                {
                    WriteUnlocked(text);
                    _captured[channel] = captured + bytes;
                    _atLineStart = text.EndsWith('\n');
                    return;
                }

                var remaining = MaxCapturedBytesPerStream - captured;
                if (remaining > 0)
                {
                    var prefix = Utf8Prefix(text, (int)remaining);
                    WriteUnlocked(prefix);
                    _captured[channel] = captured + Encoding.UTF8.GetByteCount(prefix);
                    _atLineStart = prefix.EndsWith('\n');
                }

                if (_truncated.Add(channel))
                {
                    if (!_atLineStart) WriteUnlocked(Environment.NewLine);
                    WriteUnlocked(
                        $"[archoera] {channel} capture truncated after " +
                        $"{MaxCapturedBytesPerStream / 1024} KiB{Environment.NewLine}");
                    _atLineStart = true;
                }
            }
        }

        public void Complete()
        {
            lock (_gate)
            {
                if (_closed) return;
                if (!_atLineStart) WriteUnlocked(Environment.NewLine);
                _closed = true;
                _writer.Dispose();
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_closed) return;
                _closed = true;
                _writer.Dispose();
            }
        }

        private void WriteUnlocked(string text)
        {
            try
            {
                _writer.Write(text);
            }
            catch (IOException)
            {
                // A viewer may hold the file; a failed capture write must not
                // change the command result.
            }
            catch (ObjectDisposedException)
            {
                // The process exited while output was still arriving.
            }
        }

        /// <summary>Longest character prefix whose UTF-8 form fits the budget.</summary>
        private static string Utf8Prefix(string text, int maxBytes)
        {
            var bytes = 0;
            for (var index = 0; index < text.Length; index++)
            {
                var size = Encoding.UTF8.GetByteCount(text.AsSpan(index, 1));
                if (bytes + size > maxBytes) return text[..index];
                bytes += size;
            }

            return text;
        }
    }
}
