using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Archoera.Domain.Mojang;

namespace Archoera.Application.Mojang;

/// <summary>
/// Mojang JSON to domain-model mapping. Everything goes through
/// <see cref="JsonElement"/>, so NativeAOT never needs reflection-based
/// serialization. Shapes are validated eagerly: an absent <c>sha1</c>/<c>size</c>
/// stays <see langword="null"/> so the plan builder can report
/// <c>MISSING_HASH</c>/<c>MISSING_SIZE</c> instead of guessing.
/// </summary>
public static class MojangJson
{
    /// <summary>Parses <c>version_manifest_v2.json</c>.</summary>
    public static MojangVersionManifest ParseManifest(JsonElement root)
    {
        RequireObject(root, "version manifest");

        var latestRelease = string.Empty;
        var latestSnapshot = string.Empty;
        if (root.TryGetProperty("latest", out var latest)
            && latest.ValueKind != JsonValueKind.Null)
        {
            RequireObject(latest, "version manifest latest");
            latestRelease = GetString(latest, "release") ?? string.Empty;
            latestSnapshot = GetString(latest, "snapshot") ?? string.Empty;
        }

        var versions = new List<MojangVersionSummary>();
        if (!root.TryGetProperty("versions", out var versionsElement)
            || versionsElement.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("version manifest 'versions' must be an array");
        }

        foreach (var item in versionsElement.EnumerateArray())
        {
            versions.Add(ParseVersionSummary(item));
        }

        return new MojangVersionManifest(latestRelease, latestSnapshot, versions);
    }

    /// <summary>Parses one resolved version JSON.</summary>
    public static MojangVersion ParseVersion(JsonElement root)
    {
        RequireObject(root, "version");

        var assetIndex = root.TryGetProperty("assetIndex", out var assetIndexElement)
            && assetIndexElement.ValueKind != JsonValueKind.Null
                ? ParseAssetIndexRef(assetIndexElement)
                : null;

        var downloads = default(MojangVersionDownloads);
        if (root.TryGetProperty("downloads", out var downloadsElement)
            && downloadsElement.ValueKind != JsonValueKind.Null)
        {
            RequireObject(downloadsElement, "version downloads");
            downloads = new MojangVersionDownloads(
                ParseDownload(downloadsElement, "client"));
        }

        var libraries = new List<MojangLibrary>();
        if (root.TryGetProperty("libraries", out var librariesElement)
            && librariesElement.ValueKind != JsonValueKind.Null)
        {
            if (librariesElement.ValueKind != JsonValueKind.Array)
            {
                throw Invalid("version 'libraries' must be an array");
            }

            foreach (var item in librariesElement.EnumerateArray())
            {
                libraries.Add(ParseLibrary(item));
            }
        }

        return new MojangVersion
        {
            Id = RequireNonEmptyString(root, "id"),
            Type = GetString(root, "type") ?? string.Empty,
            InheritsFrom = GetString(root, "inheritsFrom"),
            MainClass = GetString(root, "mainClass"),
            Assets = GetString(root, "assets"),
            AssetIndex = assetIndex,
            Downloads = downloads ?? new MojangVersionDownloads((MojangDownload?)null),
            JavaVersion = ParseJavaVersion(root),
            Logging = ParseLogging(root),
            Libraries = libraries,
            Arguments = ParseArguments(root),
            MinimumLauncherVersion = GetInt32OrNull(root, "minimumLauncherVersion"),
            ComplianceLevel = GetInt32OrNull(root, "complianceLevel"),
            ReleaseTime = GetDate(root, "releaseTime"),
        };
    }

    /// <summary>Parses <c>assets/indexes/&lt;id&gt;.json</c>.</summary>
    /// <param name="root">Asset index JSON.</param>
    /// <param name="id">Index id from the version JSON (the file itself has none).</param>
    public static MojangAssetIndex ParseAssetIndex(JsonElement root, string id)
    {
        RequireObject(root, "asset index");
        if (string.IsNullOrWhiteSpace(id))
        {
            throw Invalid("asset index id is required");
        }

        if (!root.TryGetProperty("objects", out var objectsElement)
            || objectsElement.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("asset index 'objects' must be an object");
        }

        var objects = new Dictionary<string, MojangAssetObject>(StringComparer.Ordinal);
        foreach (var property in objectsElement.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name))
            {
                throw Invalid("asset index contains an empty object name");
            }

