using System.Globalization;
using System.Text.Json;
using Archoera.Domain.Java;

namespace Archoera.Application.JavaRuntime;

/// <summary>
/// AOT-safe mapping for Mojang's Java-runtime documents. Both parsers use
/// <see cref="JsonElement"/> only, so NativeAOT never needs reflection. Absent
/// hashes/sizes stay <see langword="null"/>; validation happens in
/// <c>JavaRuntimeClient</c> and <c>JavaRuntimePlanBuilder</c>.
/// </summary>
public static class JavaRuntimeJson
{
    /// <summary>
    /// Parses <c>all.json</c>: <c>platform → component → [version entries]</c>.
    /// Empty arrays (for example <c>gamecore.java-runtime-alpha</c>) are valid
    /// and simply produce no entries.
    /// </summary>
    /// <exception cref="JavaRuntimeException"><c>INVALID_RESPONSE</c>.</exception>
    public static JavaRuntimeCatalog ParseCatalog(JsonElement root)
    {
        RequireObject(root, "java runtime catalog");

        var platforms = new Dictionary<string, IReadOnlyList<JavaRuntimeComponent>>(
            StringComparer.Ordinal);

        foreach (var platformProperty in root.EnumerateObject())
        {
            var platformKey = platformProperty.Name;
            if (string.IsNullOrWhiteSpace(platformKey))
            {
                throw Invalid("catalog contains an empty platform key");
            }

            if (platformProperty.Value.ValueKind != JsonValueKind.Object)
            {
                throw Invalid($"catalog platform '{platformKey}' must be an object");
            }

            var components = new List<JavaRuntimeComponent>();
            foreach (var componentProperty in platformProperty.Value.EnumerateObject())
            {
                var componentName = componentProperty.Name;
                if (string.IsNullOrWhiteSpace(componentName))
                {
                    throw Invalid($"catalog platform '{platformKey}' has an empty component name");
                }

                if (componentProperty.Value.ValueKind != JsonValueKind.Array)
                {
                    throw Invalid(
                        $"catalog component '{componentName}' on '{platformKey}' must be an array");
                }

                foreach (var item in componentProperty.Value.EnumerateArray())
                {
                    components.Add(ParseComponent(item, platformKey, componentName));
                }
            }

            platforms[platformKey] = components;
        }

        return new JavaRuntimeCatalog(platforms);
    }

    /// <summary>
    /// Parses one platform runtime manifest: <c>{ "files": { path: entry } }</c>.
    /// </summary>
    /// <exception cref="JavaRuntimeException"><c>INVALID_RESPONSE</c>.</exception>
    public static JavaRuntimeManifest ParseManifest(JsonElement root)
    {
        RequireObject(root, "java runtime manifest");

        if (!root.TryGetProperty("files", out var filesElement)
            || filesElement.ValueKind == JsonValueKind.Null)
        {
            throw Invalid("runtime manifest 'files' is required");
        }

        if (filesElement.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("runtime manifest 'files' must be an object");
        }

        var files = new Dictionary<string, JavaRuntimeFileEntry>(StringComparer.Ordinal);
        foreach (var property in filesElement.EnumerateObject())
        {
            var path = property.Name;
            if (string.IsNullOrWhiteSpace(path))
            {
                throw Invalid("runtime manifest contains an empty file path");
            }

            files[path] = ParseFileEntry(property.Value, path);
        }

        return new JavaRuntimeManifest(files);
    }

    private static JavaRuntimeComponent ParseComponent(
        JsonElement root,
        string platformKey,
        string componentName)
    {
        RequireObject(root, $"runtime '{componentName}' on '{platformKey}'");

        if (!root.TryGetProperty("manifest", out var manifestElement)
            || manifestElement.ValueKind == JsonValueKind.Null)
        {
            throw Invalid($"runtime '{componentName}' on '{platformKey}' has no manifest");
        }

        RequireObject(manifestElement, "runtime manifest reference");
        var manifest = new JavaRuntimeDownloadRef(
            Url: GetString(manifestElement, "url") ?? string.Empty,
            Sha1: GetString(manifestElement, "sha1"),
            Size: GetInt64OrNull(manifestElement, "size"));

        var version = new JavaRuntimeVersion(string.Empty, null);
        if (root.TryGetProperty("version", out var versionElement)
            && versionElement.ValueKind != JsonValueKind.Null)
        {
            RequireObject(versionElement, "runtime version");
            version = new JavaRuntimeVersion(
                Name: GetString(versionElement, "name") ?? string.Empty,
                Released: GetDate(versionElement, "released"));
        }

        return new JavaRuntimeComponent(
            PlatformKey: platformKey,
            Name: componentName,
            Availability: GetString(root, "availability"),
            Manifest: manifest,
            Version: version);
    }

    private static JavaRuntimeFileEntry ParseFileEntry(JsonElement root, string path)
    {
        RequireObject(root, $"runtime file '{path}'");

        var type = GetString(root, "type");
        if (string.IsNullOrWhiteSpace(type))
        {
            throw Invalid($"runtime file '{path}' has no type");
        }

        JavaRuntimeDownloadRef? raw = null;
        JavaRuntimeDownloadRef? lzma = null;
        if (root.TryGetProperty("downloads", out var downloadsElement)
            && downloadsElement.ValueKind != JsonValueKind.Null)
        {
            RequireObject(downloadsElement, $"runtime file '{path}' downloads");
            raw = ParseDownload(downloadsElement, "raw");
            lzma = ParseDownload(downloadsElement, "lzma");
        }

        return new JavaRuntimeFileEntry(
            Path: path,
            Type: type,
            Executable: GetBool(root, "executable"),
            Raw: raw,
            Lzma: lzma);
    }

    private static JavaRuntimeDownloadRef? ParseDownload(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element)
            || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        RequireObject(element, $"runtime download '{name}'");

        return new JavaRuntimeDownloadRef(
            Url: GetString(element, "url") ?? string.Empty,
            Sha1: GetString(element, "sha1"),
            Size: GetInt64OrNull(element, "size"));
    }

    private static void RequireObject(JsonElement element, string what)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"{what} must be a JSON object");
        }
    }

    private static string? GetString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Null => null,
            _ => throw Invalid($"'{name}' must be a string"),
        };
    }

    private static long? GetInt64OrNull(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element)
            || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out var value))
        {
            throw Invalid($"'{name}' must be a 64-bit integer");
        }

        return value;
    }

    private static bool GetBool(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element)
            || element.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw Invalid($"'{name}' must be a boolean"),
        };
    }

    private static DateTimeOffset? GetDate(JsonElement parent, string name)
    {
        var text = GetString(parent, name);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var value)
            ? value
            : null;
    }

    private static JavaRuntimeException Invalid(string detail)
        => new(
            JavaRuntimeErrorCode.InvalidResponse,
            $"Java runtime metadata is invalid: {detail}");
}
