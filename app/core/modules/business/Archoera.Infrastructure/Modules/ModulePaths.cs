using System.Text.Json;

namespace Archoera.Infrastructure.Modules;

/// <summary>
/// Native artifact directory reported by the C++ hub at module init.
///
/// WHY this exists: .NET sets <c>AppContext.BaseDirectory</c> to the host
/// executable directory when the business module is loaded as a NativeAOT
/// shared library. That is <c>flutter_tester</c> in tests and
/// <c>Contents/MacOS</c> in a macOS bundle, not the directory containing
/// <c>archoera-dl</c>, <c>archoera-vault</c> and <c>e_sqlite3</c>. The hub
/// knows the real location (it loaded the core from there), so it reports it
/// as <c>coreDir</c> in the module config and the C# side probes it first.
/// </summary>
internal static class ModulePaths
{
    private static string? _nativeDirectory;

    /// <summary>Trusted directory reported by the hub, or null when absent.</summary>
    internal static string? NativeDirectory => _nativeDirectory;

    /// <summary>Applies the <c>coreDir</c> field from the opaque module config.</summary>
    internal static void ConfigureFromJson(string? configJson)
    {
        if (ParseNativeDirectory(configJson) is { } directory)
        {
            _nativeDirectory = directory;
        }
    }

    /// <summary>
    /// Parses and normalizes <c>coreDir</c>. Kept pure so the malformed-input
    /// paths can be tested without mutating process-wide state.
    /// </summary>
    internal static string? ParseNativeDirectory(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(configJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("coreDir", out var element) ||
                element.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var value = element.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
