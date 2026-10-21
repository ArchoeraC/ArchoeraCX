using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Archoera.Domain.Mojang;

namespace Archoera.Application.Mojang;

/// <summary>
/// Result of the pure structural merge, before
/// <see cref="MojangJson.ParseVersion"/> validation.
/// </summary>
/// <param name="Id">Effective id.</param>
/// <param name="Chain">Resolved chain ids, root to leaf.</param>
/// <param name="Json">Compact UTF-8 effective-version document.</param>
/// <param name="Warnings">Deterministic merge diagnostics.</param>
/// <param name="MergeSchema">Merge algorithm version.</param>
public sealed record VersionMergeResult(
    string Id,
    IReadOnlyList<string> Chain,
    byte[] Json,
    IReadOnlyList<MergeWarning> Warnings,
    int MergeSchema = VersionMergeSchema.Current);

/// <summary>
/// Flattens a root-to-leaf <c>inheritsFrom</c> chain into one effective version
/// (docs/08 01 §3 and §5). The merge is structural only: rules, natives and
/// <c>${...}</c> placeholders are preserved verbatim and never evaluated.
/// </summary>
public static class VersionMergeJson
{
    private const string InheritsFrom = "inheritsFrom";
    private const string Libraries = "libraries";
    private const string Arguments = "arguments";
    private const string MinecraftArguments = "minecraftArguments";
    private const string Downloads = "downloads";
    private const string Logging = "logging";
    private const string MinimumLauncherVersion = "minimumLauncherVersion";
    private const string ComplianceLevel = "complianceLevel";

    /// <summary>
    /// Merges the chain and writes compact JSON with
    /// <see cref="Utf8JsonWriter"/>, so NativeAOT needs no reflection-based
    /// serializer. Throws <see cref="VersionMergeException"/> for malformed
    /// layers; warnings are returned instead of failing for recoverable
    /// differences.
    /// </summary>
    public static VersionMergeResult Build(IReadOnlyList<VersionLayer> chain)
    {
        ArgumentNullException.ThrowIfNull(chain);

        if (chain.Count == 0)
        {
            throw new VersionMergeException(
                VersionMergeErrorCode.InvalidRequest,
                "version chain is empty");
        }

        var warnings = new List<MergeWarning>();
        var output = new JsonObject();
        JsonObject? downloads = null;
        JsonObject? logging = null;
        JsonObject? leafSource = null;
        var libraries = new List<LibrarySlot>();
        var libraryIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var jvmArguments = new List<JsonNode?>();
        var gameArguments = new List<JsonNode?>();
        var argumentExtras = new JsonObject();
        var sawArguments = false;
        int? minimumLauncherVersion = null;
        int? complianceLevel = null;

        foreach (var layer in chain)
        {
            if (layer is null)
            {
                throw new VersionMergeException(
                    VersionMergeErrorCode.InvalidRequest,
                    "version chain contains a null layer");
            }

            var source = ParseLayer(layer);
            leafSource = source;
            var hasStructuredGame = false;
            JsonNode? legacyArguments = null;

            foreach (var property in source)
            {
                switch (property.Key)
                {
                    case InheritsFrom:
                        // The effective version never carries inheritsFrom; the
                        // chain is kept separately on the result.
                        break;

                    case Libraries:
                        MergeLibraries(
                            property.Value,
                            layer,
                            libraries,
                            libraryIndex,
                            warnings);
                        break;

                    case Arguments:
                        MergeArguments(
                            property.Value,
                            layer,
                            ref hasStructuredGame,
                            jvmArguments,
                            gameArguments,
                            argumentExtras,
                            ref sawArguments);
                        break;

                    case MinecraftArguments:
                        legacyArguments = property.Value;
                        break;

                    case Downloads:
                        downloads = MergeDownloads(
                            property.Value,
                            layer,
                            downloads,
                            output);
                        break;

                    case Logging:
                        logging = MergeLogging(property.Value, layer, logging, output);
                        break;

                    case MinimumLauncherVersion:
                        minimumLauncherVersion = MaxInt(
                            property.Value,
                            MinimumLauncherVersion,
                            layer.Id,
                            minimumLauncherVersion);
                        break;

                    case ComplianceLevel:
                        complianceLevel = MaxInt(
                            property.Value,
                            ComplianceLevel,
                            layer.Id,
                            complianceLevel);
                        break;

                    default:
                        // Leaf wins; an absent property keeps the nearest
                        // ancestor value already present in output.
                        output[property.Key] = property.Value?.DeepClone();
                        break;
                }
            }

            if (legacyArguments is not null)
            {
                MergeLegacyArguments(
                    legacyArguments,
                    layer,
                    hasStructuredGame,
                    gameArguments,
                    warnings,
                    ref sawArguments);
            }
        }

        if (libraries.Count > 0)
        {
            var array = new JsonArray();
            foreach (var slot in libraries)
            {
                // Cast to JsonNode so the non-generic Add overload is chosen;
                // the generic overload is trimming/AOT-unsafe (IL2026/IL3050).
                array.Add((JsonNode?)slot.Node);
            }

            output[Libraries] = array;
        }

        if (sawArguments)
        {
            var arguments = new JsonObject();
            foreach (var property in argumentExtras)
            {
                arguments[property.Key] = property.Value?.DeepClone();
            }

            arguments["jvm"] = ToArray(jvmArguments);
            arguments["game"] = ToArray(gameArguments);
            output[Arguments] = arguments;
        }

        if (minimumLauncherVersion is not null)
        {
            output[MinimumLauncherVersion] = minimumLauncherVersion.Value;
        }

        if (complianceLevel is not null)
        {
            output[ComplianceLevel] = complianceLevel.Value;
        }

        var id = ResolveId(output, chain[^1], leafSource);
        var json = WriteCompact(output);

        return new VersionMergeResult(
            id,
            chain.Select(layer => layer.Id).ToArray(),
            json,
            warnings);
    }

