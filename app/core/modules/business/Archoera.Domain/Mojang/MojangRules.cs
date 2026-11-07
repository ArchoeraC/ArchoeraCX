using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace Archoera.Domain.Mojang;

/// <summary>Action of a Mojang rule entry.</summary>
public enum MojangRuleAction
{
    Allow,
    Disallow,
}

/// <summary>
/// The <c>os</c> condition of a Mojang rule. Values are kept as strings because
/// Mojang has used several spellings over time; matching normalizes aliases.
/// </summary>
public sealed record MojangRuleOs(string? Name, string? Arch, string? Version)
{
    /// <summary>True when every populated condition matches <paramref name="platform"/>.</summary>
    public bool Matches(MojangPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);

        if (!string.IsNullOrWhiteSpace(Name) && !MatchesOsName(Name, platform.Os))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(Arch) && !MatchesArch(Arch, platform.Arch))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(Version)
            && !MatchesOsVersion(Version, platform.OsVersion))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesOsName(string name, MojangSystemOs os)
    {
        var normalized = name.Trim().ToLowerInvariant();
        if (normalized == "macos")
        {
            normalized = "osx";
        }

        var expected = os switch
        {
            MojangSystemOs.Linux => "linux",
            MojangSystemOs.Windows => "windows",
            MojangSystemOs.MacOs => "osx",
            _ => string.Empty,
        };

        return string.Equals(normalized, expected, StringComparison.Ordinal);
    }

    private static bool MatchesArch(string arch, MojangSystemArch actual)
    {
        var normalized = arch.Trim().ToLowerInvariant();
        var expected = normalized switch
        {
            "x86" or "i386" or "i486" or "i586" or "i686" or "32" => MojangSystemArch.X86,
            "x86_64" or "x64" or "amd64" or "64" => MojangSystemArch.X64,
            "arm64" or "aarch64" => MojangSystemArch.Arm64,
            "arm" or "arm32" => MojangSystemArch.Arm,
            _ => (MojangSystemArch?)null,
        };

        return expected == actual;
    }

    private static bool MatchesOsVersion(string pattern, string? osVersion)
    {
        if (string.IsNullOrWhiteSpace(osVersion))
        {
            return false;
        }

        try
        {
            return Regex.IsMatch(
                osVersion,
                pattern,
                RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException)
        {
            // A malformed rule cannot match anything; metadata validation keeps
            // this from silently enabling a library.
            return false;
        }
    }
}

/// <summary>
/// One <c>rules[]</c> entry. <see cref="Features"/> requires every listed flag
/// to equal the platform's value (missing flags are <see langword="false"/>).
/// </summary>
public sealed record MojangRule(
    MojangRuleAction Action,
    MojangRuleOs? Os = null,
    IReadOnlyDictionary<string, bool>? Features = null)
{
    private static readonly IReadOnlyDictionary<string, bool> NoFeatures =
        new ReadOnlyDictionary<string, bool>(
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

    /// <summary>True when all populated conditions match.</summary>
    public bool Matches(MojangPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);

        if (Os is not null && !Os.Matches(platform))
        {
            return false;
        }

        var features = Features ?? NoFeatures;
        foreach (var (name, expected) in features)
        {
            var actual = platform.HasFeature(name);
            if (actual != expected)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Mojang rule evaluation: no rules means allow; otherwise the default is
/// deny and every matching rule replaces the result, so the last matching rule
/// wins (docs/06 §1.3).
/// </summary>
public static class MojangRuleEvaluator
{
    /// <summary>Evaluates a library/argument rule list for <paramref name="platform"/>.</summary>
    public static bool IsAllowed(
        IReadOnlyList<MojangRule>? rules,
        MojangPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);

        if (rules is null || rules.Count == 0)
        {
            return true;
        }

        var allowed = false;
        foreach (var rule in rules)
        {
            if (rule is null || !rule.Matches(platform))
            {
                continue;
            }

            allowed = rule.Action == MojangRuleAction.Allow;
        }

        return allowed;
    }
}