            objects[property.Name] = ParseAssetObject(property.Value, property.Name);
        }

        return new MojangAssetIndex(
            id,
            new ReadOnlyDictionary<string, MojangAssetObject>(objects),
            GetBool(root, "virtual"),
            GetBool(root, "map_to_resources"));
    }

    private static MojangVersionSummary ParseVersionSummary(JsonElement root)
    {
        RequireObject(root, "version manifest entry");

        return new MojangVersionSummary(
            Id: RequireNonEmptyString(root, "id"),
            Type: GetString(root, "type") ?? string.Empty,
            Url: RequireNonEmptyString(root, "url"),
            Sha1: RequireNonEmptyString(root, "sha1"),
            ReleaseTime: GetDate(root, "releaseTime"),
            ComplianceLevel: GetInt32OrNull(root, "complianceLevel") ?? 0,
            Size: GetInt64OrNull(root, "size"));
    }

    private static MojangAssetIndexRef ParseAssetIndexRef(JsonElement root)
    {
        RequireObject(root, "assetIndex");

        return new MojangAssetIndexRef(
            Id: RequireNonEmptyString(root, "id"),
            Url: RequireNonEmptyString(root, "url"),
            Sha1: GetString(root, "sha1"),
            Size: GetInt64OrNull(root, "size"),
            TotalSize: GetInt64OrNull(root, "totalSize"));
    }

    private static MojangAssetObject ParseAssetObject(JsonElement root, string name)
    {
        RequireObject(root, $"asset '{name}'");

        return new MojangAssetObject(
            Hash: RequireNonEmptyString(root, "hash"),
            Size: GetInt64OrNull(root, "size"));
    }

    private static MojangLibrary ParseLibrary(JsonElement root)
    {
        RequireObject(root, "library");

        var name = RequireNonEmptyString(root, "name");
        var downloads = default(MojangLibraryDownloads);
        if (root.TryGetProperty("downloads", out var downloadsElement)
            && downloadsElement.ValueKind != JsonValueKind.Null)
        {
            RequireObject(downloadsElement, "library downloads");
            downloads = new MojangLibraryDownloads(
                ParseDownload(downloadsElement, "artifact"),
                ParseClassifierDownloads(downloadsElement));
        }
        else
        {
            // Fabric/Quilt profiles and profiles merged by other launchers may
            // keep only a Maven coordinate (and optionally a repository URL).
            // Synthesize the artifact URL/path so the plan builder can download
            // it; a missing hash/size is filled by the Maven sidecar resolver.
            downloads = ParseMavenLibrary(name, root);
        }

        return new MojangLibrary(
            Name: name,
            Downloads: downloads,
            Natives: ParseNatives(root),
            Rules: ParseRules(root),
            Extract: ParseExtract(root));
    }

    private static MojangLibraryDownloads? ParseMavenLibrary(
        string name,
        JsonElement root)
    {
        if (!MojangLibraryCoordinate.TryParse(name, out var coordinate) ||
            !MojangMavenPath.TryResolve(name, classifierOverride: null, out var path))
        {
            return null;
        }

        var repository = MojangRepositories.ForCoordinate(
            GetString(root, "url"),
            coordinate);
        var url = new Uri(repository, path).AbsoluteUri;

        return new MojangLibraryDownloads(
            new MojangDownload(
                Url: url,
                Sha1: GetString(root, "sha1"),
                Size: GetInt64OrNull(root, "size"),
                Path: path));
    }

    private static MojangDownload? ParseDownload(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element)
            || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        RequireObject(element, name);

        return new MojangDownload(
            Url: GetString(element, "url") ?? string.Empty,
            Sha1: GetString(element, "sha1"),
            Size: GetInt64OrNull(element, "size"),
            Path: GetString(element, "path"));
    }

    private static IReadOnlyDictionary<string, MojangDownload>? ParseClassifierDownloads(
        JsonElement downloads)
    {
        if (!downloads.TryGetProperty("classifiers", out var element)
            || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("library downloads 'classifiers' must be an object");
        }

        var classifiers = new Dictionary<string, MojangDownload>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            var download = ParseDownload(element, property.Name)
                ?? throw Invalid($"library classifier '{property.Name}' must be an object");
            classifiers[property.Name] = download;
        }

        return new ReadOnlyDictionary<string, MojangDownload>(classifiers);
    }

    private static IReadOnlyDictionary<string, string>? ParseNatives(JsonElement root)
    {
        if (!root.TryGetProperty("natives", out var element)
            || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("library 'natives' must be an object");
        }

        var natives = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.String)
            {
                throw Invalid($"library natives['{property.Name}'] must be a string");
            }

            var value = property.Value.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                natives[property.Name] = value;
            }
        }

        return natives.Count == 0
            ? null
            : new ReadOnlyDictionary<string, string>(natives);
    }

    private static IReadOnlyList<MojangRule>? ParseRules(JsonElement root)
    {
        if (!root.TryGetProperty("rules", out var element)
            || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("'rules' must be an array");
        }

        var rules = new List<MojangRule>(element.GetArrayLength());
        foreach (var item in element.EnumerateArray())
        {
            rules.Add(ParseRule(item));
        }

        return rules;
    }

    private static MojangRule ParseRule(JsonElement root)
    {
        RequireObject(root, "rule");

        var actionText = RequireNonEmptyString(root, "action");
        var action = actionText.ToLowerInvariant() switch
        {
            "allow" => MojangRuleAction.Allow,
            "disallow" => MojangRuleAction.Disallow,
            _ => throw Invalid($"unsupported rule action '{actionText}'"),
        };

        MojangRuleOs? os = null;
        if (root.TryGetProperty("os", out var osElement)
            && osElement.ValueKind != JsonValueKind.Null)
        {
            RequireObject(osElement, "rule os");
            os = new MojangRuleOs(
                Name: GetString(osElement, "name"),
                Arch: GetString(osElement, "arch"),
                Version: GetString(osElement, "version"));
        }

        IReadOnlyDictionary<string, bool>? features = null;
        if (root.TryGetProperty("features", out var featuresElement)
            && featuresElement.ValueKind != JsonValueKind.Null)
        {
            if (featuresElement.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("rule 'features' must be an object");
            }

            var parsed = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in featuresElement.EnumerateObject())
            {
                if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    throw Invalid($"rule feature '{property.Name}' must be a boolean");
                }

                parsed[property.Name] = property.Value.GetBoolean();
            }

            features = new ReadOnlyDictionary<string, bool>(parsed);
        }

        return new MojangRule(action, os, features);
    }

    private static MojangExtract? ParseExtract(JsonElement root)
    {
        if (!root.TryGetProperty("extract", out var element)
            || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        RequireObject(element, "library extract");
        return new MojangExtract(GetStringArray(element, "exclude"));
    }

    private static MojangJavaVersion? ParseJavaVersion(JsonElement root)
    {
        if (!root.TryGetProperty("javaVersion", out var element)
            || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        RequireObject(element, "javaVersion");
        return new MojangJavaVersion(
            Component: GetString(element, "component"),
            MajorVersion: GetInt32OrNull(element, "majorVersion"));
    }

    private static MojangLogging? ParseLogging(JsonElement root)
    {
        if (!root.TryGetProperty("logging", out var element)
            || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        RequireObject(element, "logging");
        if (!element.TryGetProperty("client", out var client)
            || client.ValueKind == JsonValueKind.Null)
        {
            return new MojangLogging(null, null);
        }

        RequireObject(client, "logging client");
        MojangLoggingFile? file = null;
        if (client.TryGetProperty("file", out var fileElement)
            && fileElement.ValueKind != JsonValueKind.Null)
        {
            RequireObject(fileElement, "logging client file");
            file = new MojangLoggingFile(
                Id: GetString(fileElement, "id"),
                Url: RequireNonEmptyString(fileElement, "url"),
                Sha1: GetString(fileElement, "sha1"),
                Size: GetInt64OrNull(fileElement, "size"),
                Type: GetString(fileElement, "type"));
        }

        return new MojangLogging(GetString(client, "argument"), file);
    }

    private static MojangArguments? ParseArguments(JsonElement root)
    {
        if (!root.TryGetProperty("arguments", out var element)
            || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        RequireObject(element, "arguments");
        return new MojangArguments(
            ParseArgumentList(element, "game"),
            ParseArgumentList(element, "jvm"));
    }

    private static IReadOnlyList<MojangArgument> ParseArgumentList(
        JsonElement parent,
        string name)
    {
        if (!parent.TryGetProperty(name, out var element)
            || element.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"arguments '{name}' must be an array");
        }

        var arguments = new List<MojangArgument>(element.GetArrayLength());
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                arguments.Add(new MojangArgument([], [item.GetString() ?? string.Empty]));
                continue;
            }

            RequireObject(item, $"argument in '{name}'");
            if (!item.TryGetProperty("value", out var valueElement))
            {
                throw Invalid($"argument in '{name}' is missing 'value'");
            }

            arguments.Add(new MojangArgument(
                ParseRules(item) ?? [],
                ParseArgumentValues(valueElement, name)));
        }

        return arguments;
    }

    private static IReadOnlyList<string> ParseArgumentValues(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return [value.GetString() ?? string.Empty];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"argument 'value' in '{name}' must be a string or array");
        }

        var values = new List<string>(value.GetArrayLength());
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw Invalid($"argument 'value' entries in '{name}' must be strings");
            }

            values.Add(item.GetString() ?? string.Empty);
        }

        return values;
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element)
            || element.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"'{name}' must be an array");
        }

        var values = new List<string>(element.GetArrayLength());
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw Invalid($"'{name}' entries must be strings");
            }

            values.Add(item.GetString() ?? string.Empty);
        }

        return values;
    }

    private static void RequireObject(JsonElement element, string what)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"{what} must be a JSON object");
        }
    }

    private static string RequireNonEmptyString(JsonElement parent, string name)
    {
        var value = GetString(parent, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Invalid($"missing required string '{name}'");
        }

        return value;
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

    private static int? GetInt32OrNull(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element)
            || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value))
        {
            throw Invalid($"'{name}' must be a 32-bit integer");
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

    private static MojangException Invalid(string detail)
        => new(MojangErrorCode.InvalidResponse, $"Mojang metadata is invalid: {detail}");
}