    private static JsonObject ParseLayer(VersionLayer layer)
    {
        if (string.IsNullOrWhiteSpace(layer.Json))
        {
            throw new VersionMergeException(
                VersionMergeErrorCode.InvalidLayerJson,
                $"version '{layer.Id}' has empty JSON");
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(layer.Json);
        }
        catch (JsonException exception)
        {
            throw new VersionMergeException(
                VersionMergeErrorCode.InvalidLayerJson,
                $"version '{layer.Id}' is not valid JSON: {exception.Message}",
                exception);
        }

        if (node is not JsonObject root)
        {
            throw new VersionMergeException(
                VersionMergeErrorCode.InvalidLayerJson,
                $"version '{layer.Id}' must be a JSON object");
        }

        return root;
    }

    private static void MergeLibraries(
        JsonNode? value,
        VersionLayer layer,
        List<LibrarySlot> slots,
        Dictionary<string, int> index,
        List<MergeWarning> warnings)
    {
        if (value is null)
        {
            return;
        }

        if (value is not JsonArray array)
        {
            throw new VersionMergeException(
                VersionMergeErrorCode.InvalidLibraries,
                $"version '{layer.Id}' has a non-array libraries");
        }

        foreach (var item in array)
        {
            if (item is not JsonObject library)
            {
                throw new VersionMergeException(
                    VersionMergeErrorCode.InvalidLibraries,
                    $"version '{layer.Id}' has a non-object library entry");
            }

            if (!library.TryGetPropertyValue("name", out var nameNode)
                || nameNode is not JsonValue nameValue
                || !nameValue.TryGetValue<string>(out var name)
                || string.IsNullOrWhiteSpace(name))
            {
                throw new VersionMergeException(
                    VersionMergeErrorCode.InvalidLibraries,
                    $"version '{layer.Id}' has a library without a string name");
            }

            if (!MojangLibraryCoordinate.TryParse(name, out var coordinate))
            {
                // Keep the entry exactly as received; a malformed coordinate is
                // a diagnostic problem, not a reason to drop a library.
                warnings.Add(new MergeWarning(
                    VersionMergeWarningCode.LibraryCoordinateUnrecognized,
                    $"library coordinate '{name}' is not maven-shaped; keeping it by raw name",
                    LibraryKey: name,
                    LayerId: layer.Id));

                var rawKey = "\0raw:" + name;
                if (index.TryGetValue(rawKey, out var rawSlot))
                {
                    slots[rawSlot].Node = (JsonObject)library.DeepClone();
                }
                else
                {
                    index[rawKey] = slots.Count;
                    slots.Add(new LibrarySlot(string.Empty, (JsonObject)library.DeepClone()));
                }

                continue;
            }

            var key = coordinate.Key;
            if (index.TryGetValue(key, out var existing))
            {
                var slot = slots[existing];
                if (!string.Equals(slot.Version, coordinate.Version, StringComparison.Ordinal))
                {
                    warnings.Add(new MergeWarning(
                        VersionMergeWarningCode.LibraryOverride,
                        $"library override {key} {slot.Version} -> {coordinate.Version} " +
                        $"(from {layer.Id})",
                        LibraryKey: key,
                        OldValue: slot.Version,
                        NewValue: coordinate.Version,
                        LayerId: layer.Id));
                }

                // Replace in place: the first (ancestor) position is kept so
                // the classpath order stays stable.
                slot.Node = (JsonObject)library.DeepClone();
                slot.Version = coordinate.Version;
            }
            else
            {
                index[key] = slots.Count;
                slots.Add(new LibrarySlot(
                    coordinate.Version,
                    (JsonObject)library.DeepClone()));
            }
        }
    }

