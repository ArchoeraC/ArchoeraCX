using System.Text.Json;
using Archoera.Domain.Instances;
using Archoera.Infrastructure.Settings;

namespace Archoera.Infrastructure.Instances;

/// <summary>
/// Lenient reader for the <c>overrides</c> object in
/// <c>.archoera/instance.json</c>. The file may be hand-edited or written by
/// another launcher, so unknown keys and mistyped values are ignored and only
/// individually invalid values are dropped.
/// </summary>
internal static class InstanceOverridesReader
{
    public static InstanceOverrides? Read(JsonElement root)
    {
        if (!root.TryGetProperty("overrides", out var element) ||
            element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var memoryMin = MemoryInt(element, "memoryMinMb");
        var memoryMax = MemoryInt(element, "memoryMaxMb");
        if (memoryMin is { } minimum && memoryMax is { } maximum && minimum > maximum)
        {
            // Imported metadata with an impossible range must not reach the UI
            // or the launch planner; fall back to auto sizing.
            memoryMin = null;
            memoryMax = null;
        }

        var joinOnLaunch = Bool(element, "joinServerOnLaunch");
        var joinAddress = ImportedJoinAddress(element);
        var joinWorld = ImportedJoinWorld(element);
        if (joinOnLaunch == true && joinAddress is null && joinWorld is null)
        {
            // An enabled join without a target would fail at launch; imported
            // metadata is untrusted, so drop the switch instead.
            joinOnLaunch = null;
        }

        var overrides = new InstanceOverrides(
            memoryMin,
            memoryMax,
            String(element, "jvmArgs"),
            String(element, "gcMode"),
            String(element, "gameArgs"),
            JavaPath(element),
            Int(element, "windowWidth"),
            Int(element, "windowHeight"),
            Bool(element, "fullscreen"),
            ImportedWrapper(element),
            RuntimeOptionsJson.TryReadEnvironment(element, "environment"),
            joinOnLaunch,
            joinAddress,
            joinWorld,
            ImportedCommand(element, "preLoadCommand"),
            ImportedCommand(element, "preLaunchCommand"),
            ImportedCommand(element, "postExitCommand"));
        return overrides.IsEmpty ? null : overrides;
    }

    /// <summary>
    /// Imported wrapper: an empty string is a meaningful "no wrapper", but an
    /// invalid command is dropped so it cannot break the first launch.
    /// </summary>
    private static string? ImportedWrapper(JsonElement root)
    {
        if (!root.TryGetProperty("wrapperCommand", out var element) ||
            element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = element.GetString();
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        try
        {
            return RuntimeOptionsJson.NormalizeWrapper(value);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Imported command hook: empty means "explicitly no command", and an
    /// invalid command is dropped so the imported instance still launches.
    /// </summary>
    private static string? ImportedCommand(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) ||
            element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = element.GetString();
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

    private static string? ImportedJoinAddress(JsonElement element)
    {
        try
        {
            return InstanceJoinRules.NormalizeAddress(String(element, "joinServerAddress"));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? ImportedJoinWorld(JsonElement element)
    {
        try
        {
            return InstanceJoinRules.NormalizeWorldName(String(element, "joinWorldName"));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Imported overrides are untrusted. Only an absolute, existing regular
    /// file is accepted; a relative, missing, or linked path is ignored so
    /// auto-detection picks a real runtime instead of launching something else.
    /// </summary>
    private static string? JavaPath(JsonElement root)
    {
        var path = String(root, "javaPath");
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return null;
        }

        try
        {
            var full = Path.GetFullPath(path);
            if (!File.Exists(full)) return null;
            // LinkTarget covers symlinks and Windows junctions on every
            // platform, while leaving OneDrive/cloud placeholder files alone
            // (they are reparse points but still regular files).
            if (new FileInfo(full).LinkTarget is not null)
            {
                return null;
            }

            return full;
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException or
            NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static int? MemoryInt(JsonElement root, string name)
    {
        var value = Int(root, name);
        return value is >= 128 and <= 65536 ? value : null;
    }

    private static int? Int(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) &&
           element.ValueKind == JsonValueKind.Number &&
           element.TryGetInt32(out var value)
            ? value
            : null;

    private static bool? Bool(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) &&
           element.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? element.GetBoolean()
            : null;

    private static string? String(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) &&
           element.ValueKind == JsonValueKind.String &&
           !string.IsNullOrWhiteSpace(element.GetString())
            ? element.GetString()
            : null;
}
