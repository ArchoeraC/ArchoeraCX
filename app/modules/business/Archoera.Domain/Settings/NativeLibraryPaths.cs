namespace Archoera.Domain.Settings;

/// <summary>
/// Validation for the developer-mode native-library override paths. Saving a
/// path is strict (it must be absolute and well formed), while loading and
/// launching are lenient: a hand-edited or stale value is dropped instead of
/// preventing the launcher from starting. Whether the file actually exists is
/// intentionally not checked here, because the save and launch moments differ.
/// </summary>
public static class NativeLibraryPaths
{
    /// <summary>Maximum accepted path length in characters.</summary>
    public const int MaxLength = 4096;

    /// <summary>
    /// Normalizes a user-supplied path for storage: whitespace-only becomes
    /// <c>null</c>, relative or malformed values throw
    /// <see cref="ArgumentException"/>. Existence is not required.
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Length > MaxLength)
        {
            throw new ArgumentException(
                $"native library path must be at most {MaxLength} characters");
        }

        if (trimmed.Contains('\0', StringComparison.Ordinal) ||
            trimmed.Contains('\r', StringComparison.Ordinal) ||
            trimmed.Contains('\n', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "native library path must not contain NUL or line breaks");
        }

        if (!Path.IsPathFullyQualified(trimmed))
        {
            throw new ArgumentException("native library path must be absolute");
        }

        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException or
            System.Security.SecurityException)
        {
            throw new ArgumentException(
                $"native library path is not usable: {exception.Message}",
                exception);
        }
    }

    /// <summary>
    /// Lenient variant used when reading settings or building a launch: any
    /// invalid value becomes <c>null</c> and is therefore ignored.
    /// </summary>
    public static string? TryNormalize(string? value)
    {
        try
        {
            return Normalize(value);
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or NotSupportedException or
            System.Security.SecurityException)
        {
            return null;
        }
    }
}
