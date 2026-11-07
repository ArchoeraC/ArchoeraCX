using System.Text.Json;

namespace Archoera.Application.Json;

/// <summary>
/// Raised by <see cref="JsonReader"/> for a present-but-mistyped value, a
/// missing required field or an invalid request document. The code is stable
/// and the message is safe to surface in an error envelope. Deriving from
/// <see cref="ArgumentException"/> keeps every existing
/// <c>catch (ArgumentException)</c> envelope mapping intact.
/// </summary>
public sealed class JsonReaderException : ArgumentException
{
    public JsonReaderException(string code, string message) : base(message) => Code = code;

    /// <summary>Stable envelope error code, for example <c>E_INVALID</c>.</summary>
    public string Code { get; }
}

/// <summary>
/// Shared JSON primitives for module handlers. NativeAOT-safe: parsing is
/// based on <see cref="JsonDocument"/> only, never reflection.
///
/// Two styles per primitive, and every replacement site must pick exactly one:
/// <list type="bullet">
/// <item><see cref="JsonReader.RequireString(string)"/> and friends: missing,
/// null or mistyped values throw <see cref="JsonReaderException"/> with a
/// stable code that the handler's error envelope catch maps through.</item>
/// <item><see cref="JsonReader.OptionalString(string)"/> and friends: missing
/// or null yield the fallback, but a mistyped value still throws — requests
/// are validated.</item>
/// <item><see cref="JsonReader.TryString(string)"/> and friends: fully silent
/// — missing, null or mistyped values yield the fallback, matching lenient
/// sidecar/metadata parsing.</item>
/// </list>
/// </summary>
public sealed class JsonReader(JsonDocument document) : IDisposable
{
    /// <summary>Default stable code for request-shaped handlers.</summary>
    public const string InvalidCode = "E_INVALID";

    private readonly JsonDocument _document = document;

    public JsonElement RootElement => _document.RootElement;

    /// <summary>
    /// Parses a request document. An empty/blank string becomes an empty
    /// object; malformed JSON throws <see cref="JsonReaderException"/> with
    /// <paramref name="invalidCode"/> and the (overridable) standard message.
    /// </summary>
    public static JsonReader Parse(
        string? json,
        string invalidCode = InvalidCode,
        string? invalidMessage = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new JsonReader(JsonDocument.Parse("{}"));
        }

