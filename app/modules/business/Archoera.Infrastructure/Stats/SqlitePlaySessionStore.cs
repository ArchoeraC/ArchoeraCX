using Archoera.Application.Stats;
using Archoera.Infrastructure.Settings;
using Archoera.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace Archoera.Infrastructure.Stats;

/// <summary>
/// SQLite-backed play-session log. Rows are written when the JVM starts and
/// completed when the process exits; the launcher itself never opens the
/// database. A session whose exit was not observed keeps <c>ended_at</c> null
/// and contributes zero playtime, so a crash cannot invent hours of play.
/// </summary>
public sealed class SqlitePlaySessionStore : IPlaySessionStore
{
    private static readonly SemaphoreSlim InitGate = new(1, 1);

    private readonly string _dataRoot;
    private readonly string _databasePath;
    private volatile bool _initialized;

    public SqlitePlaySessionStore(string? dataRoot = null)
    {
        var settings = new JsonAppSettingsStore(dataRoot);
        _dataRoot = settings.DataRoot;
        _databasePath = Path.Combine(_dataRoot, "archoera.db");
    }

    public string DatabasePath => _databasePath;

    public async Task BeginAsync(
        PlaySessionStart session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var local = session.StartedAt.ToLocalTime();
        var startedDay = DateOnly
            .FromDateTime(local.DateTime)
            .ToString("yyyy-MM-dd");

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO play_sessions
              (id, instance_id, instance_name, loader_kind, game_version,
               started_at, started_day, started_hour)
            VALUES
              ($id, $instanceId, $instanceName, $loader, $gameVersion,
               $startedAt, $startedDay, $startedHour)
            """;
        command.Parameters.AddWithValue("$id", session.Id);
        command.Parameters.AddWithValue("$instanceId", session.InstanceId);
        command.Parameters.AddWithValue("$instanceName", session.InstanceName);
        command.Parameters.AddWithValue("$loader", session.Loader);
        command.Parameters.AddWithValue("$gameVersion", session.GameVersion);
        command.Parameters.AddWithValue(
            "$startedAt",
            session.StartedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$startedDay", startedDay);
        command.Parameters.AddWithValue("$startedHour", local.Hour);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CompleteAsync(
        string sessionId,
        DateTimeOffset endedAt,
        int exitCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE play_sessions
            SET ended_at = $endedAt,
                duration_ms = MAX(0, $endedAt - started_at),
                exit_code = $exitCode
            WHERE id = $id AND ended_at IS NULL
            """;
        command.Parameters.AddWithValue(
            "$endedAt",
            endedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$exitCode", exitCode);
        command.Parameters.AddWithValue("$id", sessionId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AbortAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM play_sessions WHERE id = $id AND ended_at IS NULL";
        command.Parameters.AddWithValue("$id", sessionId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PlaySessionRow>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var rows = new List<PlaySessionRow>();
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, instance_id, instance_name, loader_kind, game_version,
                   started_at, ended_at, duration_ms, started_day, started_hour,
                   exit_code
            FROM play_sessions
            ORDER BY started_at ASC
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new PlaySessionRow(
                Id: reader.GetString(0),
                InstanceId: reader.GetString(1),
                InstanceName: reader.GetString(2),
                Loader: reader.GetString(3),
                GameVersion: reader.GetString(4),
                StartedAt: DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(5)),
                EndedAt: reader.IsDBNull(6)
                    ? null
                    : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(6)),
                DurationMs: reader.IsDBNull(7) ? 0 : reader.GetInt64(7),
                StartedDay: reader.GetString(8),
                StartedHour: reader.GetInt32(9),
                ExitCode: reader.IsDBNull(10) ? null : reader.GetInt32(10)));
        }

        return rows;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        await InitGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            Directory.CreateDirectory(_dataRoot);
            SqliteNativeLibrary.EnsureProvider();

            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var schema = connection.CreateCommand();
            schema.CommandText =
                "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;\n" +
                PlaySessionSchema.CreateSql;
            await schema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            InitGate.Release();
        }
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
}
