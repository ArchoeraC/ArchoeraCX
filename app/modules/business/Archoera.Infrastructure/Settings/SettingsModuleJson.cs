using System.Text.Json;
using Archoera.Domain.Settings;

namespace Archoera.Infrastructure.Settings;

/// <summary>
/// Response envelopes for the settings facade. Kept next to (and separate
/// from) the patch parser so both stay readable: this class only owns the
/// stable JSON field list the UI consumes.
/// </summary>
internal static class SettingsModuleJson
{
    /// <summary>
    /// The launcher command fields always appear in the response: null means
    /// "no command" so the UI never needs a missing-key branch.
    /// </summary>
    private static void WriteCommand(Utf8JsonWriter writer, string name, string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, command);
        }
    }

    /// <summary>Nullable strings always appear: null means "not configured".</summary>
    private static void WriteNullableString(
        Utf8JsonWriter writer,
        string name,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    internal static byte[] BuildEnvelope(AppSettings settings)
    {
        using var memory = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memory))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", true);
            writer.WriteStartObject("data");
            writer.WriteStartObject("settings");
            writer.WriteString("instancesRoot", settings.InstancesRoot);
            writer.WriteStartArray("instancesRoots");
            foreach (var root in settings.Roots)
            {
                writer.WriteStringValue(root);
            }
            writer.WriteEndArray();
            if (string.IsNullOrWhiteSpace(settings.JavaPath))
            {
                writer.WriteNull("javaPath");
            }
            else
            {
                writer.WriteString("javaPath", settings.JavaPath);
            }
            writer.WriteBoolean("javaAuto", settings.JavaAuto);
            writer.WriteNumber("memoryMinMb", settings.MemoryMinMb);
            writer.WriteNumber("memoryMaxMb", settings.MemoryMaxMb);
            if (string.IsNullOrWhiteSpace(settings.JvmArgs))
            {
                writer.WriteNull("jvmArgs");
            }
            else
            {
                writer.WriteString("jvmArgs", settings.JvmArgs);
            }
            writer.WriteString("gcMode", settings.GcMode);
            if (string.IsNullOrWhiteSpace(settings.GameArgs))
            {
                writer.WriteNull("gameArgs");
            }
            else
            {
                writer.WriteString("gameArgs", settings.GameArgs);
            }
            if (string.IsNullOrWhiteSpace(settings.WrapperCommand))
            {
                writer.WriteNull("wrapperCommand");
            }
            else
            {
                writer.WriteString("wrapperCommand", settings.WrapperCommand);
            }

            WriteCommand(writer, "preLoadCommand", settings.PreLoadCommand);
            WriteCommand(writer, "preLaunchCommand", settings.PreLaunchCommand);
            WriteCommand(writer, "postExitCommand", settings.PostExitCommand);

            RuntimeOptionsJson.WriteEnvironment(writer, "environment", settings.Environment);
            writer.WriteString("downloadSource", settings.DownloadSource);
            writer.WriteNumber("downloadConcurrency", settings.DownloadConcurrency);
            writer.WriteNumber("downloadPerHost", settings.DownloadPerHost);
            writer.WriteNumber("windowWidth", settings.WindowWidth);
            writer.WriteNumber("windowHeight", settings.WindowHeight);
            writer.WriteBoolean("fullscreen", settings.Fullscreen);
            writer.WriteBoolean("maximized", settings.Maximized);
            writer.WriteBoolean("enableGameMode", settings.EnableGameMode);
            writer.WriteBoolean("enableMangoHud", settings.EnableMangoHud);
            writer.WriteBoolean("useZink", settings.UseZink);
            writer.WriteBoolean("developerMode", settings.DeveloperMode);
            WriteNullableString(writer, "nativeGlfwPath", settings.NativeGlfwPath);
            WriteNullableString(writer, "nativeOpenALPath", settings.NativeOpenALPath);
            WriteNullableString(writer, "nativeSdlPath", settings.NativeSdlPath);
            writer.WriteNumber("downloadChunkSizeKb", settings.DownloadChunkSizeKb);
            writer.WriteNumber("downloadRangeThresholdKb", settings.DownloadRangeThresholdKb);
            writer.WriteNumber("logRetentionFiles", settings.LogRetentionFiles);
            writer.WriteNumber("logRetentionMb", settings.LogRetentionMegabytes);
            writer.WriteBoolean("shareLoaderFiles", settings.ShareLoaderFiles);
            writer.WriteString("memoryMode", settings.MemoryMode);
            writer.WriteBoolean("gameOptimization", settings.GameOptimization);
            if (string.IsNullOrWhiteSpace(settings.DownloadProxy))
            {
                writer.WriteNull("downloadProxy");
            }
            else
            {
                writer.WriteString("downloadProxy", settings.DownloadProxy);
            }
            writer.WriteString("dataRoot", settings.DataRoot);
            writer.WriteString("settingsPath", settings.SettingsPath);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteStartObject("meta");
            writer.WriteNumber("schemaVersion", AppSettings.CurrentSchemaVersion);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return memory.ToArray();
    }

    internal static byte[] Error(string code, string message)
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
            writer.WriteEndObject();
        }

        return memory.ToArray();
    }
}
