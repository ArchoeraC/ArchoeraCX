using System.Text.Json;
using Archoera.Application.Instances;
using Archoera.Domain.Instances;
using Archoera.Infrastructure.Settings;

namespace Archoera.Infrastructure.Instances;

/// <summary>
/// JSON-backed first implementation of the instance repository. Each instance
/// owns <c>.archoera/instance.json</c>; <c>instances.json</c> in the data root
/// is a registry that lets custom locations be discovered. Replace this class
/// with the SQLite index later without changing the module contract.
/// </summary>
public sealed class JsonInstanceRepository : IInstanceRepository
{
    private static readonly string[] Loaders =
    [
        "vanilla", "fabric", "quilt", "forge", "neoforge",
    ];

    private readonly string _dataRoot;
    private readonly string _registryPath;
    private readonly JsonAppSettingsStore _settings;

    public JsonInstanceRepository(
        string? dataRoot = null,
        JsonAppSettingsStore? settings = null)
    {
        _settings = settings ?? new JsonAppSettingsStore(dataRoot);
        _dataRoot = _settings.DataRoot;
        _registryPath = Path.Combine(_dataRoot, "instances.json");
    }

    public async Task<IReadOnlyList<GameInstanceRecord>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var registry = await ReadRegistryAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<GameInstanceRecord>(registry.Count);
        foreach (var entry in registry)
        {
            var record = await ReadInstanceFileAsync(entry.RootPath, cancellationToken)
                .ConfigureAwait(false);
            if (record is not null)
            {
                results.Add(record);
            }
            else if (Directory.Exists(entry.RootPath))
            {
                // A user removed .archoera/instance.json; keep the registry entry
                // visible with the last known metadata instead of silently losing it.
                results.Add(entry);
            }
        }