    private static void MergeArguments(
        JsonNode? value,
        VersionLayer layer,
        ref bool hasStructuredGame,
        List<JsonNode?> jvmArguments,
        List<JsonNode?> gameArguments,
        JsonObject argumentExtras,
        ref bool sawArguments)
    {
        if (value is null)
        {
            return;
        }

        if (value is not JsonObject arguments)
        {
            throw new VersionMergeException(
                VersionMergeErrorCode.InvalidArguments,
                $"version '{layer.Id}' has a non-object arguments");
        }

        sawArguments = true;

        if (arguments.TryGetPropertyValue("jvm", out var jvm) && jvm is not null)
        {
            if (jvm is not JsonArray jvmArray)
            {
                throw new VersionMergeException(
                    VersionMergeErrorCode.InvalidArguments,
                    $"version '{layer.Id}' has a non-array arguments.jvm");
            }

            foreach (var item in jvmArray)
            {
                jvmArguments.Add(item?.DeepClone());
            }
        }

        if (arguments.TryGetPropertyValue("game", out var game) && game is not null)
        {
            if (game is not JsonArray gameArray)
            {
                throw new VersionMergeException(
                    VersionMergeErrorCode.InvalidArguments,
                    $"version '{layer.Id}' has a non-array arguments.game");
            }

            hasStructuredGame = true;
            foreach (var item in gameArray)
            {
                gameArguments.Add(item?.DeepClone());
            }
        }

        foreach (var property in arguments)
        {
            if (property.Key is "jvm" or "game")
            {
                continue;
            }

            argumentExtras[property.Key] = property.Value?.DeepClone();
        }
    }

    private static void MergeLegacyArguments(
        JsonNode value,
        VersionLayer layer,
        bool hasStructuredGame,
        List<JsonNode?> gameArguments,
        List<MergeWarning> warnings,
        ref bool sawArguments)
    {
        if (value is not JsonValue legacyValue
            || !legacyValue.TryGetValue<string>(out var legacy))
        {
            throw new VersionMergeException(
                VersionMergeErrorCode.InvalidLegacyArguments,
                $"version '{layer.Id}' has a non-string minecraftArguments");
        }

        if (string.IsNullOrWhiteSpace(legacy))
        {
            return;
        }

        if (hasStructuredGame)
        {
            warnings.Add(new MergeWarning(
                VersionMergeWarningCode.LegacyArgumentsIgnored,
                $"version '{layer.Id}' has structured arguments.game; " +
                "minecraftArguments ignored",
                LayerId: layer.Id));
            return;
        }

        var tokens = TokenizeLegacyArguments(legacy, layer.Id);
        foreach (var token in tokens)
        {
            gameArguments.Add(JsonValue.Create(token));
        }

        sawArguments = true;
        warnings.Add(new MergeWarning(
            VersionMergeWarningCode.LegacyArgumentsConverted,
            $"version '{layer.Id}' minecraftArguments converted to {tokens.Count} " +
            "arguments.game entries",
            LayerId: layer.Id));
    }

    /// <summary>
    /// Splits legacy <c>minecraftArguments</c> without trimming meaningful
    /// spaces. Double/single quotes group a token; backslash escapes only a
    /// quote, another backslash, or whitespace so Windows-style paths survive.
    /// </summary>
    private static IReadOnlyList<string> TokenizeLegacyArguments(string text, string layerId)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var hasToken = false;
        var quote = '\0';

        for (var i = 0; i < text.Length; i++)
        {
            var character = text[i];

            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                    continue;
                }

                if (character == '\\'
                    && quote == '"'
                    && i + 1 < text.Length
                    && text[i + 1] is '"' or '\\')
                {
                    i++;
                    current.Append(text[i]);
                    hasToken = true;
                    continue;
                }

                current.Append(character);
                hasToken = true;
                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
                hasToken = true;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (hasToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }

                continue;
            }

            if (character == '\\'
                && i + 1 < text.Length
                && (char.IsWhiteSpace(text[i + 1]) || text[i + 1] is '"' or '\'' or '\\'))
            {
                i++;
                current.Append(text[i]);
                hasToken = true;
                continue;
            }

            current.Append(character);
            hasToken = true;
        }

        if (quote != '\0')
        {
            throw new VersionMergeException(
                VersionMergeErrorCode.InvalidLegacyArguments,
                $"version '{layerId}' minecraftArguments has an unterminated quote");
        }

        if (hasToken)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private static JsonObject? MergeDownloads(
        JsonNode? value,
        VersionLayer layer,
        JsonObject? target,
        JsonObject output)
    {
        if (value is null)
        {
            return target;
        }

        if (value is not JsonObject source)
        {
            throw new VersionMergeException(
                VersionMergeErrorCode.InvalidLayerJson,
                $"version '{layer.Id}' has a non-object downloads");
        }

        if (target is null)
        {
            target = new JsonObject();
            output[Downloads] = target;
        }

        // Leaf wins per property, so a child downloads block without "client"
        // keeps the inherited client artifact.
        foreach (var property in source)
        {
            target[property.Key] = property.Value?.DeepClone();
        }

        return target;
    }

    private static JsonObject? MergeLogging(
        JsonNode? value,
        VersionLayer layer,
        JsonObject? target,
        JsonObject output)
    {
        if (value is null)
        {
            return target;
        }

        if (value is not JsonObject source)
        {
            throw new VersionMergeException(
                VersionMergeErrorCode.InvalidLayerJson,
                $"version '{layer.Id}' has a non-object logging");
        }

        if (target is null)
        {
            target = new JsonObject();
            output[Logging] = target;
        }

        foreach (var property in source)
        {
            if (property.Key == "client" && property.Value is JsonObject client)
            {
                // Merge client per property: a child that only overrides
                // "argument" keeps the inherited config file (Log4Shell).
                var mergedClient = target["client"] as JsonObject;
                if (mergedClient is null)
                {
                    mergedClient = new JsonObject();
                    target["client"] = mergedClient;
                }

                foreach (var clientProperty in client)
                {
                    mergedClient[clientProperty.Key] = clientProperty.Value?.DeepClone();
                }

                continue;
            }

            target[property.Key] = property.Value?.DeepClone();
        }

        return target;
    }

    private static int? MaxInt(
        JsonNode? value,
        string field,
        string layerId,
        int? current)
    {
        if (value is null)
        {
            return current;
        }

        if (value is not JsonValue jsonValue || !jsonValue.TryGetValue<int>(out var parsed))
        {
            throw new VersionMergeException(
                VersionMergeErrorCode.InvalidLayerJson,
                $"version '{layerId}' has a non-integer {field}");
        }

        return current is null ? parsed : Math.Max(current.Value, parsed);
    }

    private static JsonArray ToArray(List<JsonNode?> nodes)
    {
        var array = new JsonArray();
        foreach (var node in nodes)
        {
            array.Add(node);
        }

        return array;
    }

    private static string ResolveId(
        JsonObject output,
        VersionLayer leaf,
        JsonObject? leafSource)
    {
        // The id comes from the leaf layer only; never from an ancestor.
        if (leafSource is not null
            && leafSource.TryGetPropertyValue("id", out var leafIdNode)
            && leafIdNode is not null)
        {
            if (leafIdNode is JsonValue leafIdValue
                && leafIdValue.TryGetValue<string>(out var leafId)
                && !string.IsNullOrWhiteSpace(leafId))
            {
                output["id"] = leafId;
                return leafId;
            }

            throw new VersionMergeException(
                VersionMergeErrorCode.InvalidLayerJson,
                $"version '{leaf.Id}' has a non-string or empty id");
        }

        if (string.IsNullOrWhiteSpace(leaf.Id))
        {
            throw new VersionMergeException(
                VersionMergeErrorCode.InvalidRequest,
                "effective version has no id");
        }

        output["id"] = leaf.Id;
        return leaf.Id;
    }

    private static byte[] WriteCompact(JsonObject root)
    {
        var buffer = new ArrayBufferWriter<byte>(4096);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            root.WriteTo(writer);
        }

        return buffer.WrittenSpan.ToArray();
    }

    private sealed class LibrarySlot
    {
        public LibrarySlot(string version, JsonObject node)
        {
            Version = version;
            Node = node;
        }

        public string Version { get; set; }

        public JsonObject Node { get; set; }
    }
}
