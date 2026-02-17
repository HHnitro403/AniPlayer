namespace Aniplayer.Core.Database;

public static class Queries
{
    // ── Libraries ──────────────────────────────────────────────

    public const string GetAllLibraries =
        "SELECT id AS Id, path AS Path, label AS Label, created_at AS CreatedAt FROM Libraries ORDER BY created_at";

    public const string GetLibraryById =
        "SELECT id AS Id, path AS Path, label AS Label, created_at AS CreatedAt FROM Libraries WHERE id = @id";

    public const string GetLibraryByPath =
        "SELECT id AS Id, path AS Path, label AS Label, created_at AS CreatedAt FROM Libraries WHERE path = @path";

    public const string InsertLibrary =
        "INSERT INTO Libraries (path, label) VALUES (@Path, @Label) RETURNING id";

    public const string DeleteLibrary =
        "DELETE FROM Libraries WHERE id = @id";

    // ── Series ─────────────────────────────────────────────────

    public const string GetAllSeries = @"
        SELECT id AS Id, library_id AS LibraryId, folder_name AS FolderName, path AS Path,
               series_group_name AS SeriesGroupName, season_number AS SeasonNumber,
               anilist_id AS AnilistId, title_romaji AS TitleRomaji, title_english AS TitleEnglish,
               title_native AS TitleNative, cover_image_path AS CoverImagePath, synopsis AS Synopsis,
               genres AS Genres, average_score AS AverageScore, total_episodes AS TotalEpisodes,
               status AS Status, metadata_fetched_at AS MetadataFetchedAt, created_at AS CreatedAt
        FROM Series
        ORDER BY COALESCE(title_english, title_romaji, folder_name)";

    public const string GetSeriesById = @"
        SELECT id AS Id, library_id AS LibraryId, folder_name AS FolderName, path AS Path,
               series_group_name AS SeriesGroupName, season_number AS SeasonNumber,
               anilist_id AS AnilistId, title_romaji AS TitleRomaji, title_english AS TitleEnglish,
               title_native AS TitleNative, cover_image_path AS CoverImagePath, synopsis AS Synopsis,
               genres AS Genres, average_score AS AverageScore, total_episodes AS TotalEpisodes,
               status AS Status, metadata_fetched_at AS MetadataFetchedAt, created_at AS CreatedAt
        FROM Series WHERE id = @id";

    public const string GetSeriesByLibraryId = @"
        SELECT id AS Id, library_id AS LibraryId, folder_name AS FolderName, path AS Path,
               series_group_name AS SeriesGroupName, season_number AS SeasonNumber,
               anilist_id AS AnilistId, title_romaji AS TitleRomaji, title_english AS TitleEnglish,
               title_native AS TitleNative, cover_image_path AS CoverImagePath, synopsis AS Synopsis,
               genres AS Genres, average_score AS AverageScore, total_episodes AS TotalEpisodes,
               status AS Status, metadata_fetched_at AS MetadataFetchedAt, created_at AS CreatedAt
        FROM Series WHERE library_id = @libraryId
        ORDER BY COALESCE(title_english, title_romaji, folder_name)";

    public const string GetSeriesByPath = @"
        SELECT id AS Id, library_id AS LibraryId, folder_name AS FolderName, path AS Path,
               series_group_name AS SeriesGroupName, season_number AS SeasonNumber,
               anilist_id AS AnilistId, title_romaji AS TitleRomaji, title_english AS TitleEnglish,
               title_native AS TitleNative, cover_image_path AS CoverImagePath, synopsis AS Synopsis,
               genres AS Genres, average_score AS AverageScore, total_episodes AS TotalEpisodes,
               status AS Status, metadata_fetched_at AS MetadataFetchedAt, created_at AS CreatedAt
        FROM Series WHERE path = @path";

    public const string GetSeriesByGroupName = @"
        SELECT id AS Id, library_id AS LibraryId, folder_name AS FolderName, path AS Path,
               series_group_name AS SeriesGroupName, season_number AS SeasonNumber,
               anilist_id AS AnilistId, title_romaji AS TitleRomaji, title_english AS TitleEnglish,
               title_native AS TitleNative, cover_image_path AS CoverImagePath, synopsis AS Synopsis,
               genres AS Genres, average_score AS AverageScore, total_episodes AS TotalEpisodes,
               status AS Status, metadata_fetched_at AS MetadataFetchedAt, created_at AS CreatedAt
        FROM Series WHERE series_group_name = @seriesGroupName";

