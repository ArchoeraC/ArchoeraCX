using System.Text.Json;
using Archoera.Application.Instances;
using Archoera.Application.Modules;
using Archoera.Domain;
using Archoera.Domain.Instances;
using Archoera.Infrastructure.Settings;

namespace Archoera.Infrastructure.Instances;

/// <summary>JSON facade for instance.list/create/update/delete/duplicate.</summary>
public sealed partial class InstanceModuleService : IModuleMethodHandler
{
    private const string ListMethod = "instance.list";
    private const string CreateMethod = "instance.create";
    private const string UpdateMethod = "instance.update";
    private const string DeleteMethod = "instance.delete";
    private const string DuplicateMethod = "instance.duplicate";
    private const string SyncMethod = "instance.sync";
    private const string ModsListMethod = "instance.mods.list";
    private const string ModsSetEnabledMethod = "instance.mods.setEnabled";
    private const string ModsRemoveMethod = "instance.mods.remove";

    private static readonly string[] SupportedMethods =
        [
            ListMethod, CreateMethod, UpdateMethod, DeleteMethod, DuplicateMethod,
            SyncMethod, ModsListMethod, ModsSetEnabledMethod, ModsRemoveMethod,
        ];

    private static StringComparison PathComparison => PathSemantics.Comparison;

    private readonly IInstanceRepository _repository;
    private readonly JsonAppSettingsStore _settings;
    private readonly Dictionary<string, ModuleMethodDelegate> _handlers;

    public InstanceModuleService(
        IInstanceRepository? repository = null,
        JsonAppSettingsStore? settings = null)
    {
        _settings = settings ?? new JsonAppSettingsStore();
        _repository = repository ?? new SqliteInstanceRepository(settings: _settings);
        _handlers = new Dictionary<string, ModuleMethodDelegate>(StringComparer.Ordinal)
        {
            [ListMethod] = ListAsync,
            [CreateMethod] = CreateAsync,
            [UpdateMethod] = UpdateAsync,
            [DeleteMethod] = DeleteAsync,
            [DuplicateMethod] = DuplicateAsync,
            [SyncMethod] = SyncAsync,
            [ModsListMethod] = ListModsAsync,
            [ModsSetEnabledMethod] = SetModEnabledAsync,
            [ModsRemoveMethod] = RemoveModAsync,
        };
    }

    public IReadOnlyCollection<string> Methods => SupportedMethods;

