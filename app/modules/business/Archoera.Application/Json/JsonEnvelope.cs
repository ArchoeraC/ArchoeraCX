using System.Text.Json;

namespace Archoera.Application.Json;

/// <summary>
/// The one JSON envelope shape used by every module method:
/// <c>{ ok, data | error, meta }</c>. Handlers serialize their payload into
/// <c>data</c> through <see cref="Envelope"/> and report failures through
/// <see cref="Error"/>; the export layer never reshapes envelopes.
/// </summary>
public static class JsonEnvelope
{
    /// <summary>Serializes a success envelope with schemaVersion meta.</summary>
    public static byte[] Envelope(Action<Utf8JsonWriter> data, bool ok = true)
    {
        using var memory = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memory))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", ok);
            writer.WriteStartObject("data");
            data(writer);
            writer.WriteEndObject();
            writer.WriteStartObject("meta");
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return memory.ToArray();
    }

    /// <summary>Serializes a failure envelope with a stable code and schemaVersion meta.</summary>
    public static byte[] Error(string code, string message)
    {
        using var memory = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memory))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", false);
            writer.WriteStartObject("error");
            writer.WriteString("code", code);
            writer.WriteString("message", message);
            writer.WriteEndObject();
            writer.WriteStartObject("meta");
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return memory.ToArray();
    }
}
