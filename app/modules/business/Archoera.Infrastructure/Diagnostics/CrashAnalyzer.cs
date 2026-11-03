using System.Text.RegularExpressions;

namespace Archoera.Infrastructure.Diagnostics;

/// <summary>One explainable diagnostic finding produced from game artifacts.</summary>
/// <param name="RuleId">Stable id; the UI maps it to localized copy.</param>
/// <param name="Severity">blocker, crash, warning or info.</param>
/// <param name="Confidence">0–100 confidence used to order findings.</param>
/// <param name="Args">Values referenced by the localized message.</param>
/// <param name="Evidence">Short redacted excerpts that produced the finding.</param>
public sealed record CrashFinding(
    string RuleId,
    string Severity,
    int Confidence,
    IReadOnlyDictionary<string, string> Args,
    IReadOnlyList<string> Evidence);

/// <summary>Result of analyzing one launch's logs and crash artifacts.</summary>
public sealed record CrashAnalysis(
    string Outcome,
    int? ExitCode,
    string? LogPath,
    string? CrashReportPath,
    string? HsErrPath,
    IReadOnlyList<CrashFinding> Findings,
    DateTimeOffset AnalyzedAt)
{
    /// <summary>True when the game did not exit cleanly.</summary>
    public bool Abnormal => !string.Equals(Outcome, "clean", StringComparison.Ordinal);
}

/// <summary>
/// Reads the latest launch's artifacts and turns common failures into a short,
/// explainable diagnosis. This is a conservative first stage of the design in
/// <c>docs/08-technical-research/04-crash-analysis.md</c>: local only, no
/// upload, and only findings with a concrete textual signature.
/// </summary>
public static class CrashAnalyzer
{
    private const int MaxLogBytes = 4 * 1024 * 1024;
    private const int MaxCrashReportBytes = 512 * 1024;
    private const int MaxHsErrBytes = 256 * 1024;
    private const int MaxEvidenceLength = 240;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(80);

    private static readonly Regex ExitCodePattern = new(
        @"\[exited with code (?<code>-?\d+)\]",
        RegexOptions.CultureInvariant | RegexOptions.RightToLeft,
        MatchTimeout);

    private static readonly Regex CrashMarker = new(
        @"#@!@# Game crashed!",
        RegexOptions.CultureInvariant,
        MatchTimeout);

    private static readonly Regex AccessTokenPattern = new(
        @"(--accessToken\s+)\S+",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        MatchTimeout);

    private static readonly Regex BearerPattern = new(
        @"(Bearer\s+)[A-Za-z0-9._~+/=-]+",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        MatchTimeout);

    private static readonly Regex EmailPattern = new(
        @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        MatchTimeout);

