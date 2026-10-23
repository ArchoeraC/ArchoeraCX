using System.Text.Json;
using Archoera.Domain.Instances;

namespace Archoera.Infrastructure.Instances;

/// <summary>
/// The one serializer for <see cref="InstanceOverrides"/> payloads. Two exact
/// shapes share the field list:
/// <list type="bullet">
/// <item>module envelopes (<c>instance.list</c>, <c>instance.sync</c>, …)
///       always carry an <c>overrides</c> object, possibly empty;</item>
/// <item>the per-instance <c>.archoera/instance.json</c> sidecar omits the
///       property entirely when nothing is set (it is written by other
///       tooling/readers as well, so the on-disk shape stays stable).</item>
/// </list>
/// Field order and names are shared here; adding a field touches this one place.
/// </summary>
internal static class InstanceOverridesJson
{
    public static void Write(
        Utf8JsonWriter writer,
        InstanceOverrides? overrides,
        bool omitWhenEmpty)
    {
        var hasContent = overrides is { IsEmpty: false };
        if (!hasContent && omitWhenEmpty)
        {
            // The sidecar omits the property entirely instead of writing `{}`.
            return;
        }

        writer.WriteStartObject("overrides");
        if (overrides is { IsEmpty: false } content)
        {
            if (content.MemoryMinMb is { } memoryMin)
            {
                writer.WriteNumber("memoryMinMb", memoryMin);
            }

            if (content.MemoryMaxMb is { } memoryMax)
            {
                writer.WriteNumber("memoryMaxMb", memoryMax);
            }

            if (content.JvmArgs is { } jvmArgs) writer.WriteString("jvmArgs", jvmArgs);
            if (content.GcMode is { } gcMode) writer.WriteString("gcMode", gcMode);
            if (content.GameArgs is { } gameArgs) writer.WriteString("gameArgs", gameArgs);
            if (content.JavaPath is { } javaPath) writer.WriteString("javaPath", javaPath);
            if (content.WindowWidth is { } windowWidth)
            {
                writer.WriteNumber("windowWidth", windowWidth);
            }

            if (content.WindowHeight is { } windowHeight)
            {
                writer.WriteNumber("windowHeight", windowHeight);
            }

            if (content.Fullscreen is { } fullscreen)
            {
                writer.WriteBoolean("fullscreen", fullscreen);
            }
        }

        writer.WriteEndObject();
    }
}
