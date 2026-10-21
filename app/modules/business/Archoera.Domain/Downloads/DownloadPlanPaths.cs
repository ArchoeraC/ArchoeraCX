using System.Net;
using System.Text;

namespace Archoera.Domain.Downloads;

/// <summary>Stable error codes shared with the Rust executor.</summary>
public static class DownloadPlanErrorCode
{
    public const string InvalidPlan = "INVALID_PLAN";
    public const string PathEscape = "PATH_ESCAPE";
    public const string InvalidHash = "INVALID_HASH";
    public const string DuplicateDest = "DUPLICATE_DEST";
    public const string PlanTooLarge = "PLAN_TOO_LARGE";
    public const string UnsupportedScheme = "UNSUPPORTED_SCHEME";
}

/// <summary>One validation finding. <see cref="Code"/> is stable and log-safe.</summary>
/// <param name="Code">One of <see cref="DownloadPlanErrorCode"/>.</param>
/// <param name="Message">Human-readable detail. Contains paths, never secrets.</param>
public sealed record DownloadPlanIssue(string Code, string Message)
{
    /// <inheritdoc />
    public override string ToString() => $"{Code}: {Message}";
}

/// <summary>
/// Raised when a plan is rejected before any byte is downloaded. Matches the
/// fail-fast behaviour of <c>validate_plan_inner</c> in Rust.
/// </summary>
public sealed class DownloadPlanException : Exception
{
    public DownloadPlanException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>Stable error code, for example <c>PATH_ESCAPE</c>.</summary>
    public string Code { get; }

    /// <summary>Wraps a single issue.</summary>
    public static DownloadPlanException FromIssue(DownloadPlanIssue issue)
        => new(issue.Code, issue.Message);
}

/// <summary>
/// Path/shape rules for <c>root</c> and <c>files[].dest</c>. These are a direct
/// port of <c>normalize_dest</c>/<c>validate_url</c> in
/// <c>app/modules/downloader/src/plan.rs</c> so that the producer rejects a plan
/// before the executor would.
/// </summary>
public static class DownloadPlanPaths
{
    /// <summary>
    /// Validates and normalizes a relative destination. On success
    /// <paramref name="normalized"/> is the canonical forward-slash form.
    /// </summary>
    public static DownloadPlanIssue? TryNormalizeDest(string? dest, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrEmpty(dest))
        {
            return new DownloadPlanIssue(DownloadPlanErrorCode.InvalidPlan, "dest is empty");
        }

