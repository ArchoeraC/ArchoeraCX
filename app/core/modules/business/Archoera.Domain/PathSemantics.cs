namespace Archoera.Domain;

/// <summary>
/// Case-sensitivity rules for on-disk paths, used by installs, repositories,
/// launch file checks, planners and cleanup. Comparison is case-insensitive
/// on Windows and macOS (their file systems fold case) and ordinal elsewhere.
/// Everything that compares two stored paths must go through
/// <see cref="Comparer"/> or <see cref="Comparison"/> so the behaviour cannot
/// drift between call sites.
/// </summary>
public static class PathSemantics
{
    /// <summary>Comparer for paths stored in records, dictionaries and sets.</summary>
    public static StringComparer Comparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    /// <summary>
    /// Comparison for <c>string.Equals</c>/<c>StartsWith</c> on full paths.
    /// </summary>
    public static StringComparison Comparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
