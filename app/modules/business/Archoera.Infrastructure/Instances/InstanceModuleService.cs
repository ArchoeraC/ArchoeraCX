using System.Text.Json;
using Archoera.Application.Instances;
using Archoera.Application.Modules;
using Archoera.Infrastructure.Settings;
using Archoera.Domain.Instances;

namespace Archoera.Infrastructure.Instances;

/// <summary>JSON facade for instance.list/create/update/delete.</summary>
public sealed class InstanceModuleService : IModuleMethodHandler
{
    private static readonly string[] SupportedMethods =
        [
            "instance.list", "instance.create", "instance.update", "instance.delete", "instance.sync",
            "instance.mods.list", "instance.mods.setEnabled", "instance.mods.remove",
        ];

    private readonly IInstanceRepository _repository;
    private readonly JsonAppSettingsStore _settings;

    public InstanceModuleService(
        IInstanceRepository? repository = null,
        JsonAppSettingsStore? settings = null)
    {
        _settings = settings ?? new JsonAppSettingsStore();
        _repository = repository ?? new SqliteInstanceRepository(settings: _settings);
    }

    public IReadOnlyCollection<string> Methods => SupportedMethods;

    public async Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        try
        {
            return method switch
            {
                "instance.list" => await ListAsync(cancellationToken).ConfigureAwait(false),
                "instance.create" => await CreateAsync(paramsJson, cancellationToken)
                    .ConfigureAwait(false),
                "instance.update" => await UpdateAsync(paramsJson, cancellationToken)
                    .ConfigureAwait(false),
                "instance.delete" => await DeleteAsync(paramsJson, cancellationToken)
                    .ConfigureAwait(false),
                "instance.sync" => await SyncAsync(paramsJson, cancellationToken)
                    .ConfigureAwait(false),
                "instance.mods.list" => await ListModsAsync(paramsJson, cancellationToken)
                    .ConfigureAwait(false),
                "instance.mods.setEnabled" => await SetModEnabledAsync(paramsJson, cancellationToken)
                    .ConfigureAwait(false),
                "instance.mods.remove" => await RemoveModAsync(paramsJson, cancellationToken)
                    .ConfigureAwait(false),
                _ => Error("E_UNSUPPORTED", $"unsupported instance method '{method}'"),
            };
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

    private async Task<byte[]> ListAsync(CancellationToken cancellationToken)
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

        var record = await _repository
            .UpdateAsync(id, name, favorite, lastPlayed, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ArgumentException("instance not found");
        var roots = (await _settings.LoadAsync(cancellationToken).ConfigureAwait(false)).Roots;
        return BuildEnvelope(writer =>
        {
            writer.WritePropertyName("instance");
            WriteRecord(writer, record, roots);
        });
    }

    private async Task<byte[]> DeleteAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        using var document = Parse(paramsJson);
        var id = ReadString(document.RootElement, "id") ?? throw new ArgumentException("id is required");
        var deleted = await _repository.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        return BuildEnvelope(writer => writer.WriteBoolean("deleted", deleted));
    }

    private async Task<byte[]> ListModsAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        using var document = Parse(paramsJson);
        var instanceId = ReadString(document.RootElement, "instanceId")
            ?? throw new ArgumentException("instanceId is required");
        var directory = await ResolveModsDirectoryAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);
        var files = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Where(IsModFileName)
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

        return BuildEnvelope(writer =>
        {
            writer.WriteStartArray("items");
            foreach (var file in files) WriteMod(writer, file);
            writer.WriteEndArray();
            writer.WriteNumber("count", files.Length);
        });
    }

    private async Task<byte[]> SetModEnabledAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        using var document = Parse(paramsJson);
        var root = document.RootElement;
        var instanceId = ReadString(root, "instanceId")
            ?? throw new ArgumentException("instanceId is required");
        var fileName = NormalizeModFileName(
            ReadString(root, "fileName")
            ?? throw new ArgumentException("fileName is required"));
        var enabled = root.TryGetProperty("enabled", out var enabledElement) &&
                      enabledElement.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? enabledElement.GetBoolean()
            : throw new ArgumentException("enabled must be a boolean");
        var directory = await ResolveModsDirectoryAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);
        var source = Path.Combine(directory, fileName);
        if (!File.Exists(source))
        {
            throw new ArgumentException("mod file not found");
        }

        var baseName = fileName.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^".disabled".Length]
            : fileName;
        var targetName = enabled ? baseName : baseName + ".disabled";
        var target = Path.Combine(directory, targetName);
        if (!string.Equals(source, target, StringComparison.Ordinal))
        {
            if (File.Exists(target))
            {
                throw new ArgumentException("target mod file already exists");
            }
            File.Move(source, target);
        }

        return BuildEnvelope(writer =>
        {
            writer.WritePropertyName("item");
            WriteMod(writer, target);
        });
    }

    private async Task<byte[]> RemoveModAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        using var document = Parse(paramsJson);
        var root = document.RootElement;
        var instanceId = ReadString(root, "instanceId")
            ?? throw new ArgumentException("instanceId is required");
        var fileName = NormalizeModFileName(
            ReadString(root, "fileName")
            ?? throw new ArgumentException("fileName is required"));
        var directory = await ResolveModsDirectoryAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            throw new ArgumentException("mod file not found");
        }
        File.Delete(path);
        return BuildEnvelope(writer => writer.WriteBoolean("removed", true));
    }

    private async Task<string> ResolveModsDirectoryAsync(
        string instanceId,
        CancellationToken cancellationToken)
    {
        var items = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var record = items.FirstOrDefault(item => item.Id == instanceId)
            ?? throw new ArgumentException("instance not found");
        if (!Directory.Exists(record.RootPath))
        {
            throw new ArgumentException("instance directory is missing");
        }
        return Path.Combine(record.RootPath, "mods");
    }

    private static string NormalizeModFileName(string value)
    {
        var name = value.Trim();
        if (name.Length == 0 ||
            name.Contains('/') ||
            name.Contains('\\') ||
            name is "." or ".." ||
            !IsModFileName(name))
        {
            throw new ArgumentException("invalid mod file name");
        }
        return name;
    }

    private static bool IsModFileName(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteMod(Utf8JsonWriter writer, string path)
    {
        var fileName = Path.GetFileName(path);
        var disabled = fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
        var name = disabled ? fileName[..^".disabled".Length] : fileName;
        var info = new FileInfo(path);
        writer.WriteStartObject();
        writer.WriteString("fileName", fileName);
        writer.WriteString("name", name);
        writer.WriteBoolean("enabled", !disabled);
        writer.WriteNumber("size", info.Exists ? info.Length : 0);
        writer.WriteString("modifiedAt", info.Exists ? info.LastWriteTimeUtc.ToString("O") : null);
        writer.WriteEndObject();
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
        writer.WriteEndObject();
    }

    private static bool IsWithin(string path, string root)
    {
        try
        {
            var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return fullPath.Equals(fullRoot, comparison) ||
                   fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison);
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