        if (dest.Contains('\0'))
        {
            return new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                $"{Printable(dest)}: dest contains NUL");
        }

        if (Encoding.UTF8.GetByteCount(dest) > DownloadPlanLimits.MaxDestBytes)
        {
            return new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                $"dest is longer than {DownloadPlanLimits.MaxDestBytes} bytes");
        }

        if (dest.Contains('\\'))
        {
            return new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                $"{dest}: backslashes are not allowed, use '/'");
        }

        if (dest[0] == '/' || Path.IsPathRooted(dest))
        {
            return new DownloadPlanIssue(
                DownloadPlanErrorCode.PathEscape,
                $"{dest}: dest must be relative");
        }

        var parts = dest.Split('/');
        foreach (var segment in parts)
        {
            if (segment.Length == 0)
            {
                return new DownloadPlanIssue(
                    DownloadPlanErrorCode.InvalidPlan,
                    $"{dest}: empty path segment");
            }

            if (segment == ".")
            {
                return new DownloadPlanIssue(
                    DownloadPlanErrorCode.InvalidPlan,
                    $"{dest}: '.' path segment is not allowed");
            }

            if (segment == "..")
            {
                return new DownloadPlanIssue(
                    DownloadPlanErrorCode.PathEscape,
                    $"{dest}: '..' path segment is not allowed");
            }

            if (Encoding.UTF8.GetByteCount(segment) > DownloadPlanLimits.MaxDestSegmentBytes)
            {
                return new DownloadPlanIssue(
                    DownloadPlanErrorCode.InvalidPlan,
                    $"{dest}: path segment longer than {DownloadPlanLimits.MaxDestSegmentBytes} bytes");
            }

            if (segment[^1] is '.' or ' ')
            {
                return new DownloadPlanIssue(
                    DownloadPlanErrorCode.InvalidPlan,
                    $"{dest}: segment ends with a dot or space");
            }

            if (segment.Contains(':'))
            {
                return new DownloadPlanIssue(
                    DownloadPlanErrorCode.InvalidPlan,
                    $"{dest}: ':' is not allowed (Windows ADS)");
            }

            foreach (var character in segment)
            {
                if (character < 0x20)
                {
                    return new DownloadPlanIssue(
                        DownloadPlanErrorCode.InvalidPlan,
                        $"{dest}: control characters are not allowed");
                }
            }

            if (IsWindowsReservedName(segment))
            {
                return new DownloadPlanIssue(
                    DownloadPlanErrorCode.InvalidPlan,
                    $"{dest}: '{segment}' is a reserved name");
            }
        }

        var fileName = parts[^1];
        var lowerName = fileName.ToLowerInvariant();
        if (lowerName == ".part"
            || lowerName.EndsWith(".part", StringComparison.Ordinal)
            || lowerName.EndsWith(".part.meta", StringComparison.Ordinal)
            || lowerName.EndsWith(".part.lock", StringComparison.Ordinal))
        {
            return new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                $"{dest}: dest may not target a downloader sidecar file");
        }

        normalized = string.Join('/', parts);
        return null;
    }

    /// <summary>
    /// True for a bare file name (no separators, not a dot entry). Resource
    /// providers require this because the file name is appended to a folder.
    /// </summary>
    public static bool IsPlainFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name is "." or ".."
            || name.Contains('/')
            || name.Contains('\\')
            || name.Contains('\0')
            || name.Contains(':'))
        {
            return false;
        }

        return TryNormalizeDest(name, out _) is null;
    }

    /// <summary>Validates an absolute download root.</summary>
    public static DownloadPlanIssue? ValidateRoot(string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return new DownloadPlanIssue(DownloadPlanErrorCode.InvalidPlan, "root is required");
        }

        if (root.Contains('\0'))
        {
            return new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                "root contains NUL");
        }

        if (!Path.IsPathRooted(root))
        {
            return new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                $"root must be an absolute path: {root}");
        }

        return null;
    }

    /// <summary>
    /// Validates a primary URL or mirror. HTTPS is required; loopback HTTP is
    /// accepted so integration tests can use a local server, matching Rust.
    /// </summary>
    public static DownloadPlanIssue? TryValidateUrl(string? raw, out Uri? uri)
    {
        uri = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return new DownloadPlanIssue(DownloadPlanErrorCode.InvalidPlan, "URL is required");
        }

        if (raw.Length > DownloadPlanLimits.MaxUrlLength)
        {
            return new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                "URL is too long");
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var parsed))
        {
            return new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                $"invalid URL '{raw}'");
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo))
        {
            return new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                $"URL userinfo is not allowed: {parsed}");
        }

        if (string.IsNullOrEmpty(parsed.Host))
        {
            return new DownloadPlanIssue(
                DownloadPlanErrorCode.InvalidPlan,
                $"URL has no host: {parsed}");
        }

        switch (parsed.Scheme)
        {
            case "https":
                uri = parsed;
                return null;
            case "http" when IsLoopback(parsed):
                uri = parsed;
                return null;
            default:
                return new DownloadPlanIssue(
                    DownloadPlanErrorCode.UnsupportedScheme,
                    $"{parsed.Scheme} is not allowed ({parsed}); " +
                    "only HTTPS (or loopback HTTP for tests)");
        }
    }

    /// <summary>True for localhost, 127.0.0.0/8 and ::1.</summary>
    public static bool IsLoopback(Uri uri)
    {
        if (uri.Host.Length == 0)
        {
            return false;
        }

        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var bare = uri.Host.TrimStart('[').TrimEnd(']');
        return IPAddress.TryParse(bare, out var address) && IPAddress.IsLoopback(address);
    }

    /// <summary>
    /// Windows device names: <c>CON</c>, <c>PRN</c>, <c>AUX</c>, <c>NUL</c>,
    /// <c>COM1</c>..<c>COM9</c>, <c>LPT1</c>..<c>LPT9</c> (with or without an
    /// extension). Matches <c>is_windows_reserved</c>.
    /// </summary>
    public static bool IsWindowsReservedName(string segment)
    {
        var dot = segment.IndexOf('.');
        var baseName = dot >= 0 ? segment[..dot] : segment;
        if (baseName.Length == 0)
        {
            return false;
        }

        var upper = baseName.ToUpperInvariant();
        if (upper is "CON" or "PRN" or "AUX" or "NUL")
        {
            return true;
        }

        return upper.Length == 4
            && (upper.StartsWith("COM", StringComparison.Ordinal)
                || upper.StartsWith("LPT", StringComparison.Ordinal))
            && upper[3] is >= '1' and <= '9';
    }

    private static string Printable(string value)
        => value.Replace("\0", "\\0", StringComparison.Ordinal);
}
