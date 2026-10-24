namespace Archoera.Domain.Settings;

/// <summary>
/// Limits and safety rules for the environment a launch passes to the child
/// JVM. The same rules guard the global settings document, per-instance
/// overrides and the final child environment, so a value accepted by one
/// entry point is also accepted by the others.
///
/// <para>The hostile variables are removed unconditionally: they let a parent
/// process (or a hand-edited legacy file) inject JVM options after the
/// launcher has decided the command line, which defeats the argument preview
/// and can break the game with no visible cause.</para>
/// </summary>
public static class LaunchEnvironmentRules
{
    /// <summary>Maximum length of <c>wrapperCommand</c>.</summary>
    public const int MaxWrapperLength = 4096;

    /// <summary>Maximum number of environment entries (global or per instance).</summary>
    public const int MaxEntries = 64;

    /// <summary>Maximum length of an environment variable name.</summary>
    public const int MaxKeyLength = 128;

    /// <summary>Maximum length of an environment variable value.</summary>
    public const int MaxValueLength = 4096;

    /// <summary>
    /// Variables the launcher deliberately strips from the child JVM. Matching
    /// is ordinal on Unix and ordinal-ignore-case on Windows, exactly like the
    /// OS environment block.
    /// </summary>
    public static readonly string[] HostileNames =
    [
        "JAVA_TOOL_OPTIONS",
        "_JAVA_OPTIONS",
        "JDK_JAVA_OPTIONS",
        "CLASSPATH",
    ];

    /// <summary>
    /// Comparer for environment dictionaries. Windows environment names are
    /// case-insensitive; Unix keeps keys distinct.
    /// </summary>
    public static StringComparer Comparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>True when the child environment must not contain <paramref name="name"/>.</summary>
    public static bool IsHostile(string name)
    {
        foreach (var hostile in HostileNames)
        {
            if (Comparer.Equals(hostile, name)) return true;
        }

        return false;
    }

    /// <summary>An empty environment map using the platform comparer.</summary>
    public static Dictionary<string, string> CreateMap() => new(Comparer);

    /// <summary>
    /// Validates a wrapper command: length and line/control characters. An
    /// empty value means "no wrapper" and is normalized to <c>null</c>.
    /// Tokenization is validated by the caller because the tokenizer lives in
    /// the infrastructure layer.
    /// </summary>
    public static string? NormalizeWrapper(string? wrapper)
    {
        if (string.IsNullOrWhiteSpace(wrapper)) return null;
        if (wrapper.Length > MaxWrapperLength)
        {
            throw new ArgumentException(
                $"wrapperCommand must be at most {MaxWrapperLength} characters");
        }

        if (wrapper.Contains('\0', StringComparison.Ordinal) ||
            wrapper.Contains('\r', StringComparison.Ordinal) ||
            wrapper.Contains('\n', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "wrapperCommand must not contain NUL or line breaks");
        }

        return wrapper;
    }

    /// <summary>Validates one environment variable name.</summary>
    public static void ValidateKey(string key)
    {
        if (!IsValidName(key))
        {
            throw new ArgumentException(
                $"environment variable name '{key}' must match ^[A-Za-z_][A-Za-z0-9_]*$ and be at most {MaxKeyLength} characters");
        }

        if (IsHostile(key))
        {
            throw new ArgumentException(
                $"environment must not set {key}; the launcher strips it for safety");
        }
    }

    /// <summary>
    /// True for <c>^[A-Za-z_][A-Za-z0-9_]*$</c> within the length limit.
    /// Does not check the hostile list so lenient readers can keep a legacy
    /// value for diagnostics while the launcher still strips it.
    /// </summary>
    public static bool IsValidName(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength) return false;
        if (!char.IsAsciiLetter(key[0]) && key[0] != '_') return false;
        foreach (var character in key)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '_') return false;
        }

        return true;
    }

    /// <summary>Validates one environment variable value.</summary>
    public static void ValidateValue(string key, string value)
    {
        if (value.Length > MaxValueLength)
        {
            throw new ArgumentException(
                $"environment value for '{key}' must be at most {MaxValueLength} characters");
        }

        if (value.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"environment value for '{key}' must not contain NUL");
        }
    }

    /// <summary>
    /// Merges environment layers in order; later layers win. Hostile names are
    /// removed from the result so they can never reach the child JVM.
    /// </summary>
    public static Dictionary<string, string> Merge(
        params IReadOnlyDictionary<string, string>?[] layers)
    {
        var result = CreateMap();
        foreach (var layer in layers)
        {
            if (layer is null) continue;
            foreach (var pair in layer)
            {
                result[pair.Key] = pair.Value;
            }
        }

        foreach (var hostile in HostileNames)
        {
            result.Remove(hostile);
        }

        return result;
    }
}