        try
        {
            return new JsonReader(JsonDocument.Parse(json));
        }
        catch (JsonException exception)
        {
            throw new JsonReaderException(
                invalidCode,
                invalidMessage ?? $"request is not valid JSON: {exception.Message}");
        }
    }

    /// <summary>
    /// Parses without converting <see cref="JsonException"/>: the caller keeps
    /// the exact exception surface of a bare <c>JsonDocument.Parse</c>.
    /// </summary>
    public static JsonReader ParseRaw(string? json)
        => new(JsonDocument.Parse(
            string.IsNullOrWhiteSpace(json) ? "{}" : json));

    public void Dispose() => _document.Dispose();

    // --- strict request style (throw on missing/mistyped) ---------------------

    public string RequireString(string name, string code = InvalidCode, string? message = null)
        => OptionalString(_document.RootElement, name, code, message)
           ?? throw new JsonReaderException(code, message ?? $"{name} is required");

    public int RequireInt32(string name, string code = InvalidCode, string? message = null)
        => OptionalInt32(_document.RootElement, name, code, message)
           ?? throw new JsonReaderException(code, message ?? $"{name} is required");

    public bool RequireBool(string name, string code = InvalidCode, string? message = null)
        => OptionalBool(_document.RootElement, name, code, message)
           ?? throw new JsonReaderException(code, message ?? $"{name} must be a boolean");

    public string? OptionalString(string name, string code = InvalidCode, string? message = null)
        => OptionalString(_document.RootElement, name, code, message);

    public int? OptionalInt32(string name, string code = InvalidCode, string? message = null)
        => OptionalInt32(_document.RootElement, name, code, message);

    public long? OptionalInt64(string name, string code = InvalidCode, string? message = null)
        => OptionalInt64(_document.RootElement, name, code, message);

    public bool? OptionalBool(string name, string code = InvalidCode, string? message = null)
        => OptionalBool(_document.RootElement, name, code, message);

    /// <summary>
    /// Whitelist enum: a value outside <paramref name="whitelist"/> (or the
    /// wrong JSON type) throws with the stable code.
    /// </summary>
    public T RequireEnum<T>(
        string name,
        IReadOnlyDictionary<string, T> whitelist,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase,
        string code = InvalidCode,
        string? message = null)
    {
        var element = Optional(_document.RootElement, name);
        if (element is null ||
            element.Value.ValueKind != JsonValueKind.String ||
            !MatchWhitelist(element.Value.GetString(), whitelist, comparison, out var value))
        {
            throw new JsonReaderException(code, message ?? $"{name} is required");
        }

        return value;
    }

    /// <summary>
    /// Whitelist enum: missing/null yields <paramref name="fallback"/>; a value
    /// outside the whitelist or the wrong JSON type throws with the stable code.
    /// </summary>
    public T OptionalEnum<T>(
        string name,
        T fallback,
        IReadOnlyDictionary<string, T> whitelist,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase,
        string code = InvalidCode,
        string? message = null)
    {
        var element = Optional(_document.RootElement, name);
        if (element is null) return fallback;
        if (element.Value.ValueKind != JsonValueKind.String ||
            !MatchWhitelist(element.Value.GetString(), whitelist, comparison, out var value))
        {
            throw new JsonReaderException(code, message ?? $"{name} is not supported");
        }

        return value;
    }

    // --- silent style (never throws) ------------------------------------------

    public string? TryString(string name) => TryString(_document.RootElement, name);

    public int? TryInt32(string name) => TryInt32(_document.RootElement, name);

    public long? TryInt64(string name) => TryInt64(_document.RootElement, name);

    public bool? TryBool(string name) => TryBool(_document.RootElement, name);

    // --- element-level strict helpers -----------------------------------------

    public static string? OptionalString(
        JsonElement parent,
        string name,
        string code = InvalidCode,
        string? message = null)
    {
        var element = Optional(parent, name);
        if (element is null) return null;
        if (element.Value.ValueKind != JsonValueKind.String)
        {
            throw new JsonReaderException(code, message ?? $"{name} must be a string");
        }

        return element.Value.GetString();
    }

    public static int? OptionalInt32(
        JsonElement parent,
        string name,
        string code = InvalidCode,
        string? message = null)
    {
        var element = Optional(parent, name);
        if (element is null) return null;
        if (element.Value.ValueKind != JsonValueKind.Number ||
            !element.Value.TryGetInt32(out var value))
        {
            throw new JsonReaderException(code, message ?? $"{name} must be an integer");
        }

        return value;
    }

    public static long? OptionalInt64(
        JsonElement parent,
        string name,
        string code = InvalidCode,
        string? message = null)
    {
        var element = Optional(parent, name);
        if (element is null) return null;
        if (element.Value.ValueKind != JsonValueKind.Number ||
            !element.Value.TryGetInt64(out var value))
        {
            throw new JsonReaderException(code, message ?? $"{name} must be an integer");
        }

        return value;
    }

    public static bool? OptionalBool(
        JsonElement parent,
        string name,
        string code = InvalidCode,
        string? message = null)
    {
        var element = Optional(parent, name);
        if (element is null) return null;
        if (element.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new JsonReaderException(code, message ?? $"{name} must be a boolean");
        }

        return element.Value.GetBoolean();
    }

    // --- element-level silent helpers (lenient sidecar/metadata parsing) ------

    public static string? TryString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static int? TryInt32(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetInt32(out var number)
            ? number
            : null;

    public static long? TryInt64(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetInt64(out var number)
            ? number
            : null;

    public static bool? TryBool(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value) &&
           value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    // --- value-level strict helpers (validate an already-extracted property) --

    /// <summary>JSON null yields null; any other non-integer value throws.</summary>
    public static int? Int32Value(
        JsonElement element,
        string name,
        string code = InvalidCode,
        string? message = null)
    {
        if (element.ValueKind == JsonValueKind.Null) return null;
        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt32(out var value))
        {
            throw new JsonReaderException(
                code,
                message ?? $"{name} must be an integer or null");
        }

        return value;
    }

    public static string? StringValue(
        JsonElement element,
        string name,
        string code = InvalidCode,
        string? message = null)
    {
        if (element.ValueKind == JsonValueKind.Null) return null;
        if (element.ValueKind != JsonValueKind.String)
        {
            throw new JsonReaderException(
                code,
                message ?? $"{name} must be a string or null");
        }

        return element.GetString();
    }

    public static bool? BoolValue(
        JsonElement element,
        string name,
        string code = InvalidCode,
        string? message = null)
    {
        if (element.ValueKind == JsonValueKind.Null) return null;
        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new JsonReaderException(
                code,
                message ?? $"{name} must be a boolean or null");
        }

        return element.GetBoolean();
    }

    private static bool MatchWhitelist<T>(
        string? text,
        IReadOnlyDictionary<string, T> whitelist,
        StringComparison comparison,
        out T value)
    {
        value = default!;
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (var pair in whitelist)
        {
            if (string.Equals(pair.Key, text, comparison))
            {
                value = pair.Value;
                return true;
            }
        }

        return false;
    }

    /// <summary>Missing or JSON null yields null; present values keep their kind.</summary>
    private static JsonElement? Optional(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(name, out var element) ||
            element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return element;
    }
}
