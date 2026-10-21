using System.Text.Json;

namespace Archoera.Application.Java;

/// <summary>
/// Parses the optional <c>java.scan</c> params object. The parser is strict
/// about JSON types (so a caller bug surfaces as <c>E_INVALID</c>) but ignores
/// unknown properties for forward compatibility.
/// </summary>
public static class JavaScanRequestJson
{
    /// <summary>
    /// Parses <paramref name="json"/> as a UTF-8 JSON object. A missing or empty
    /// payload yields the default request. Returns <see langword="false"/> for
    /// malformed JSON, a non-object root, or a known property with the wrong type.
    /// </summary>
    public static bool TryParse(string? json, out JavaScanRequest request)
    {
        request = JavaScanRequest.Default;

        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return TryParse(document.RootElement, out request);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Parses an already-read JSON value. Used by the NativeAOT export layer.
    /// </summary>
    public static bool TryParse(JsonElement root, out JavaScanRequest request)
    {
        request = JavaScanRequest.Default;

        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var roots = Array.Empty<string>();
        if (root.TryGetProperty("roots", out var rootsElement))
        {
            if (!TryReadStringArray(rootsElement, out roots))
            {
                return false;
            }
        }

        var executables = Array.Empty<string>();
        if (root.TryGetProperty("executables", out var executablesElement))
        {
            if (!TryReadStringArray(executablesElement, out executables))
            {
                return false;
            }
        }

        var includeDefaults = true;
        if (root.TryGetProperty("includeDefaults", out var includeElement))
        {
            if (includeElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return false;
            }

            includeDefaults = includeElement.GetBoolean();
        }

        request = new JavaScanRequest
        {
            Roots = roots,
            Executables = executables,
            IncludeDefaults = includeDefaults,
        };

        return true;
    }

    private static bool TryReadStringArray(JsonElement element, out string[] values)
    {
        values = [];

        if (element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var length = element.GetArrayLength();
        var buffer = new string[length];
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            buffer[index++] = item.GetString() ?? string.Empty;
        }

        values = buffer;
        return true;
    }
}
