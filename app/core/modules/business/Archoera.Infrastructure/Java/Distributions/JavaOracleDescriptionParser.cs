using System.Text.RegularExpressions;

namespace Archoera.Infrastructure.Java.Distributions;

/// <summary>
/// Extracts affected versions from Oracle CPU descriptions such as
/// "Supported versions that are affected are Oracle Java SE: 8u511, 11.0.33,
/// 21.0.13; Oracle GraalVM for JDK: 21.0.13". NVD keyword-search responses can
/// include CVEs whose CPE configurations are not published yet; the
/// description still carries the affected versions.
/// </summary>
internal static partial class JavaOracleDescriptionParser
{
    public static IReadOnlyList<JavaCpeMatch> Parse(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return [];

        var matches = new List<JavaCpeMatch>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match section in SectionPattern().Matches(description))
        {
            var product = ProductId(section.Groups["product"].Value);
            if (product is null) continue;
            foreach (var raw in VersionSeparator().Split(section.Groups["versions"].Value))
            {
                var token = raw.Trim().TrimEnd(',', ';', '.');
                if (token.Length == 0) continue;
                var parsed = JavaCveVersion.Parse(token);
                if (parsed.Major is < 8 or > 99) continue;
                var key = $"{product}|{token}";
                if (!seen.Add(key)) continue;
                matches.Add(JavaCpeMatch.Synthetic(product, token));
            }
        }

        return matches;
    }

    private static string? ProductId(string product)
    {
        var text = product.Trim().ToLowerInvariant();
        if (text.Contains("graalvm", StringComparison.Ordinal))
        {
            if (text.Contains("enterprise", StringComparison.Ordinal) ||
                !text.Contains("jdk", StringComparison.Ordinal))
            {
                return null;
            }

            return "oracle:graalvm_for_jdk";
        }

        return text.Contains("java se", StringComparison.Ordinal)
            ? "oracle:jdk"
            : null;
    }

    [GeneratedRegex(
        @"(?<product>Oracle Java SE|Oracle GraalVM for JDK|Oracle GraalVM Enterprise Edition|Java SE JDK and JRE)\s*:\s*(?<versions>[^;]+)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SectionPattern();

    [GeneratedRegex(
        @"\s*(?:,|/|\band\b)\s*",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex VersionSeparator();
}
