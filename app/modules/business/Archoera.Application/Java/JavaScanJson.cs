using System.Buffers;
using System.Text.Json;
using Archoera.Domain.Java;

namespace Archoera.Application.Java;

/// <summary>
/// Builds the wire envelope shared by the NativeAOT exports and the DevHost.
/// Written with <see cref="Utf8JsonWriter"/> so NativeAOT does not need reflection.
/// </summary>
public static class JavaScanJson
{
    public const int SchemaVersion = 2;

    public static byte[] BuildSelection(JavaRuntimeInfo item)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", true);
            writer.WriteStartObject("data");
            WriteRuntime(writer, item);
            writer.WriteEndObject();
            writer.WriteStartObject("meta");
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteRuntime(Utf8JsonWriter writer, JavaRuntimeInfo item)
    {
        writer.WriteString("path", item.Path);
        writer.WriteString("version", item.Version);
        writer.WriteNumber("major", item.Major);
        writer.WriteString("vendor", item.Vendor);
        writer.WriteString("arch", item.Arch);
        writer.WriteString("source", item.Source);
        if (!string.IsNullOrWhiteSpace(item.Home))
        {
            writer.WriteString("home", item.Home);
        }
    }

    public static byte[] BuildEnvelope(IReadOnlyList<JavaRuntimeInfo> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var buffer = new ArrayBufferWriter<byte>(256 + (items.Count * 160));
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", true);
            writer.WriteStartObject("data");
            writer.WriteStartArray("items");
            foreach (var item in items)
            {
                writer.WriteStartObject();
                WriteRuntime(writer, item);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteNumber("count", items.Count);
            writer.WriteEndObject();
            writer.WriteStartObject("meta");
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }
}
