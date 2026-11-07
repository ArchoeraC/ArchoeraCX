using System.Text.RegularExpressions;

namespace Archoera.Infrastructure.Java.Distributions;

/// <summary>
/// Parsed JDK version used for NVD CPE matching. Vendors mix several shapes
/// (<c>21.0.13+9</c>, <c>21.0.13.0.3</c>, <c>8.0.402+6</c>,
/// <c>1.8.0_402</c>, <c>25i4-25.0.4.1.1</c>), so the parser keeps only the
/// numeric feature/update segments and a Java 8 update number.
/// </summary>
internal readonly partial record struct JavaCveVersion(
    int Major,
    IReadOnlyList<int> Segments,
    int? Update)
{
    public static JavaCveVersion Parse(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0) return new JavaCveVersion(0, [0], null);

        // Oracle's Java 8 CPE uses "1.8.0" plus a separate update field.
        var legacy = LegacyJava8().Match(text);
        if (legacy.Success &&
            int.TryParse(legacy.Groups["update"].Value, out var legacyUpdate))
        {
            return new JavaCveVersion(8, [8], legacyUpdate);
        }

        // Vendor-native Java 8 spelling: 8u402.
        var shortUpdate = ShortJava8().Match(text);
        if (shortUpdate.Success &&
            int.TryParse(shortUpdate.Groups["major"].Value, out var shortMajor) &&
            int.TryParse(shortUpdate.Groups["update"].Value, out var shortPatch))
        {
            return new JavaCveVersion(shortMajor, [shortMajor], shortPatch);
        }

        var match = DottedVersion().Matches(text);
        if (match.Count > 0)
        {
            // GraalVM uses "25i4-25.0.4.1.1"; the last dotted token is the JDK
            // version and the prefix only describes the GraalVM release line.
            var token = match[^1];
            var segments = ParseSegments(token.Groups["version"].Value);
            // Oracle writes Java 8 as 1.8.0 in CPEs; treat it as feature 8.
            if (segments.Count >= 3 &&
                segments[0] == 1 &&
                segments[1] is >= 7 and <= 8 &&
                segments[2] == 0)
            {
                var dottedUpdate = segments.Count >= 4 ? segments[3] : (int?)null;
                return new JavaCveVersion(
                    segments[1],
                    [segments[1]],
                    dottedUpdate);
            }

            var major = segments.Count > 0 ? segments[0] : 0;
            int? update = null;
            if (major == 8 && segments.Count >= 3) update = segments[2];
            return new JavaCveVersion(major, segments, update);
        }

        var majorOnly = MajorOnly().Match(text);
        if (majorOnly.Success &&
            int.TryParse(majorOnly.Groups["major"].Value, out var parsedMajor))
        {
            return new JavaCveVersion(parsedMajor, [parsedMajor], null);
        }

        return new JavaCveVersion(0, [0], null);
    }

    /// <summary>
    /// Compares only the segments both sides publish. Vendor build suffixes
    /// (for example Corretto's <c>21.0.11.9.1</c>) must not move a build past
    /// the Oracle CPU version it derives from.
    /// </summary>
    public int ComparePrefix(JavaCveVersion other)
    {
        if (Major != other.Major) return Major < other.Major ? -1 : 1;

        if (Major == 8 && (Update.HasValue || other.Update.HasValue))
        {
            var left = Update ?? 0;
            var right = other.Update ?? 0;
            if (left != right) return left < right ? -1 : 1;
            return 0;
        }

        var length = Math.Min(Segments.Count, other.Segments.Count);
        for (var index = 0; index < length; index++)
        {
            var left = Segments[index];
            var right = other.Segments[index];
            if (left != right) return left < right ? -1 : 1;
        }

        return 0;
    }

    /// <summary>
    /// NVD lists the last affected version for Oracle CPUs (for example
    /// <c>21.0.11</c>) rather than an explicit range. A build at or below that
    /// version is treated as affected; a feature-only CPE (<c>21</c>) only
    /// matches the GA build, not every later update.
    /// </summary>
    public bool IsAtOrBelowExact(JavaCveVersion bound)
    {
        // A CPU lists one exact CPE per affected feature release (21.0.11,
        // 25.0.3, 26.0.1). Lower majors must not inherit a later major's CPE.
        if (Major != bound.Major) return false;
        if (ComparePrefix(bound) > 0) return false;

        if (bound.Major == 8 && (bound.Update.HasValue || Update.HasValue))
        {
            return (Update ?? 0) <= (bound.Update ?? 0);
        }

        if (bound.Segments.Count >= 3) return true;

        // A short CPE version such as "21" or "21.0" describes the GA build.
        // Treat non-zero trailing segments as a later update, not as the same
        // build, so the latest patch is not flagged by an old GA advisory.
        for (var index = bound.Segments.Count; index < Segments.Count; index++)
        {
            if (Segments[index] != 0) return false;
        }

        return true;
    }

    /// <summary>
    /// Full comparison for CPE ranges. Missing trailing segments are zeros,
    /// so the GA build <c>21</c> is correctly below the bound <c>21.0.2</c>.
    /// </summary>
    public int CompareFull(JavaCveVersion other)
    {
        if (Major != other.Major) return Major < other.Major ? -1 : 1;

        if (Major == 8 && (Update.HasValue || other.Update.HasValue))
        {
            var left = Update ?? 0;
            var right = other.Update ?? 0;
            if (left != right) return left < right ? -1 : 1;
            return 0;
        }

        var length = Math.Max(Segments.Count, other.Segments.Count);
        for (var index = 0; index < length; index++)
        {
            var left = index < Segments.Count ? Segments[index] : 0;
            var right = index < other.Segments.Count ? other.Segments[index] : 0;
            if (left != right) return left < right ? -1 : 1;
        }

        return 0;
    }

    public bool IsBelow(JavaCveVersion bound) => CompareFull(bound) < 0;
    public bool IsAtOrAbove(JavaCveVersion bound) => CompareFull(bound) >= 0;
    public bool IsAbove(JavaCveVersion bound) => CompareFull(bound) > 0;

    private static IReadOnlyList<int> ParseSegments(string value)
    {
        var parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var segments = new List<int>(Math.Min(parts.Length, 8));
        foreach (var part in parts)
        {
            if (segments.Count == 8) break;
            segments.Add(int.TryParse(part, out var parsed) ? parsed : 0);
        }

        return segments.Count == 0 ? [0] : segments;
    }

    [GeneratedRegex(@"(?<!\d)1\.(?<major>\d+)\.0[_-](?<update>\d+)")]
    private static partial Regex LegacyJava8();

    [GeneratedRegex(@"(?<!\d)(?<major>\d+)[uU](?<update>\d+)(?!\d)")]
    private static partial Regex ShortJava8();

    [GeneratedRegex(@"(?<!\d)(?<version>\d+(?:\.\d+)+)(?:\+\d+)?")]
    private static partial Regex DottedVersion();

    [GeneratedRegex(@"(?<!\d)(?<major>\d+)(?:\+\d+)?(?!\d)")]
    private static partial Regex MajorOnly();
}