    public const string GetRecentlyAddedSeries = @"
        SELECT id AS Id, library_id AS LibraryId, folder_name AS FolderName, path AS Path,
               series_group_name AS SeriesGroupName, season_number AS SeasonNumber,
               anilist_id AS AnilistId, title_romaji AS TitleRomaji, title_english AS TitleEnglish,
               title_native AS TitleNative, cover_image_path AS CoverImagePath, synopsis AS Synopsis,
               genres AS Genres, average_score AS AverageScore, total_episodes AS TotalEpisodes,
               status AS Status, metadata_fetched_at AS MetadataFetchedAt, created_at AS CreatedAt
        FROM Series
        WHERE created_at >= datetime('now', @daysOffset)
        ORDER BY created_at DESC";

    public const string InsertSeries = @"
        INSERT INTO Series (library_id, folder_name, path, series_group_name, season_number)
        VALUES (@LibraryId, @FolderName, @Path, @SeriesGroupName, @SeasonNumber)
        ON CONFLICT(path) DO UPDATE SET
            folder_name = excluded.folder_name,
            series_group_name = excluded.series_group_name,
            season_number = excluded.season_number
        RETURNING id";

    public const string UpdateSeriesMetadata = @"
        UPDATE Series SET
            anilist_id          = @AnilistId,
            title_romaji        = @TitleRomaji,
            title_english       = @TitleEnglish,
            title_native        = @TitleNative,
            cover_image_path    = @CoverImagePath,
            synopsis            = @Synopsis,
            genres              = @Genres,
            average_score       = @AverageScore,
            total_episodes      = @TotalEpisodes,
            status              = @Status,
            metadata_fetched_at = datetime('now')
        WHERE id = @Id";

    public const string DeleteSeries =
        "DELETE FROM Series WHERE id = @id";

    // ── Episodes ───────────────────────────────────────────────

    public const string GetEpisodesBySeriesId = @"
        SELECT id AS Id, series_id AS SeriesId, file_path AS FilePath, title AS Title,
               episode_number AS EpisodeNumber, episode_type AS EpisodeType,
               duration_seconds AS DurationSeconds, thumbnail_path AS ThumbnailPath,
               anilist_ep_id AS AnilistEpId, created_at AS CreatedAt
        FROM Episodes WHERE series_id = @seriesId
        ORDER BY file_path, episode_number";

    public const string GetEpisodeById = @"
        SELECT id AS Id, series_id AS SeriesId, file_path AS FilePath, title AS Title,
               episode_number AS EpisodeNumber, episode_type AS EpisodeType,
               duration_seconds AS DurationSeconds, thumbnail_path AS ThumbnailPath,
               anilist_ep_id AS AnilistEpId, created_at AS CreatedAt
        FROM Episodes WHERE id = @id";

    public const string GetEpisodeByFilePath = @"
        SELECT id AS Id, series_id AS SeriesId, file_path AS FilePath, title AS Title,
               episode_number AS EpisodeNumber, episode_type AS EpisodeType,
               duration_seconds AS DurationSeconds, thumbnail_path AS ThumbnailPath,
               anilist_ep_id AS AnilistEpId, created_at AS CreatedAt
        FROM Episodes WHERE file_path = @filePath";

    public const string InsertEpisode = @"
        INSERT INTO Episodes (series_id, file_path, title, episode_number, episode_type)
        VALUES (@SeriesId, @FilePath, @Title, @EpisodeNumber, @EpisodeType)
        ON CONFLICT(file_path) DO UPDATE SET
            title          = excluded.title,
            episode_number = excluded.episode_number,
            episode_type   = excluded.episode_type
        RETURNING id";

    public const string UpdateEpisodeDuration =
        "UPDATE Episodes SET duration_seconds = @duration WHERE id = @id";

    public const string DeleteEpisode =
        "DELETE FROM Episodes WHERE id = @id";

    public const string DeleteEpisodesBySeriesId =
        "DELETE FROM Episodes WHERE series_id = @seriesId";

    public const string GetEpisodeFilePathsBySeriesId =
        "SELECT file_path FROM Episodes WHERE series_id = @seriesId";

    // ── Watch Progress ─────────────────────────────────────────

    public const string GetProgressByEpisodeId = @"
        SELECT id AS Id, episode_id AS EpisodeId, position_seconds AS PositionSeconds,
               duration_seconds AS DurationSeconds, is_completed AS IsCompleted,
               last_watched_at AS LastWatchedAt
        FROM WatchProgress WHERE episode_id = @episodeId";

    public const string GetProgressForSeries = @"
        SELECT wp.id AS Id, wp.episode_id AS EpisodeId, wp.position_seconds AS PositionSeconds,
               wp.duration_seconds AS DurationSeconds, wp.is_completed AS IsCompleted,
               wp.last_watched_at AS LastWatchedAt
        FROM WatchProgress wp
        INNER JOIN Episodes e ON e.id = wp.episode_id
        WHERE e.series_id = @seriesId";