    /// <summary>
    /// Analyzes <paramref name="instanceRoot"/>. <paramref name="logPath"/> is
    /// the launch log returned by <c>launch.start</c>; when absent the newest
    /// launch log is used.
    /// </summary>
    public static CrashAnalysis Analyze(
        string instanceRoot,
        string? logPath = null,
        int? exitCode = null,
        DateTimeOffset? startedAt = null)
    {
        var root = Path.GetFullPath(instanceRoot);
        var logFile = ResolveLaunchLog(root, logPath);
        var start = startedAt ??
                    (logFile is null
                        ? DateTimeOffset.UtcNow.AddHours(-1)
                        : SafeCreationTime(logFile) ?? SafeWriteTime(logFile) ??
                          DateTimeOffset.UtcNow.AddHours(-1));

        var launchText = logFile is null ? string.Empty : ReadLog(logFile);
        var latestLog = Path.Combine(root, "logs", "latest.log");
        var latestText = File.Exists(latestLog) ? ReadLog(latestLog) : string.Empty;
        var crashReport = FindCrashReport(root, start);
        var crashText = crashReport is null ? string.Empty : ReadCrashReport(crashReport);
        var hsErr = FindHsErr(root, start);
        var hsText = hsErr is null ? string.Empty : ReadHsErr(hsErr);

        var corpus = string.Join(
            "\n",
            launchText,
            latestText,
            crashText,
            hsText);

        var code = exitCode ?? ParseExitCode(launchText) ?? ParseExitCode(latestText);
        var crashed = crashReport is not null ||
                      hsErr is not null ||
                      CrashMarker.IsMatch(launchText) ||
                      CrashMarker.IsMatch(latestText) ||
                      (code is not null && code != 0);

        var findings = new List<CrashFinding>();
        AddRule(
            findings,
            "java.too_old",
            "blocker",
            95,
            corpus,
            @"UnsupportedClassVersionError[\s\S]{0,300}?class file version (?<actual>\d+)\.0[\s\S]{0,120}?up to (?<supported>\d+)\.0",
            match => new Dictionary<string, string>
            {
                ["needed"] = JavaMajor(match.Groups["actual"].Value),
                ["current"] = JavaMajor(match.Groups["supported"].Value),
            });
        AddRule(
            findings,
            "java.too_new",
            "blocker",
            80,
            corpus,
            @"Unsupported class file major version \d+|NoSuchFieldException: ucp|because module java\.base does not export");
        AddRule(
            findings,
            "jvm.heap",
            "crash",
            90,
            corpus,
            @"Could not reserve enough space for .*?object heap|Invalid maximum heap size|OutOfMemoryError: Java heap space");
        AddRule(
            findings,
            "mixin.failed",
            "blocker",
            90,
            corpus,
            @"Mixin apply failed (?<config>[\w.-]+\.mixins\.json)|MixinTransformerError|Critical injection failure|InvalidMixinException",
            match => new Dictionary<string, string>
            {
                ["config"] = match.Groups["config"].Success
                    ? match.Groups["config"].Value
                    : string.Empty,
            });
        AddRule(
            findings,
            "deps.missing",
            "blocker",
            88,
            corpus,
            @"Could not find required mod: (?<mod>.+?) requires|Missing or unsupported mandatory dependencies");
        AddRule(
            findings,
            "loader.main_class",
            "blocker",
            92,
            corpus,
            @"Could not find or load main class (?<cls>\S+)|Invalid or corrupt jarfile",
            match => new Dictionary<string, string>
            {
                ["cls"] = match.Groups["cls"].Success
                    ? match.Groups["cls"].Value
                    : "main class",
            });
        AddRule(
            findings,
            "gfx.glfw",
            "blocker",
            88,
            corpus,
            @"GLFW error|Pixel format not accelerated|does not appear to support OpenGL|Failed to create window");
        AddRule(
            findings,
            "mods.entrypoint",
            "crash",
            88,
            corpus,
            @"Could not execute entrypoint stage '(?<stage>[^']+)' due to errors, provided by '(?<mod>[^']+)'",
            match => new Dictionary<string, string>
            {
                ["stage"] = match.Groups["stage"].Value,
                ["mod"] = match.Groups["mod"].Value,
            });
        AddRule(
            findings,
            "io.file_locked",
            "blocker",
            75,
            corpus,
            @"being used by another process|AccessDeniedException|Permission denied.*\.jar");

        // Native crashes have no Java stack; hs_err is the only reliable source.
        if (hsErr is not null)
        {
            var frame = ExtractProblematicFrame(hsText);
            if (IsGraphicsFrame(frame))
            {
                AddFinding(
                    findings,
                    "gfx.driver_crash",
                    "crash",
                    95,
                    new Dictionary<string, string> { ["lib"] = frame },
                    EvidenceAround(hsText, frame));
            }
            else
            {
                AddFinding(
                    findings,
                    "jvm.native_crash",
                    "crash",
                    92,
                    new Dictionary<string, string>
                    {
                        ["frame"] = string.IsNullOrWhiteSpace(frame) ? "native code" : frame,
                    },
                    EvidenceAround(hsText, frame));
            }
        }

        // If the crash report names a description but no signature matched, keep
        // the description as the actionable fallback instead of saying nothing.
        if (findings.Count == 0 && crashReport is not null)
        {
            var description = ExtractDescription(crashText);
            if (!string.IsNullOrWhiteSpace(description))
            {
                AddFinding(
                    findings,
                    "crash.report",
                    "crash",
                    70,
                    new Dictionary<string, string> { ["description"] = description },
                    [Clamp(description)]);
            }
        }

        if (findings.Count == 0 && code is not null && code != 0)
        {
            AddFinding(
                findings,
                "unknown.exit_code",
                "crash",
                40,
                new Dictionary<string, string> { ["code"] = code.Value.ToString() },
                []);
        }

        findings.Sort((left, right) =>
        {
            var severity = SeverityRank(right.Severity).CompareTo(SeverityRank(left.Severity));
            return severity != 0 ? severity : right.Confidence.CompareTo(left.Confidence);
        });

        var outcome = crashed ? "crashed" : code is not null ? "clean" : "unknown";
        return new CrashAnalysis(
            outcome,
            code,
            logFile,
            crashReport,
            hsErr,
            findings.Take(3).ToArray(),
            DateTimeOffset.UtcNow);
    }