/// <summary>A single NVD CPE match entry, reduced to what Java matching needs.</summary>
internal sealed record JavaCpeMatch(
    string Criteria,
    string Product,
    string Version,
    string? Update,
    bool Vulnerable,
    string? VersionStartIncluding,
    string? VersionStartExcluding,
    string? VersionEndIncluding,
    string? VersionEndExcluding)
{
    /// <summary>
    /// Builds a match from a feed that publishes affected version strings
    /// instead of CPE ranges. The criteria remains a valid CPE so the match
    /// can be written to the shared disk cache and read back.
    /// </summary>
    public static JavaCpeMatch Synthetic(string product, string version)
    {
        var parts = product.Split(':', 2);
        var safeVersion = version.Replace(':', '_').Replace(' ', '_');
        var criteria = $"cpe:2.3:a:{parts[0]}:{parts[1]}:{safeVersion}:" +
                       "*:*:*:*:*:*:*";
        return new JavaCpeMatch(
            criteria,
            product,
            version,
            null,
            true,
            null,
            null,
            null,
            null);
    }

    public static JavaCpeMatch? Parse(
        string? criteria,
        bool vulnerable,
        string? versionStartIncluding,
        string? versionStartExcluding,
        string? versionEndIncluding,
        string? versionEndExcluding)
    {
        if (string.IsNullOrWhiteSpace(criteria)) return null;
        var parts = criteria.Split(':');
        if (parts.Length < 6 || parts[0] != "cpe") return null;
        if (parts[2] != "a") return null;

        return new JavaCpeMatch(
            criteria,
            $"{parts[3]}:{parts[4]}",
            parts[5],
            parts.Length > 6 ? parts[6] : null,
            vulnerable,
            versionStartIncluding,
            versionStartExcluding,
            versionEndIncluding,
            versionEndExcluding);
    }

    public bool Matches(JavaCveVersion version, string product)
    {
        if (!Vulnerable) return false;
        if (!string.Equals(Product, product, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var startIncluding = Bound(VersionStartIncluding);
        var startExcluding = Bound(VersionStartExcluding);
        var endIncluding = Bound(VersionEndIncluding);
        var endExcluding = Bound(VersionEndExcluding);

        if (startIncluding is { } start &&
            version.IsBelow(start))
        {
            return false;
        }

        if (startExcluding is { } afterStart &&
            !version.IsAbove(afterStart))
        {
            return false;
        }

        if (endIncluding is { } endInclusive &&
            version.IsAbove(endInclusive))
        {
            return false;
        }

        if (endExcluding is { } endExclusive &&
            version.IsAtOrAbove(endExclusive))
        {
            return false;
        }

        if (startIncluding is not null ||
            startExcluding is not null ||
            endIncluding is not null ||
            endExcluding is not null)
        {
            return true;
        }

        // A bare "*" means NVD could not enumerate versions (common for very
        // old Java entries). Treating it as "every build" would flag every
        // current JDK with ancient CVEs, so only explicit ranges count.
        if (Version is "*") return false;
        if (Version is "-" or "") return false;

        var exact = JavaCveVersion.Parse(Version);
        if (Update is not null &&
            Update is not "*" and not "-" &&
            TryParseUpdate(Update, out var update))
        {
            exact = exact with { Update = update };
            if (exact.Major == 8)
            {
                exact = exact with { Segments = [8] };
            }
        }

        return version.IsAtOrBelowExact(exact);
    }

    private static JavaCveVersion? Bound(string? value)
        => string.IsNullOrWhiteSpace(value) || value is "*" or "-"
            ? null
            : JavaCveVersion.Parse(value);

    private static bool TryParseUpdate(string value, out int update)
    {
        // "update471_b50" is update 471 build 50; concatenating every digit
        // would read it as update 47150 and match every later Java 8 build.
        var digits = new string(
            value
                .SkipWhile(character => !char.IsAsciiDigit(character))
                .TakeWhile(char.IsAsciiDigit)
                .ToArray());
        return int.TryParse(digits, out update);
    }
}

/// <summary>Normalizes CVSS severities and provides a stable sort order.</summary>
internal static class JavaCveSeverity
{
    public static string Normalize(string? value, double? score)
    {
        var text = value?.Trim().ToLowerInvariant();
        return text switch
        {
            "critical" => "critical",
            "high" => "high",
            "medium" => "medium",
            "low" => "low",
            "none" => "none",
            _ => FromScore(score),
        };
    }

    public static int Rank(string severity) => severity switch
    {
        "critical" => 4,
        "high" => 3,
        "medium" => 2,
        "low" => 1,
        "none" => 0,
        _ => -1,
    };

    private static string FromScore(double? score) => score switch
    {
        >= 9.0 => "critical",
        >= 7.0 => "high",
        >= 4.0 => "medium",
        > 0.0 => "low",
        0.0 => "none",
        _ => "unknown",
    };
}
