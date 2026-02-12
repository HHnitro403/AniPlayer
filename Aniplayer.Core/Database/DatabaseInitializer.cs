using Aniplayer.Core.Constants;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Aniplayer.Core.Database;

public class DatabaseInitializer
{
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(ILogger<DatabaseInitializer> logger)
    {
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing database at {Path}", AppConstants.DbPath);

        // Ensure all app directories exist
        Directory.CreateDirectory(AppConstants.AppDataPath);
        Directory.CreateDirectory(AppConstants.CoversPath);
        Directory.CreateDirectory(AppConstants.ThumbnailsPath);
        Directory.CreateDirectory(AppConstants.LogsPath);

        var connectionString = $"Data Source={AppConstants.DbPath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        // Pragmas — order matters, execute before any DDL
        await connection.ExecuteAsync("PRAGMA journal_mode = WAL;").ConfigureAwait(false);
        await connection.ExecuteAsync("PRAGMA foreign_keys = ON;").ConfigureAwait(false);
        await connection.ExecuteAsync("PRAGMA synchronous = NORMAL;").ConfigureAwait(false);
        _logger.LogDebug("Database pragmas set (WAL, FK, synchronous=NORMAL)");

        // Tables
        await connection.ExecuteAsync(Schema.CreateLibraries).ConfigureAwait(false);
        await connection.ExecuteAsync(Schema.CreateSeries).ConfigureAwait(false);
        await connection.ExecuteAsync(Schema.CreateEpisodes).ConfigureAwait(false);
        await connection.ExecuteAsync(Schema.CreateWatchProgress).ConfigureAwait(false);
        await connection.ExecuteAsync(Schema.CreateTrackPreferences).ConfigureAwait(false);
        await connection.ExecuteAsync(Schema.CreateSettings).ConfigureAwait(false);

        // Indexes
        await connection.ExecuteAsync(Schema.CreateIndexes).ConfigureAwait(false);

        _logger.LogInformation("Database initialized successfully");
    }

    private static class Schema
    {
        public const string CreateLibraries = @"
            CREATE TABLE IF NOT EXISTS Libraries (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                path        TEXT NOT NULL UNIQUE,
                label       TEXT,
                created_at  TEXT NOT NULL DEFAULT (datetime('now'))
            );";

        public const string CreateSeries = @"
            CREATE TABLE IF NOT EXISTS Series (
                id                   INTEGER PRIMARY KEY AUTOINCREMENT,
                library_id           INTEGER NOT NULL REFERENCES Libraries(id) ON DELETE CASCADE,
                folder_name          TEXT NOT NULL,
                path                 TEXT NOT NULL UNIQUE,
                anilist_id           INTEGER,
                title_romaji         TEXT,
                title_english        TEXT,
                title_native         TEXT,
                cover_image_path     TEXT,
                synopsis             TEXT,
                genres               TEXT,
                average_score        REAL,
                total_episodes       INTEGER,
                status               TEXT,
                metadata_fetched_at  TEXT,
                created_at           TEXT NOT NULL DEFAULT (datetime('now'))
            );";

        public const string CreateEpisodes = @"
            CREATE TABLE IF NOT EXISTS Episodes (
                id               INTEGER PRIMARY KEY AUTOINCREMENT,
                series_id        INTEGER NOT NULL REFERENCES Series(id) ON DELETE CASCADE,
                file_path        TEXT NOT NULL UNIQUE,
                title            TEXT,
                episode_number   REAL,
                episode_type     TEXT NOT NULL DEFAULT 'EPISODE',
                duration_seconds INTEGER,
                thumbnail_path   TEXT,
                anilist_ep_id    INTEGER,
                created_at       TEXT NOT NULL DEFAULT (datetime('now'))
            );";

        public const string CreateWatchProgress = @"
            CREATE TABLE IF NOT EXISTS WatchProgress (
                id                INTEGER PRIMARY KEY AUTOINCREMENT,
                episode_id        INTEGER NOT NULL UNIQUE REFERENCES Episodes(id) ON DELETE CASCADE,
                position_seconds  INTEGER NOT NULL DEFAULT 0,
                duration_seconds  INTEGER,
                is_completed      INTEGER NOT NULL DEFAULT 0,
                last_watched_at   TEXT
            );";

        public const string CreateTrackPreferences = @"
            CREATE TABLE IF NOT EXISTS TrackPreferences (
                id                          INTEGER PRIMARY KEY AUTOINCREMENT,
                episode_id                  INTEGER REFERENCES Episodes(id) ON DELETE CASCADE,
                series_id                   INTEGER REFERENCES Series(id) ON DELETE CASCADE,
                preferred_audio_language    TEXT,
                preferred_subtitle_language TEXT,
                preferred_subtitle_name     TEXT,
                CHECK (
                    (episode_id IS NOT NULL AND series_id IS NULL) OR
                    (episode_id IS NULL     AND series_id IS NOT NULL)
                )
            );";

        public const string CreateSettings = @"
            CREATE TABLE IF NOT EXISTS Settings (
                key    TEXT PRIMARY KEY,
                value  TEXT
            );";

        public const string CreateIndexes = @"
            CREATE INDEX IF NOT EXISTS idx_episodes_series_id      ON Episodes(series_id);
            CREATE INDEX IF NOT EXISTS idx_watch_progress_ep_id    ON WatchProgress(episode_id);
            CREATE INDEX IF NOT EXISTS idx_series_library_id       ON Series(library_id);
            CREATE INDEX IF NOT EXISTS idx_track_prefs_episode_id  ON TrackPreferences(episode_id);
            CREATE INDEX IF NOT EXISTS idx_track_prefs_series_id   ON TrackPreferences(series_id);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_track_prefs_series_unique  ON TrackPreferences(series_id) WHERE episode_id IS NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS idx_track_prefs_episode_unique ON TrackPreferences(episode_id) WHERE series_id IS NULL;";
    }
}
