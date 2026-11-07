using System.Globalization;
using System.Runtime.InteropServices;
using Archoera.Domain.Java;
using Archoera.Infrastructure.Processes;

namespace Archoera.Infrastructure.Java;

/// <summary>
/// Validates a candidate and extracts its version metadata using
/// <c>java -XshowSettings:properties -version</c>.
/// </summary>
internal static class JavaProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private static readonly string[] PropertyArguments = ["-XshowSettings:properties", "-version"];
    private static readonly string[] VersionArguments = ["-version"];

    public static JavaRuntimeInfo? TryProbe(JavaCandidate candidate)
    {
        try
        {
            var result = ProcessRunner.Run(candidate.ExecutablePath, PropertyArguments, ProbeTimeout);
            if (result is null)
            {
                return null;
            }

            var output = Combine(result);
            var properties = ParseProperties(output);

            var version = Get(properties, "java.version");
            var home = Get(properties, "java.home");
            var vendor = Get(properties, "java.vendor");
            var arch = Get(properties, "os.arch");

            if (version is null)
            {
                // Older/partial JVMs may not print properties. The regular version
                // banner is enough to populate the remaining fields.
                var fallback = ProcessRunner.Run(candidate.ExecutablePath, VersionArguments, ProbeTimeout);
                if (fallback is null)
                {
                    return null;
                }

                var fallbackOutput = Combine(fallback);
                version = ParseBannerVersion(fallbackOutput);
                if (version is null)
                {
                    return null;
                }

                output = fallbackOutput;
            }

            vendor ??= InferVendor(output);
            arch ??= InferArch(output);

            return new JavaRuntimeInfo(
                candidate.ExecutablePath,
                version,
                ParseMajor(version),
                vendor,
                arch,
                candidate.Source,
                home);
        }
        catch
        {
            // A candidate that cannot be probed is simply not a valid runtime.
            return null;
        }
    }

    private static string Combine(ProcessResult result)
        => string.Concat(result.StandardOutput, "\n", result.StandardError);

    private static string? Get(IReadOnlyDictionary<string, string> properties, string key)
        => properties.TryGetValue(key, out var value) && value.Length > 0 ? value : null;

    private static Dictionary<string, string> ParseProperties(string text)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            if (key is not ("java.version" or "java.home" or "java.vendor" or "os.arch"))
            {
                continue;
            }

            var value = line[(separator + 1)..].Trim();
            if (value.Length > 0)
            {
                // First value wins; avoid a later banner line overriding it.
                properties.TryAdd(key, value);
            }
        }

        return properties;
    }

    private static string? ParseBannerVersion(string text)
    {
        const string quotedMarker = "version \"";
        var index = text.IndexOf(quotedMarker, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            var start = index + quotedMarker.Length;
            var end = text.IndexOf('"', start);
            if (end > start)
            {
                return text[start..end].Trim();
            }
        }

        const string plainMarker = "version ";
        index = text.IndexOf(plainMarker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var tokenStart = index + plainMarker.Length;
        var tokenEnd = tokenStart;
        while (tokenEnd < text.Length && !char.IsWhiteSpace(text[tokenEnd]) && text[tokenEnd] != '"')
        {
            tokenEnd++;
        }

        return tokenEnd > tokenStart ? text[tokenStart..tokenEnd].Trim() : null;
    }

    internal static int ParseMajor(string version)
    {
        var span = version.AsSpan().Trim();

        var start = 0;
        while (start < span.Length && !char.IsAsciiDigit(span[start]))
        {
            start++;
        }

        if (start == span.Length)
        {
            return 0;
        }

        var end = start;
        while (end < span.Length && char.IsAsciiDigit(span[end]))
        {
            end++;
        }

        if (!int.TryParse(span[start..end], NumberStyles.None, CultureInfo.InvariantCulture, out var first))
        {
            return 0;
        }

        if (first != 1)
        {
            return first;
        }

        // Legacy "1.8.0_402" style versions report 8 as the feature release.
        var relativeDot = span[end..].IndexOf('.');
        if (relativeDot < 0)
        {
            return first;
        }

        var secondStart = end + relativeDot + 1;
        var secondEnd = secondStart;
        while (secondEnd < span.Length && char.IsAsciiDigit(span[secondEnd]))
        {
            secondEnd++;
        }

        return secondEnd > secondStart &&
               int.TryParse(span[secondStart..secondEnd], NumberStyles.None, CultureInfo.InvariantCulture, out var second)
            ? second
            : first;
    }

    private static string InferVendor(string text)
    {
        if (text.Contains("Eclipse Adoptium", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Temurin", StringComparison.OrdinalIgnoreCase))
        {
            return "Eclipse Adoptium";
        }

        if (text.Contains("AdoptOpenJDK", StringComparison.OrdinalIgnoreCase))
        {
            return "AdoptOpenJDK";
        }

        if (text.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
        {
            return "Microsoft";
        }

        if (text.Contains("Zulu", StringComparison.OrdinalIgnoreCase))
        {
            return "Azul Systems, Inc.";
        }

        if (text.Contains("Corretto", StringComparison.OrdinalIgnoreCase))
        {
            return "Amazon.com Inc.";
        }

        if (text.Contains("Semeru", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("IBM", StringComparison.OrdinalIgnoreCase))
        {
            return "IBM";
        }

        if (text.Contains("GraalVM", StringComparison.OrdinalIgnoreCase))
        {
            return "GraalVM";
        }

        if (text.Contains("Oracle", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Java(TM)", StringComparison.OrdinalIgnoreCase))
        {
            return "Oracle Corporation";
        }

        if (text.Contains("OpenJDK", StringComparison.OrdinalIgnoreCase))
        {
            return "OpenJDK";
        }

        return "Unknown";
    }

    private static string InferArch(string text)
    {
        if (text.Contains("aarch64", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("arm64", StringComparison.OrdinalIgnoreCase))
        {
            return "aarch64";
        }

        if (text.Contains("64-Bit", StringComparison.OrdinalIgnoreCase))
        {
            return "amd64";
        }

        if (text.Contains("32-Bit", StringComparison.OrdinalIgnoreCase))
        {
            return "x86";
        }

        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "amd64",
            Architecture.Arm64 => "aarch64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            _ => "unknown",
        };
    }
}