    public async Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        try
        {
            return _handlers.TryGetValue(method, out var handler)
                ? await handler(paramsJson, cancellationToken).ConfigureAwait(false)
                : Error("E_UNSUPPORTED", $"unsupported instance method '{method}'");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Error("E_INVALID", exception.Message);
        }
        catch (Exception exception)
        {
            return Error("E_INTERNAL", exception.Message);
        }
    }

    private async Task<byte[]> ListAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        var items = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var roots = (await _settings.LoadAsync(cancellationToken).ConfigureAwait(false)).Roots;
        return BuildEnvelope(writer =>
        {
            writer.WriteStartArray("items");
            foreach (var item in items) WriteRecord(writer, item, roots);
            writer.WriteEndArray();
        });
    }

    private async Task<byte[]> SyncAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        using var document = Parse(paramsJson);
        var rootElement = document.RootElement;
        var roots = new List<string>();
        if (rootElement.TryGetProperty("roots", out var rootsElement) &&
            rootsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in rootsElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(item.GetString()))
                {
                    roots.Add(item.GetString()!);
                }
            }
        }

        var dryRun = rootElement.TryGetProperty("dryRun", out var dryRunElement) &&
                     dryRunElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                     dryRunElement.GetBoolean();
        var report = await _repository
            .SyncAsync(roots.Count > 0 ? roots : null, dryRun, cancellationToken)
            .ConfigureAwait(false);
        var configured = (await _settings.LoadAsync(cancellationToken).ConfigureAwait(false)).Roots;
        return BuildEnvelope(writer =>
        {
            writer.WriteStartArray("items");
            foreach (var item in report.Items) WriteRecord(writer, item, configured);
            writer.WriteEndArray();
            writer.WriteStartArray("adopted");
            foreach (var id in report.Adopted) writer.WriteStringValue(id);
            writer.WriteEndArray();
            writer.WriteStartArray("updated");
            foreach (var id in report.Updated) writer.WriteStringValue(id);
            writer.WriteEndArray();
            writer.WriteStartArray("missing");
            foreach (var item in report.Missing)
            {
                writer.WriteStartObject();
                writer.WriteString("id", item.Id);
                writer.WriteString("rootPath", item.RootPath);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("roots");
            foreach (var root in report.Roots)
            {
                writer.WriteStartObject();
                writer.WriteString("path", root.Path);
                writer.WriteBoolean("exists", root.Exists);
                writer.WriteNumber("instanceCount", root.InstanceCount);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        });
    }

    private async Task<byte[]> CreateAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        using var document = Parse(paramsJson);
        var root = document.RootElement;
        var record = await _repository.CreateAsync(
                ReadString(root, "name") ?? throw new ArgumentException("name is required"),
                ReadString(root, "gameVersion") ?? throw new ArgumentException("gameVersion is required"),
                ReadString(root, "loader") ?? "vanilla",
                ReadString(root, "loaderVersion") ?? string.Empty,
                ReadString(root, "rootPath"),
                cancellationToken)
            .ConfigureAwait(false);
        var roots = (await _settings.LoadAsync(cancellationToken).ConfigureAwait(false)).Roots;
        return BuildEnvelope(writer =>
        {
            writer.WritePropertyName("instance");
            WriteRecord(writer, record, roots);
        });
    }

    private async Task<byte[]> UpdateAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        using var document = Parse(paramsJson);
        var root = document.RootElement;
        var id = ReadString(root, "id") ?? throw new ArgumentException("id is required");
        var name = ReadString(root, "name");
        bool? favorite = root.TryGetProperty("favorite", out var favoriteElement) &&
                         favoriteElement.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? favoriteElement.GetBoolean()
            : null;
        DateTimeOffset? lastPlayed = null;
        if (ReadString(root, "lastPlayedAt") is { } lastPlayedText)
        {
            if (!DateTimeOffset.TryParse(lastPlayedText, out var parsed))
            {
                throw new ArgumentException("lastPlayedAt must be an ISO timestamp");
            }

            lastPlayed = parsed;
        }

        var overridesPresent = root.TryGetProperty("overrides", out var overridesElement);
        InstanceOverrides? mergedOverrides = null;
        if (overridesPresent)
        {
            if (overridesElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("overrides must be an object");
            }

            // Merge patch: a missing key keeps the stored value, an explicit
            // null clears it, and anything else replaces it.
            var items = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);
            var current = items.FirstOrDefault(
                    item => string.Equals(item.Id, id, StringComparison.Ordinal))
                ?? throw new ArgumentException("instance not found");
            mergedOverrides = ApplyOverridesPatch(current.Overrides, overridesElement);
        }

        if (name is null && favorite is null && lastPlayed is null && !overridesPresent)
        {
            throw new ArgumentException("instance.update requires at least one field");
        }

        var record = await _repository
            .UpdateAsync(
                id,
                name,
                favorite,
                lastPlayed,
                mergedOverrides,
                overridesPresent,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ArgumentException("instance not found");
        var roots = (await _settings.LoadAsync(cancellationToken).ConfigureAwait(false)).Roots;
        return BuildEnvelope(writer =>
        {
            writer.WritePropertyName("instance");
            WriteRecord(writer, record, roots);
        });
    }

    private static InstanceOverrides? ApplyOverridesPatch(
        InstanceOverrides? current,
        JsonElement patch)
    {
        var memoryMin = current?.MemoryMinMb;
        var memoryMax = current?.MemoryMaxMb;
        var jvmArgs = current?.JvmArgs;
        var gcMode = current?.GcMode;
        var gameArgs = current?.GameArgs;
        var javaPath = current?.JavaPath;
        var windowWidth = current?.WindowWidth;
        var windowHeight = current?.WindowHeight;
        var fullscreen = current?.Fullscreen;
        var maximized = current?.Maximized;
        var enableGameMode = current?.EnableGameMode;
        var enableMangoHud = current?.EnableMangoHud;
        var useZink = current?.UseZink;
        var wrapperCommand = current?.WrapperCommand;
        IReadOnlyDictionary<string, string>? environment = current?.Environment;
        var joinServerOnLaunch = current?.JoinServerOnLaunch;
        var joinServerAddress = current?.JoinServerAddress;
        var joinWorldName = current?.JoinWorldName;
        var preLoadCommand = current?.PreLoadCommand;
        var preLaunchCommand = current?.PreLaunchCommand;
        var postExitCommand = current?.PostExitCommand;

        if (patch.TryGetProperty("memoryMinMb", out var memoryMinElement))
        {
            memoryMin = ReadOverrideInt(memoryMinElement, "memoryMinMb", 128, 65536);
        }

        if (patch.TryGetProperty("memoryMaxMb", out var memoryMaxElement))
        {
            memoryMax = ReadOverrideInt(memoryMaxElement, "memoryMaxMb", 128, 65536);
        }

        if (patch.TryGetProperty("jvmArgs", out var jvmElement))
        {
            jvmArgs = NormalizeArgument(ReadOverrideString(jvmElement, "jvmArgs"));
        }

        if (patch.TryGetProperty("gcMode", out var gcElement))
        {
            gcMode = ReadOverrideString(gcElement, "gcMode");
            if (gcMode is not null &&
                !InstanceOverrides.GcModes.Contains(gcMode, StringComparer.Ordinal))
            {
                throw new ArgumentException("unsupported gcMode");
            }
        }

        if (patch.TryGetProperty("gameArgs", out var gameElement))
        {
            gameArgs = NormalizeArgument(ReadOverrideString(gameElement, "gameArgs"));
        }

        if (patch.TryGetProperty("javaPath", out var javaElement))
        {
            javaPath = ReadOverrideString(javaElement, "javaPath");
            if (javaPath is not null && !Path.IsPathFullyQualified(javaPath))
            {
                throw new ArgumentException("javaPath must be an absolute path or null");
            }
        }

        if (patch.TryGetProperty("windowWidth", out var widthElement))
        {
            windowWidth = ReadOverrideInt(widthElement, "windowWidth", 640, 7680);
        }

        if (patch.TryGetProperty("windowHeight", out var heightElement))
        {
            windowHeight = ReadOverrideInt(heightElement, "windowHeight", 480, 4320);
        }

        if (patch.TryGetProperty("fullscreen", out var fullscreenElement))
        {
            fullscreen = ReadOverrideBool(fullscreenElement, "fullscreen");
        }

        if (patch.TryGetProperty("maximized", out var maximizedElement))
        {
            maximized = ReadOverrideBool(maximizedElement, "maximized");
        }

        if (patch.TryGetProperty("enableGameMode", out var gameModeElement))
        {
            enableGameMode = ReadOverrideBool(gameModeElement, "enableGameMode");
        }

        if (patch.TryGetProperty("enableMangoHud", out var mangoHudElement))
        {
            enableMangoHud = ReadOverrideBool(mangoHudElement, "enableMangoHud");
        }

        if (patch.TryGetProperty("useZink", out var zinkElement))
        {
            useZink = ReadOverrideBool(zinkElement, "useZink");
        }

        if (patch.TryGetProperty("wrapperCommand", out var wrapperElement))
        {
            var raw = ReadOverrideString(wrapperElement, "wrapperCommand");
            // Null inherits, an empty string explicitly disables the wrapper.
            wrapperCommand = raw is null
                ? null
                : string.IsNullOrWhiteSpace(raw)
                    ? string.Empty
                    : RuntimeOptionsJson.NormalizeWrapper(raw);
        }

        if (patch.TryGetProperty("environment", out var environmentElement))
        {
            // JSON null inherits the global map; `{}` explicitly clears it.
            environment = RuntimeOptionsJson.ReadEnvironment(environmentElement);
        }

        if (patch.TryGetProperty("joinServerOnLaunch", out var joinElement))
        {
            joinServerOnLaunch = ReadOverrideBool(joinElement, "joinServerOnLaunch");
        }

        if (patch.TryGetProperty("joinServerAddress", out var addressElement))
        {
            joinServerAddress = InstanceJoinRules.NormalizeAddress(
                ReadOverrideString(addressElement, "joinServerAddress"));
        }

        if (patch.TryGetProperty("joinWorldName", out var worldElement))
        {
            joinWorldName = InstanceJoinRules.NormalizeWorldName(
                ReadOverrideString(worldElement, "joinWorldName"));
        }

        if (patch.TryGetProperty("preLoadCommand", out var preLoadElement))
        {
            preLoadCommand = ReadOverrideCommand(preLoadElement, "preLoadCommand");
        }

        if (patch.TryGetProperty("preLaunchCommand", out var preLaunchElement))
        {
            preLaunchCommand = ReadOverrideCommand(preLaunchElement, "preLaunchCommand");
        }

        if (patch.TryGetProperty("postExitCommand", out var postExitElement))
        {
            postExitCommand = ReadOverrideCommand(postExitElement, "postExitCommand");
        }

        if (memoryMin is { } minimum && memoryMax is { } maximum && minimum > maximum)
        {
            throw new ArgumentException("memoryMinMb must be <= memoryMaxMb");
        }

        // Fullscreen and maximized are alternative window modes. Validate the
        // merged object so a patch that only sets one side cannot conflict with
        // an already stored value on the other side.
        if (fullscreen == true && maximized == true)
        {
            throw new ArgumentException(
                "fullscreen and maximized cannot both be enabled");
        }

        InstanceJoinRules.ValidateTarget(
            joinServerOnLaunch,
            joinServerAddress,
            joinWorldName);

        // Join fields are meaningful only while the switch is on. An explicit
        // false (the UI sends it to clear the switch) drops the whole group so
        // an instance does not keep a stale auto-join badge.
        if (joinServerOnLaunch != true)
        {
            joinServerOnLaunch = null;
            joinServerAddress = null;
            joinWorldName = null;
        }

        var merged = new InstanceOverrides(
            MemoryMinMb: memoryMin,
            MemoryMaxMb: memoryMax,
            JvmArgs: jvmArgs,
            GcMode: gcMode,
            GameArgs: gameArgs,
            JavaPath: javaPath,
            WindowWidth: windowWidth,
            WindowHeight: windowHeight,
            Fullscreen: fullscreen,
            WrapperCommand: wrapperCommand,
            Environment: environment,
            JoinServerOnLaunch: joinServerOnLaunch,
            JoinServerAddress: joinServerAddress,
            JoinWorldName: joinWorldName,
            PreLoadCommand: preLoadCommand,
            PreLaunchCommand: preLaunchCommand,
            PostExitCommand: postExitCommand,
            Maximized: maximized,
            EnableGameMode: enableGameMode,
            EnableMangoHud: enableMangoHud,
            UseZink: useZink);
        return merged.IsEmpty ? null : merged;
    }

    /// <summary>
    /// Per-instance command semantics: null inherits the global command, a
    /// whitespace-only string explicitly disables it, and anything else is
    /// validated and tokenized with the shared command-line tokenizer.
    /// </summary>
    private static string? ReadOverrideCommand(JsonElement element, string name)
    {
        var raw = ReadOverrideString(element, name);
        if (raw is null) return null;
        return string.IsNullOrWhiteSpace(raw)
            ? string.Empty
            : RuntimeOptionsJson.NormalizeCommand(raw, name);
    }

    private static int? ReadOverrideInt(JsonElement element, string name, int min, int max)
    {
        if (element.ValueKind == JsonValueKind.Null) return null;
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value))
        {
            throw new ArgumentException($"{name} must be an integer or null");
        }

        if (value < min || value > max)
        {
            throw new ArgumentException($"{name} must be within {min}..{max}");
        }

        return value;
    }

    private static string? ReadOverrideString(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Null) return null;
        if (element.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException($"{name} must be a string or null");
        }

        var value = element.GetString();
        if (value is not null && value.Length > 8192)
        {
            throw new ArgumentException($"{name} must be at most 8192 characters");
        }

        return value;
    }

    private static bool? ReadOverrideBool(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Null) return null;
        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new ArgumentException($"{name} must be a boolean or null");
        }

        return element.GetBoolean();
    }

    private static string? NormalizeArgument(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private async Task<byte[]> DeleteAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        using var document = Parse(paramsJson);
        var rootElement = document.RootElement;
        var id = ReadString(rootElement, "id") ?? throw new ArgumentException("id is required");
        var deleteFiles = rootElement.TryGetProperty("deleteFiles", out var deleteFilesElement) &&
                          deleteFilesElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                          deleteFilesElement.GetBoolean();

        // Read the record first: the registry row disappears below and the
        // cleaner needs the original path plus every sibling instance path.
        var items = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var record = items.FirstOrDefault(item => item.Id == id);

        var deleted = await _repository.DeleteAsync(id, cancellationToken).ConfigureAwait(false);

        var filesDeleted = false;
        string? filesSkippedReason = null;
        if (deleteFiles && record is not null)
        {
            var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
            var siblings = items
                .Where(item => !string.Equals(item.Id, id, StringComparison.Ordinal))
                .Select(item => item.RootPath)
                .ToArray();
            filesDeleted = InstanceDirectoryCleaner.TryDelete(
                record.RootPath,
                settings.Roots,
                siblings,
                out filesSkippedReason);
        }

        return BuildEnvelope(writer =>
        {
            writer.WriteBoolean("deleted", deleted);
            writer.WriteBoolean("filesDeleted", filesDeleted);
            writer.WriteString("filesSkippedReason", filesSkippedReason);
        });
    }

    private static JsonDocument Parse(string? paramsJson)
    {
        if (string.IsNullOrWhiteSpace(paramsJson)) return JsonDocument.Parse("{}");
        try
        {
            return JsonDocument.Parse(paramsJson);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"request is not valid JSON: {exception.Message}");
        }
    }

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static void WriteRecord(
        Utf8JsonWriter writer,
        GameInstanceRecord record,
        IReadOnlyList<string> roots)
    {
        var instanceRoot = roots.FirstOrDefault(root => IsWithin(record.RootPath, root));
        writer.WriteStartObject();
        writer.WriteString("id", record.Id);
        writer.WriteString("name", record.Name);
        writer.WriteString("gameVersion", record.GameVersion);
        writer.WriteString("loader", record.Loader);
        writer.WriteString("loaderVersion", record.LoaderVersion);
        writer.WriteString("rootPath", record.RootPath);
        if (instanceRoot is not null) writer.WriteString("instanceRoot", instanceRoot);
        writer.WriteBoolean("custom", instanceRoot is null);
        writer.WriteBoolean("missing", !Directory.Exists(record.RootPath));
        writer.WriteNumber("modCount", record.ModCount);
        writer.WriteBoolean("favorite", record.Favorite);
        writer.WriteString("createdAt", record.CreatedAt.ToString("O"));
        writer.WriteString("updatedAt", record.UpdatedAt.ToString("O"));
        writer.WriteString("lastPlayedAt", record.LastPlayedAt?.ToString("O"));
        WriteOverrides(writer, record.Overrides);
        writer.WriteEndObject();
    }

    private static void WriteOverrides(Utf8JsonWriter writer, InstanceOverrides? overrides)
        => InstanceOverridesJson.Write(writer, overrides, omitWhenEmpty: false);

    private static bool IsWithin(string path, string root)
    {
        try
        {
            var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            return fullPath.Equals(fullRoot, PathComparison) ||
                   fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, PathComparison);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] BuildEnvelope(Action<Utf8JsonWriter> data)
    {
        using var memory = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memory))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", true);
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

    private static byte[] Error(string code, string message)
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
