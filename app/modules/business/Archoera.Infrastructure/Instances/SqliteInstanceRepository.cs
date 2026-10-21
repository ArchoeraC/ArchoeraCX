using System.Text.Json;
using Archoera.Application.Instances;
using Archoera.Domain.Instances;
using Archoera.Infrastructure.Settings;
using Microsoft.Data.Sqlite;

namespace Archoera.Infrastructure.Instances;

/// <summary>
/// SQLite index owned by C#. Flutter never opens this database; the C++ hub
/// only routes <c>instance.*</c> methods. The per-instance
/// <c>.archoera/instance.json</c> remains authoritative for user-visible data.
/// </summary>
public sealed class SqliteInstanceRepository : IInstanceRepository
{
    private static readonly string[] Loaders =
    [
        "vanilla", "fabric", "quilt", "forge", "neoforge",
    ];

    private static readonly SemaphoreSlim InitGate = new(1, 1);
    private static bool _providerInitialized;

    private readonly string _dataRoot;
    private readonly string _databasePath;
    private readonly JsonAppSettingsStore _settings;
    private volatile bool _initialized;

    public SqliteInstanceRepository(
        string? dataRoot = null,
        JsonAppSettingsStore? settings = null)
    {
        _settings = settings ?? new JsonAppSettingsStore(dataRoot);
        _dataRoot = _settings.DataRoot;
        _databasePath = Path.Combine(_dataRoot, "archoera.db");
    }

    public string DatabasePath => _databasePath;