    private static void AddRule(
        List<CrashFinding> findings,
        string id,
        string severity,
        int confidence,
        string text,
        string pattern,
        Func<Match, Dictionary<string, string>>? args = null)
    {
        if (findings.Any(item => string.Equals(item.RuleId, id, StringComparison.Ordinal)))
        {
            return;
        }

        Match match;
        try
        {
            match = Regex.Match(
                text,
                pattern,
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                MatchTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return;
        }

        if (!match.Success) return;
        AddFinding(
            findings,
            id,
            severity,
            confidence,
            args?.Invoke(match) ?? new Dictionary<string, string>(),
            EvidenceAround(text, match.Value));
    }

    private static void AddFinding(
        List<CrashFinding> findings,
        string id,
        string severity,
        int confidence,
        IReadOnlyDictionary<string, string> args,
        IReadOnlyList<string> evidence)
    {
        findings.Add(new CrashFinding(
            id,
            severity,
            confidence,
            args,
            evidence.Select(Redact).Where(line => line.Length > 0).Take(3).ToArray()));
    }

    private static IReadOnlyList<string> EvidenceAround(string text, string needle)
    {
        if (string.IsNullOrEmpty(needle)) return [];
        var index = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return [Clamp(needle)];

        var start = text.LastIndexOf('\n', Math.Max(0, index - 1));
        start = start < 0 ? 0 : start + 1;
        var end = text.IndexOf('\n', index);
        end = end < 0 ? text.Length : end;
        var line = text[start..end].Trim();
        return string.IsNullOrWhiteSpace(line) ? [Clamp(needle)] : [Clamp(line)];
    }

    private static string ExtractProblematicFrame(string hsText)
    {
        var match = Regex.Match(
            hsText,
            @"#\s+Problematic frame:\s*\n#\s+(?<frame>[^\n]+)",
            RegexOptions.CultureInvariant,
            MatchTimeout);
        return match.Success ? match.Groups["frame"].Value.Trim() : string.Empty;
    }

    private static bool IsGraphicsFrame(string frame)
    {
        if (string.IsNullOrWhiteSpace(frame)) return false;
        foreach (var name in new[]
                 {
                     "nvoglv", "atio6axx", "igdumdim", "libGL", "swrast", "opengl32",
                     "libEGL", "vulkan",
                 })
        {
            if (frame.Contains(name, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static string ExtractDescription(string crashText)
    {
        var match = Regex.Match(
            crashText,
            @"^Description:\s*(?<description>[^\n]+)",
            RegexOptions.CultureInvariant | RegexOptions.Multiline,
            MatchTimeout);
        return match.Success ? Clamp(match.Groups["description"].Value.Trim()) : string.Empty;
    }

    private static int? ParseExitCode(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var match = ExitCodePattern.Match(text);
        return match.Success && int.TryParse(match.Groups["code"].Value, out var code)
            ? code
            : null;
    }

    private static string? ResolveLaunchLog(string root, string? logPath)
    {
        if (!string.IsNullOrWhiteSpace(logPath) && File.Exists(logPath))
        {
            return Path.GetFullPath(logPath);
        }

        var directory = Path.Combine(root, ".archoera", "logs");
        if (!Directory.Exists(directory)) return null;
        try
        {
            return Directory
                .EnumerateFiles(directory, "launch-*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? FindCrashReport(string root, DateTimeOffset startedAt)
    {
        var directory = Path.Combine(root, "crash-reports");
        if (!Directory.Exists(directory)) return null;
        try
        {
            return Directory
                .EnumerateFiles(directory, "crash-*.txt", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .Where(info => info.LastWriteTimeUtc >= startedAt.UtcDateTime.AddSeconds(-5))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .Select(info => info.FullName)
                .FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? FindHsErr(string root, DateTimeOffset startedAt)
    {
        if (!Directory.Exists(root)) return null;
        try
        {
            return Directory
                .EnumerateFiles(root, "hs_err_pid*.log", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .Where(info => info.LastWriteTimeUtc >= startedAt.UtcDateTime.AddSeconds(-5))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .Select(info => info.FullName)
                .FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string ReadLog(string path) => ReadTail(path, MaxLogBytes);

    private static string ReadCrashReport(string path) => ReadHead(path, MaxCrashReportBytes);

    private static string ReadHsErr(string path) => ReadHead(path, MaxHsErrBytes);

    private static string ReadTail(string path, int maxBytes)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(Math.Max(0, stream.Length - maxBytes), SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static string ReadHead(string path, int maxBytes)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[Math.Min(maxBytes, (int)Math.Min(stream.Length, maxBytes))];
            var read = stream.Read(buffer, 0, buffer.Length);
            return System.Text.Encoding.UTF8.GetString(buffer, 0, read);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static DateTimeOffset? SafeCreationTime(string path)
    {
        try
        {
            return File.GetCreationTimeUtc(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static DateTimeOffset? SafeWriteTime(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string JavaMajor(string classFileVersion)
        => int.TryParse(classFileVersion, out var version) && version > 44
            ? (version - 44).ToString()
            : classFileVersion;

    private static string Clamp(string value)
    {
        var clean = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return clean.Length <= MaxEvidenceLength
            ? clean
            : clean[..MaxEvidenceLength] + "…";
    }

    private static string Redact(string value)
    {
        var text = AccessTokenPattern.Replace(value, "$1<redacted>");
        text = BearerPattern.Replace(text, "$1<redacted>");
        return EmailPattern.Replace(text, "<redacted>");
    }

    private static int SeverityRank(string severity) => severity switch
    {
        "blocker" => 4,
        "crash" => 3,
        "warning" => 2,
        _ => 1,
    };
}