    public const string GetRecentlyWatched = @"
        SELECT e.id AS Id, e.series_id AS SeriesId, e.file_path AS FilePath, e.title AS Title,
               e.episode_number AS EpisodeNumber, e.episode_type AS EpisodeType,
               e.duration_seconds AS DurationSeconds, e.thumbnail_path AS ThumbnailPath,
               e.anilist_ep_id AS AnilistEpId, e.created_at AS CreatedAt,
               wp.id AS WpId, wp.episode_id AS EpisodeId, wp.position_seconds AS PositionSeconds,
               wp.duration_seconds AS DurationSeconds, wp.is_completed AS IsCompleted,
               wp.last_watched_at AS LastWatchedAt
        FROM WatchProgress wp
        INNER JOIN Episodes e ON e.id = wp.episode_id
        WHERE wp.position_seconds > 0 AND wp.is_completed = 0
        ORDER BY wp.last_watched_at DESC
        LIMIT @limit";

    public const string UpsertWatchProgress = @"
        INSERT INTO WatchProgress (episode_id, position_seconds, duration_seconds, last_watched_at)
        VALUES (@episodeId, @positionSeconds, @durationSeconds, datetime('now'))
        ON CONFLICT(episode_id) DO UPDATE SET
            position_seconds = excluded.position_seconds,
            duration_seconds = excluded.duration_seconds,
            last_watched_at  = excluded.last_watched_at";

    public const string MarkEpisodeCompleted = @"
        INSERT INTO WatchProgress (episode_id, is_completed, last_watched_at)
        VALUES (@episodeId, 1, datetime('now'))
        ON CONFLICT(episode_id) DO UPDATE SET
            is_completed    = 1,
            last_watched_at = excluded.last_watched_at";

    // ── Track Preferences ──────────────────────────────────────

    public const string GetTrackPreferencesByEpisodeId = @"
        SELECT id AS Id, episode_id AS EpisodeId, series_id AS SeriesId,
               preferred_audio_language AS PreferredAudioLanguage,
               preferred_audio_title AS PreferredAudioTitle,
               preferred_audio_track_id AS PreferredAudioTrackId,
               preferred_subtitle_language AS PreferredSubtitleLanguage,
               preferred_subtitle_name AS PreferredSubtitleName
        FROM TrackPreferences WHERE episode_id = @episodeId";

    public const string GetTrackPreferencesBySeriesId = @"
        SELECT id AS Id, episode_id AS EpisodeId, series_id AS SeriesId,
               preferred_audio_language AS PreferredAudioLanguage,
               preferred_audio_title AS PreferredAudioTitle,
               preferred_audio_track_id AS PreferredAudioTrackId,
               preferred_subtitle_language AS PreferredSubtitleLanguage,
               preferred_subtitle_name AS PreferredSubtitleName
        FROM TrackPreferences WHERE series_id = @seriesId AND episode_id IS NULL";

    public const string UpsertEpisodeTrackPreference = @"
        INSERT INTO TrackPreferences (episode_id, preferred_audio_language, preferred_audio_title, preferred_subtitle_language, preferred_subtitle_name)
        VALUES (@episodeId, @audioLang, @audioTitle, @subLang, @subName)
        ON CONFLICT(episode_id) WHERE episode_id IS NOT NULL DO UPDATE SET
            preferred_audio_language    = excluded.preferred_audio_language,
            preferred_audio_title       = excluded.preferred_audio_title,
            preferred_subtitle_language = excluded.preferred_subtitle_language,
            preferred_subtitle_name     = excluded.preferred_subtitle_name";

    public const string UpsertSeriesTrackPreference = @"
        INSERT INTO TrackPreferences (series_id, preferred_audio_language, preferred_audio_title, preferred_audio_track_id, preferred_subtitle_language, preferred_subtitle_name)
        VALUES (@seriesId, @audioLang, @audioTitle, @audioTrackId, @subLang, @subName)
        ON CONFLICT(series_id) WHERE series_id IS NOT NULL AND episode_id IS NULL DO UPDATE SET
            preferred_audio_language    = excluded.preferred_audio_language,
            preferred_audio_title       = excluded.preferred_audio_title,
            preferred_audio_track_id   = excluded.preferred_audio_track_id,
            preferred_subtitle_language = excluded.preferred_subtitle_language,
            preferred_subtitle_name     = excluded.preferred_subtitle_name";

    // ── Settings ───────────────────────────────────────────────

    public const string GetSetting =
        "SELECT value FROM Settings WHERE key = @key";

    public const string UpsertSetting = @"
        INSERT INTO Settings (key, value) VALUES (@key, @value)
        ON CONFLICT(key) DO UPDATE SET value = excluded.value";

    public const string DeleteSetting =
        "DELETE FROM Settings WHERE key = @key";

    public const string GetAllSettings =
        "SELECT key, value FROM Settings";
}
