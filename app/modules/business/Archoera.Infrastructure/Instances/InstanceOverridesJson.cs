using System.Text.Json;
using Archoera.Application.Json;
using Archoera.Domain.Instances;
using Archoera.Infrastructure.Settings;

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

            if (content.Maximized is { } maximized)
            {
                writer.WriteBoolean("maximized", maximized);
            }

            if (content.EnableGameMode is { } gameMode)
            {
                writer.WriteBoolean("enableGameMode", gameMode);
            }

            if (content.EnableMangoHud is { } mangoHud)
            {
                writer.WriteBoolean("enableMangoHud", mangoHud);
            }

            if (content.UseZink is { } zink)
            {
                writer.WriteBoolean("useZink", zink);
            }

            // An explicitly empty wrapper is meaningful and must survive:
            // it disables the global wrapper for this instance.
            if (content.WrapperCommand is { } wrapper)
            {
                writer.WriteString("wrapperCommand", wrapper);
            }

            // An explicitly empty object is meaningful too: it clears the
            // global environment.
            if (content.Environment is not null)
            {
                RuntimeOptionsJson.WriteEnvironment(
                    writer,
                    "environment",
                    content.Environment);
            }

            if (content.JoinServerOnLaunch is { } joinOnLaunch)
            {
                writer.WriteBoolean("joinServerOnLaunch", joinOnLaunch);
            }

            if (content.JoinServerAddress is { } joinAddress)
            {
                writer.WriteString("joinServerAddress", joinAddress);
            }

            if (content.JoinWorldName is { } joinWorld)
            {
                writer.WriteString("joinWorldName", joinWorld);
            }

            // Command overrides keep the empty string: it disables the global
            // command for this instance instead of inheriting it.
            if (content.PreLoadCommand is { } preLoad)
            {
                writer.WriteString("preLoadCommand", preLoad);
            }

            if (content.PreLaunchCommand is { } preLaunch)
            {
                writer.WriteString("preLaunchCommand", preLaunch);
            }

            if (content.PostExitCommand is { } postExit)
            {
                writer.WriteString("postExitCommand", postExit);
            }
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Reads the <c>overrides</c> object from a sidecar record. Lenient by
    /// design: unknown keys and mistyped values are ignored because the file
    /// may be hand-edited or written by another launcher.
    /// </summary>
    public static InstanceOverrides? Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("overrides", out var element) ||
            element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var overrides = new InstanceOverrides(
            MemoryMinMb: JsonReader.TryInt32(element, "memoryMinMb"),
            MemoryMaxMb: JsonReader.TryInt32(element, "memoryMaxMb"),
            JvmArgs: LegacyString(element, "jvmArgs"),
            GcMode: LegacyString(element, "gcMode"),
            GameArgs: LegacyString(element, "gameArgs"),
            JavaPath: LegacyString(element, "javaPath"),
            WindowWidth: JsonReader.TryInt32(element, "windowWidth"),
            WindowHeight: JsonReader.TryInt32(element, "windowHeight"),
            Fullscreen: JsonReader.TryBool(element, "fullscreen"),
            WrapperCommand: JsonReader.TryString(element, "wrapperCommand"),
            Environment: RuntimeOptionsJson.TryReadEnvironment(element, "environment"),
            JoinServerOnLaunch: JsonReader.TryBool(element, "joinServerOnLaunch"),
            JoinServerAddress: JsonReader.TryString(element, "joinServerAddress"),
            JoinWorldName: JsonReader.TryString(element, "joinWorldName"),
            PreLoadCommand: ImportedCommand(element, "preLoadCommand"),
            PreLaunchCommand: ImportedCommand(element, "preLaunchCommand"),
            PostExitCommand: ImportedCommand(element, "postExitCommand"),
            Maximized: JsonReader.TryBool(element, "maximized"),
            EnableGameMode: JsonReader.TryBool(element, "enableGameMode"),
            EnableMangoHud: JsonReader.TryBool(element, "enableMangoHud"),
            UseZink: JsonReader.TryBool(element, "useZink"));
        return overrides.IsEmpty ? null : overrides;
    }

    /// <summary>
    /// Imported command: an empty string is meaningful ("no command"), while an
    /// invalid or mistyped command is dropped so it cannot break the first
    /// launch of a hand-edited or foreign instance folder.
    /// </summary>
    private static string? ImportedCommand(JsonElement element, string name)
    {
        var value = JsonReader.TryString(element, name);
        if (value is null) return null;
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        try
        {
            return RuntimeOptionsJson.NormalizeCommand(value, name);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Legacy fields treat whitespace as "not set", matching the original
    /// sidecar readers. Wrapper and command hooks are deliberately excluded:
    /// an empty value is meaningful and must stay distinguishable from
    /// "inherit".
    /// </summary>
    private static string? LegacyString(JsonElement element, string name)
    {
        var value = JsonReader.TryString(element, name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
