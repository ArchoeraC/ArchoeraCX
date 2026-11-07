using System.Text.Json;

namespace Archoera.Tests;

/// <summary>
/// JSON-encodes strings embedded in hand-written test documents. Windows paths
/// contain backslashes, which are escape characters in JSON and would make the
/// whole document invalid (and silently fall back to defaults) when interpolated
/// without quoting.
/// </summary>
internal static class TestJson
{
    internal static string Quote(string value) => JsonSerializer.Serialize(value);
}
