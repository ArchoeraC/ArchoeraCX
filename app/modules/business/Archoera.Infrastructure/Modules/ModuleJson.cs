using System.Text.Json;

namespace Archoera.Infrastructure.Modules;

/// <summary>Shared JSON envelope helpers for module handlers.</summary>
public static class ModuleJson
{
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
