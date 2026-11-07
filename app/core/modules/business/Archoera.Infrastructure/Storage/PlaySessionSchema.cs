namespace Archoera.Infrastructure.Storage;

/// <summary>
/// DDL for the play-session table. Both the instance repository (which owns
/// the base schema) and the play-session store execute this so a database
/// created before statistics existed gains the table on the next open.
/// </summary>
internal static class PlaySessionSchema
{
    internal const string CreateSql =
        """
        CREATE TABLE IF NOT EXISTS play_sessions (
          id TEXT PRIMARY KEY,
          instance_id TEXT NOT NULL,
          instance_name TEXT NOT NULL,
          loader_kind TEXT NOT NULL,
          game_version TEXT NOT NULL,
          started_at INTEGER NOT NULL,
          ended_at INTEGER,
          duration_ms INTEGER,
          started_day TEXT NOT NULL,
          started_hour INTEGER NOT NULL,
          exit_code INTEGER
        );
        CREATE INDEX IF NOT EXISTS idx_play_sessions_started_at
          ON play_sessions(started_at);
        CREATE INDEX IF NOT EXISTS idx_play_sessions_instance
          ON play_sessions(instance_id, started_at);
        """;
}