        return results
            .OrderByDescending(record => record.LastPlayedAt ?? record.UpdatedAt)
            .ThenBy(record => record.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<GameInstanceRecord> CreateAsync(
        string name,
        string gameVersion,
        string loader,
        string loaderVersion,
        string? rootPath,
        CancellationToken cancellationToken = default)
    {
        var cleanName = ValidateName(name);
        var cleanGame = ValidateSegment(gameVersion, "gameVersion");
        var cleanLoader = ValidateLoader(loader);
        var cleanLoaderVersion = string.IsNullOrWhiteSpace(loaderVersion)
            ? string.Empty
            : ValidateVersion(loaderVersion);

        var root = await ResolveRootAsync(cleanName, rootPath, cancellationToken)
            .ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var record = new GameInstanceRecord(
            Id: CreateId(now),
            Name: cleanName,
            GameVersion: cleanGame,
            Loader: cleanLoader,
            LoaderVersion: cleanLoaderVersion,
            RootPath: root,
            ModCount: 0,
            Favorite: false,
            CreatedAt: now,
            UpdatedAt: now,
            LastPlayedAt: null);

        Directory.CreateDirectory(root);
        await WriteInstanceFileAsync(record, cancellationToken).ConfigureAwait(false);
        var registry = await ReadRegistryAsync(cancellationToken).ConfigureAwait(false);
        registry.RemoveAll(entry => string.Equals(entry.Id, record.Id, StringComparison.Ordinal));
        registry.Add(record);
        await WriteRegistryAsync(registry, cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<GameInstanceRecord?> UpdateAsync(
        string id,
        string? name,
        bool? favorite,
        DateTimeOffset? lastPlayedAt,
        CancellationToken cancellationToken = default)
    {
        var cleanId = ValidateId(id);
        var registry = await ReadRegistryAsync(cancellationToken).ConfigureAwait(false);
        var index = registry.FindIndex(entry => string.Equals(entry.Id, cleanId, StringComparison.Ordinal));
        if (index < 0) return null;

        var current = registry[index];
        var updated = current with
        {
            Name = name is null ? current.Name : ValidateName(name),
            Favorite = favorite ?? current.Favorite,
            LastPlayedAt = lastPlayedAt ?? current.LastPlayedAt,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await WriteInstanceFileAsync(updated, cancellationToken).ConfigureAwait(false);
        registry[index] = updated;
        await WriteRegistryAsync(registry, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<bool> DeleteAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var cleanId = ValidateId(id);
        var registry = await ReadRegistryAsync(cancellationToken).ConfigureAwait(false);
        var removed = registry.RemoveAll(
            entry => string.Equals(entry.Id, cleanId, StringComparison.Ordinal)) > 0;
        if (!removed) return false;

        await WriteRegistryAsync(registry, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<InstanceSyncReport> SyncAsync(
        IReadOnlyList<string>? roots,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        var configured = (roots is { Count: > 0 } ? roots : settings.Roots)
            .Where(root => !string.IsNullOrWhiteSpace(root) && Path.IsPathFullyQualified(root))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var registry = await ReadRegistryAsync(cancellationToken).ConfigureAwait(false);
        var byId = registry.ToDictionary(record => record.Id, StringComparer.Ordinal);
        var found = new Dictionary<string, GameInstanceRecord>(StringComparer.Ordinal);
        var rootCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var root in configured)
        {
            var count = 0;
            if (Directory.Exists(root))
            {
                IEnumerable<string> directories;
                try
                {
                    directories = Directory.GetDirectories(root);
                }
                catch
                {
                    directories = [];
                }

                foreach (var directory in directories)
                {
                    var record = await ReadInstanceFileAsync(directory, cancellationToken)
                        .ConfigureAwait(false);
                    if (record is null || found.ContainsKey(record.Id)) continue;
                    found[record.Id] = record;
                    count++;
                }
            }

            rootCounts[root] = count;
        }

        var adopted = new List<string>();
        var updated = new List<string>();
        foreach (var record in found.Values)
        {
            if (!byId.TryGetValue(record.Id, out var current))
            {
                registry.Add(record);
                adopted.Add(record.Id);
                continue;
            }

            var index = registry.FindIndex(item => item.Id == record.Id);
            if (index >= 0)
            {
                registry[index] = record;
                updated.Add(record.Id);
            }
        }

        var missing = registry
            .Where(record => !found.ContainsKey(record.Id) && !Directory.Exists(record.RootPath))
            .Select(record => new InstanceSyncMissing(record.Id, record.RootPath))
            .ToArray();

        if (!dryRun && (adopted.Count > 0 || updated.Count > 0))
        {
            await WriteRegistryAsync(registry, cancellationToken).ConfigureAwait(false);
        }

        return new InstanceSyncReport(
            registry,
            adopted,
            updated,
            missing,
            configured
                .Select(root => new InstanceSyncRoot(
                    root,
                    Directory.Exists(root),
                    rootCounts.GetValueOrDefault(root)))
                .ToArray());
    }

    private async Task<string> ResolveRootAsync(
        string name,
        string? rootPath,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(rootPath))
        {
            if (!Path.IsPathFullyQualified(rootPath))
            {
                throw new ArgumentException("rootPath must be absolute", nameof(rootPath));
            }

            return AppDataPaths.Normalize(rootPath);
        }

        var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        var slug = Slug(name);
        // Match the SQLite repository: version isolation is the default.
        return Path.Combine(settings.Roots[0], "versions", slug);
    }

    private static string CreateId(DateTimeOffset now)
        => $"inst-{now.ToUnixTimeMilliseconds():x}-{Guid.NewGuid():N}"[..24];

    private static string ValidateName(string name)
    {
        var clean = name?.Trim() ?? string.Empty;
        if (clean.Length == 0 || clean.Length > 80 || clean.Any(char.IsControl))
        {
            throw new ArgumentException("instance name is required and must be <= 80 characters");
        }

        return clean;
    }

    private static string ValidateSegment(string value, string what)
    {
        var clean = value?.Trim() ?? string.Empty;
        if (clean.Length == 0 || clean is "." or ".." || clean.Any(char.IsControl) ||
            clean.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0)
        {
            throw new ArgumentException($"{what} is not a safe path segment");
        }

        return clean;
    }

    private static string ValidateVersion(string value)
        => ValidateSegment(value, "loaderVersion");

    private static string ValidateLoader(string loader)
    {
        var clean = loader?.Trim().ToLowerInvariant() ?? "vanilla";
        if (!Loaders.Contains(clean, StringComparer.Ordinal))
        {
            throw new ArgumentException($"unsupported loader '{loader}'");
        }

        return clean;
    }

    private static string ValidateId(string id)
    {
        var clean = id?.Trim() ?? string.Empty;
        if (clean.Length == 0 || clean.Length > 80 ||
            clean.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0)
        {
            throw new ArgumentException("instance id is invalid");
        }

        return clean;
    }

    private static string Slug(string name)
    {
        var chars = name.Trim().ToLowerInvariant()
            .Select(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'
                ? character
                : '-')
            .ToArray();
        var slug = new string(chars).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(slug) ? "instance" : slug;
    }

    private async Task<List<GameInstanceRecord>> ReadRegistryAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_registryPath)) return [];

        try
        {
            await using var stream = new FileStream(
                _registryPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            using var document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("instances", out var items) ||
                items.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var results = new List<GameInstanceRecord>();
            foreach (var item in items.EnumerateArray())
            {
                var record = ParseRecord(item);
                if (record is not null) results.Add(record);
            }

            return results;
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private async Task WriteRegistryAsync(
        IReadOnlyList<GameInstanceRecord> records,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_dataRoot);
        var temporary = _registryPath + ".tmp";
        await using (var stream = new FileStream(
                         temporary,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 4096,
                         useAsync: true))
        {
            await WriteRecordsAsync(stream, records, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporary, _registryPath, overwrite: true);
    }

    private async Task WriteInstanceFileAsync(
        GameInstanceRecord record,
        CancellationToken cancellationToken)
    {
        var metadataDirectory = Path.Combine(record.RootPath, ".archoera");
        Directory.CreateDirectory(metadataDirectory);
        var path = Path.Combine(metadataDirectory, "instance.json");
        var temporary = path + ".tmp";
        await using (var stream = new FileStream(
                         temporary,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 4096,
                         useAsync: true))
        {
            await WriteRecordAsync(stream, record, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporary, path, overwrite: true);
    }

    private static async Task<GameInstanceRecord?> ReadInstanceFileAsync(
        string rootPath,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(rootPath, ".archoera", "instance.json");
        if (!File.Exists(path)) return null;

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            using var document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var record = ParseRecord(document.RootElement);
            return record is null
                ? null
                : record with { RootPath = Path.GetFullPath(rootPath) };
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static GameInstanceRecord? ParseRecord(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;
        var id = String(item, "id");
        var name = String(item, "name");
        var game = String(item, "gameVersion");
        var root = String(item, "rootPath");
        if (id is null || name is null || game is null || root is null) return null;

        return new GameInstanceRecord(
            id,
            name,
            game,
            String(item, "loader") ?? "vanilla",
            String(item, "loaderVersion") ?? string.Empty,
            root,
            Int(item, "modCount") ?? 0,
            item.TryGetProperty("favorite", out var favorite) && favorite.ValueKind == JsonValueKind.True,
            Date(item, "createdAt") ?? DateTimeOffset.UtcNow,
            Date(item, "updatedAt") ?? DateTimeOffset.UtcNow,
            Date(item, "lastPlayedAt"));
    }

    private static async Task WriteRecordsAsync(
        Stream stream,
        IReadOnlyList<GameInstanceRecord> records,
        CancellationToken cancellationToken)
    {
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", GameInstanceRecord.CurrentSchemaVersion);
        writer.WriteStartArray("instances");
        foreach (var record in records)
        {
            WriteRecord(writer, record);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteRecordAsync(
        Stream stream,
        GameInstanceRecord record,
        CancellationToken cancellationToken)
    {
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        WriteRecord(writer, record);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void WriteRecord(Utf8JsonWriter writer, GameInstanceRecord record)
    {
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", GameInstanceRecord.CurrentSchemaVersion);
        writer.WriteString("id", record.Id);
        writer.WriteString("name", record.Name);
        writer.WriteString("gameVersion", record.GameVersion);
        writer.WriteString("loader", record.Loader);
        writer.WriteString("loaderVersion", record.LoaderVersion);
        writer.WriteString("rootPath", record.RootPath);
        writer.WriteNumber("modCount", record.ModCount);
        writer.WriteBoolean("favorite", record.Favorite);
        writer.WriteString("createdAt", record.CreatedAt.ToString("O"));
        writer.WriteString("updatedAt", record.UpdatedAt.ToString("O"));
        writer.WriteString("lastPlayedAt", record.LastPlayedAt?.ToString("O"));
        writer.WriteEndObject();
    }

    private static string? String(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static int? Int(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) &&
           element.ValueKind == JsonValueKind.Number &&
           element.TryGetInt32(out var value)
            ? value
            : null;

    private static DateTimeOffset? Date(JsonElement root, string name)
        => DateTimeOffset.TryParse(String(root, name), out var value) ? value : null;
}