    public async Task<IReadOnlyList<GameInstanceRecord>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var records = new List<GameInstanceRecord>();

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, game_version, loader_kind, loader_version, root_path,
                   mod_count, pinned, created_at, updated_at, last_played_at
            FROM instances
            ORDER BY COALESCE(last_played_at, updated_at) DESC, name COLLATE NOCASE
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var record = ReadRecord(reader);
            var fromFile = await ReadInstanceFileAsync(record.RootPath, cancellationToken)
                .ConfigureAwait(false);
            records.Add(fromFile ?? record);
        }

        return records;
    }

    public async Task<GameInstanceRecord> CreateAsync(
        string name,
        string gameVersion,
        string loader,
        string loaderVersion,
        string? rootPath,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var cleanName = ValidateName(name);
        var cleanGame = ValidateSegment(gameVersion, "gameVersion");
        var cleanLoader = ValidateLoader(loader);
        var cleanLoaderVersion = string.IsNullOrWhiteSpace(loaderVersion)
            ? string.Empty
            : ValidateSegment(loaderVersion, "loaderVersion");
        var root = await ResolveRootAsync(cleanName, rootPath, cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(rootPath) &&
            await RootExistsAsync(root, cancellationToken).ConfigureAwait(false))
        {
            throw new ArgumentException("rootPath is already used by another instance");
        }

        var now = DateTimeOffset.UtcNow;
        var record = new GameInstanceRecord(
            CreateId(now),
            cleanName,
            cleanGame,
            cleanLoader,
            cleanLoaderVersion,
            root,
            0,
            false,
            now,
            now,
            null);

        Directory.CreateDirectory(root);
        await WriteInstanceFileAsync(record, cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO instances
              (id, name, game_version, loader_kind, loader_version, root_path,
               mod_count, pinned, created_at, updated_at, last_played_at)
            VALUES
              ($id, $name, $game, $loader, $loaderVersion, $root,
               $mods, $pinned, $created, $updated, $played)
            """;
        AddParameters(command, record);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<GameInstanceRecord?> UpdateAsync(
        string id,
        string? name,
        bool? favorite,
        DateTimeOffset? lastPlayedAt,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var cleanId = ValidateId(id);
        var current = await FindAsync(cleanId, cancellationToken).ConfigureAwait(false);
        if (current is null) return null;

        var updated = current with
        {
            Name = name is null ? current.Name : ValidateName(name),
            Favorite = favorite ?? current.Favorite,
            LastPlayedAt = lastPlayedAt ?? current.LastPlayedAt,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE instances
            SET name = $name, pinned = $pinned, updated_at = $updated,
                last_played_at = $played
            WHERE id = $id
            """;
        command.Parameters.AddWithValue("$id", updated.Id);
        command.Parameters.AddWithValue("$name", updated.Name);
        command.Parameters.AddWithValue("$pinned", updated.Favorite ? 1 : 0);
        command.Parameters.AddWithValue("$updated", updated.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue(
            "$played",
            updated.LastPlayedAt?.ToString("O") ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await WriteInstanceFileAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<bool> DeleteAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var cleanId = ValidateId(id);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM instances WHERE id = $id";
        command.Parameters.AddWithValue("$id", cleanId);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task<InstanceSyncReport> SyncAsync(
        IReadOnlyList<string>? roots,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var configured = await ResolveConfiguredRootsAsync(roots, cancellationToken)
            .ConfigureAwait(false);
        var existing = await ListAsync(cancellationToken).ConfigureAwait(false);
        var byId = existing.ToDictionary(record => record.Id, StringComparer.Ordinal);

        var found = new Dictionary<string, GameInstanceRecord>(StringComparer.Ordinal);
        var rootCounts = new Dictionary<string, int>(PathComparer);
        foreach (var root in configured)
        {
            var exists = Directory.Exists(root);
            var count = 0;
            if (exists)
            {
                foreach (var record in InstanceDiscovery.Discover(root, cancellationToken))
                {
                    if (found.TryAdd(record.Id, record))
                    {
                        count++;
                    }
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
                if (!dryRun)
                {
                    await UpsertRecordAsync(record, cancellationToken).ConfigureAwait(false);
                }
                adopted.Add(record.Id);
                continue;
            }

            if (PathsEqual(current.RootPath, record.RootPath))
            {
                if (!SameMetadata(current, record))
                {
                    if (!dryRun)
                    {
                        await UpsertRecordAsync(record, cancellationToken).ConfigureAwait(false);
                    }
                    updated.Add(record.Id);
                }
                continue;
            }

            // A moved folder is safe to re-point only when the old directory is gone.
            if (Directory.Exists(current.RootPath)) continue;
            if (!dryRun)
            {
                await UpsertRecordAsync(record, cancellationToken).ConfigureAwait(false);
            }
            updated.Add(record.Id);
        }

        var missing = existing
            .Where(record => !found.ContainsKey(record.Id) && !Directory.Exists(record.RootPath))
            .Select(record => new InstanceSyncMissing(record.Id, record.RootPath))
            .ToArray();

        IReadOnlyList<GameInstanceRecord> items;
        if (dryRun)
        {
            var merged = new List<GameInstanceRecord>(existing);
            foreach (var record in found.Values)
            {
                var index = merged.FindIndex(item => item.Id == record.Id);
                if (index >= 0) merged[index] = record;
                else merged.Add(record);
            }
            items = merged;
        }
        else
        {
            items = await ListAsync(cancellationToken).ConfigureAwait(false);
        }

        return new InstanceSyncReport(
            items,
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

    private async Task<IReadOnlyList<string>> ResolveConfiguredRootsAsync(
        IReadOnlyList<string>? roots,
        CancellationToken cancellationToken)
    {
        if (roots is { Count: > 0 })
        {
            return roots
                .Where(root => !string.IsNullOrWhiteSpace(root) && Path.IsPathFullyQualified(root))
                .Select(AppDataPaths.Normalize)
                .Distinct(PathComparer)
                .ToArray();
        }

        var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        return settings.Roots;
    }

    private async Task UpsertRecordAsync(
        GameInstanceRecord record,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO instances
              (id, name, game_version, loader_kind, loader_version, root_path,
               mod_count, pinned, created_at, updated_at, last_played_at)
            VALUES
              ($id, $name, $game, $loader, $loaderVersion, $root,
               $mods, $pinned, $created, $updated, $played)
            ON CONFLICT(id) DO UPDATE SET
              name = excluded.name,
              game_version = excluded.game_version,
              loader_kind = excluded.loader_kind,
              loader_version = excluded.loader_version,
              root_path = excluded.root_path,
              mod_count = excluded.mod_count,
              pinned = excluded.pinned,
              updated_at = excluded.updated_at,
              last_played_at = excluded.last_played_at
            """;
        AddParameters(command, record);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        // `.archoera/instance.json` is authoritative for known ArchoeraCX
        // instances. Never create it for adopted external launcher folders.
        if (Directory.Exists(record.RootPath) &&
            File.Exists(Path.Combine(record.RootPath, ".archoera", "instance.json")))
        {
            await WriteInstanceFileAsync(record, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool SameMetadata(GameInstanceRecord left, GameInstanceRecord right)
        => string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
           string.Equals(left.GameVersion, right.GameVersion, StringComparison.Ordinal) &&
           string.Equals(left.Loader, right.Loader, StringComparison.Ordinal) &&
           string.Equals(left.LoaderVersion, right.LoaderVersion, StringComparison.Ordinal) &&
           left.ModCount == right.ModCount &&
           left.Favorite == right.Favorite;

    private static bool PathsEqual(string left, string right)
        => PathComparer.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)));

    private static IEnumerable<string> SafeGetDirectories(string root)
    {
        try
        {
            return Directory.Exists(root)
                ? Directory.GetDirectories(root)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                : [];
        }
        catch
        {
            return [];
        }
    }

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        await InitGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            Directory.CreateDirectory(_dataRoot);
            EnsureSqliteProvider();

            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
                await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var schema = connection.CreateCommand())
            {
                schema.CommandText = """
                    CREATE TABLE IF NOT EXISTS schema_migrations (
                      version INTEGER PRIMARY KEY,
                      applied_at TEXT NOT NULL
                    );
                    CREATE TABLE IF NOT EXISTS instances (
                      id TEXT PRIMARY KEY,
                      name TEXT NOT NULL,
                      game_version TEXT NOT NULL,
                      loader_kind TEXT NOT NULL DEFAULT 'vanilla',
                      loader_version TEXT,
                      root_path TEXT NOT NULL,
                      mod_count INTEGER NOT NULL DEFAULT 0,
                      pinned INTEGER NOT NULL DEFAULT 0,
                      created_at TEXT NOT NULL,
                      updated_at TEXT NOT NULL,
                      last_played_at TEXT
                    );
                    INSERT OR IGNORE INTO schema_migrations(version, applied_at)
                    VALUES (1, strftime('%Y-%m-%dT%H:%M:%fZ','now'));
                    """;
                await schema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // One-time import from the previous JSON registry. Keep instance.json
            // authoritative; this only preserves already-created instances.
            await using (var countCommand = connection.CreateCommand())
            {
                countCommand.CommandText = "SELECT COUNT(*) FROM instances";
                var count = Convert.ToInt64(
                    await countCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
                if (count == 0)
                {
                    var json = new JsonInstanceRepository(_dataRoot, _settings);
                    foreach (var old in await json.ListAsync(cancellationToken).ConfigureAwait(false))
                    {
                        await using var insert = connection.CreateCommand();
                        insert.CommandText = """
                            INSERT OR IGNORE INTO instances
                              (id, name, game_version, loader_kind, loader_version, root_path,
                               mod_count, pinned, created_at, updated_at, last_played_at)
                            VALUES
                              ($id, $name, $game, $loader, $loaderVersion, $root,
                               $mods, $pinned, $created, $updated, $played)
                            """;
                        AddParameters(insert, old);
                        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            _initialized = true;
        }
        finally
        {
            InitGate.Release();
        }
    }

    private static void EnsureSqliteProvider()
    {
        if (_providerInitialized) return;

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            // Use the system SQLite already loaded by the OS/Flutter runtime
            // instead of shipping a second e_sqlite3 copy with different global
            // symbols. Windows has no system SQLite; the bundled provider is
            // used there.
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
        }
        else
        {
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_e_sqlite3());
        }

        _providerInitialized = true;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        };
        var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private async Task<GameInstanceRecord?> FindAsync(
        string id,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, game_version, loader_kind, loader_version, root_path,
                   mod_count, pinned, created_at, updated_at, last_played_at
            FROM instances WHERE id = $id
            """;
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadRecord(reader)
            : null;
    }

    private static GameInstanceRecord ReadRecord(SqliteDataReader reader)
    {
        var lastPlayed = reader.IsDBNull(10) ? null : ParseDate(reader.GetString(10));
        return new GameInstanceRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
            reader.GetString(5),
            reader.GetInt32(6),
            reader.GetInt32(7) != 0,
            ParseDate(reader.GetString(8)) ?? DateTimeOffset.UtcNow,
            ParseDate(reader.GetString(9)) ?? DateTimeOffset.UtcNow,
            lastPlayed);
    }

    private static void AddParameters(SqliteCommand command, GameInstanceRecord record)
    {
        command.Parameters.AddWithValue("$id", record.Id);
        command.Parameters.AddWithValue("$name", record.Name);
        command.Parameters.AddWithValue("$game", record.GameVersion);
        command.Parameters.AddWithValue("$loader", record.Loader);
        command.Parameters.AddWithValue("$loaderVersion", record.LoaderVersion);
        command.Parameters.AddWithValue("$root", record.RootPath);
        command.Parameters.AddWithValue("$mods", record.ModCount);
        command.Parameters.AddWithValue("$pinned", record.Favorite ? 1 : 0);
        command.Parameters.AddWithValue("$created", record.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updated", record.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue(
            "$played",
            record.LastPlayedAt?.ToString("O") ?? (object)DBNull.Value);
    }

    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

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
        var candidate = Path.Combine(settings.Roots[0], slug);
        var suffix = 2;
        while (Directory.Exists(candidate) || await RootExistsAsync(candidate, cancellationToken)
                   .ConfigureAwait(false))
        {
            candidate = Path.Combine(settings.Roots[0], $"{slug}-{suffix++}");
        }

        return candidate;
    }

    private async Task<bool> RootExistsAsync(string root, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM instances WHERE root_path = $root LIMIT 1";
        command.Parameters.AddWithValue("$root", root);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
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

    private static string ValidateLoader(string loader)
    {
        var clean = loader?.Trim().ToLowerInvariant() ?? "vanilla";
        if (!Loaders.Contains(clean, StringComparer.Ordinal))
        {
            throw new ArgumentException($"unsupported loader '{loader}'");
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

    private static async Task WriteInstanceFileAsync(
        GameInstanceRecord record,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(record.RootPath, ".archoera");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "instance.json");
        var temporary = path + ".tmp";
        await using (var stream = new FileStream(
                         temporary,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 4096,
                         useAsync: true))
        {
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
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
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporary, path, overwrite: true);
    }

    private static async Task<GameInstanceRecord?> ReadInstanceFileAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, ".archoera", "instance.json");
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
            var rootElement = document.RootElement;
            if (rootElement.ValueKind != JsonValueKind.Object) return null;
            var id = String(rootElement, "id");
            var name = String(rootElement, "name");
            var game = String(rootElement, "gameVersion");
            if (id is null || name is null || game is null) return null;
            var actualRoot = AppDataPaths.Normalize(root);
            return new GameInstanceRecord(
                id,
                name,
                game,
                String(rootElement, "loader") ?? "vanilla",
                String(rootElement, "loaderVersion") ?? string.Empty,
                actualRoot,
                Int(rootElement, "modCount") ?? 0,
                rootElement.TryGetProperty("favorite", out var favorite) &&
                favorite.ValueKind == JsonValueKind.True,
                ParseDate(String(rootElement, "createdAt")) ?? DateTimeOffset.UtcNow,
                ParseDate(String(rootElement, "updatedAt")) ?? DateTimeOffset.UtcNow,
                ParseDate(String(rootElement, "lastPlayedAt")));
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
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
}
