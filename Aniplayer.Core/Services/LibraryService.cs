using Aniplayer.Core.Database;
using Aniplayer.Core.Interfaces;
using Aniplayer.Core.Models;
using Dapper;

namespace Aniplayer.Core.Services;

public class LibraryService : ILibraryService
{
    private readonly IDatabaseService _db;

    public LibraryService(IDatabaseService db)
    {
        _db = db;
    }

    // ── Libraries ────────────────────────────────────────────

    public async Task<IEnumerable<Library>> GetAllLibrariesAsync()
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryAsync<Library>(Queries.GetAllLibraries);
    }

    public async Task<Library?> GetLibraryByIdAsync(int id)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Library>(
            Queries.GetLibraryById, new { id });
    }

    public async Task<Library?> GetLibraryByPathAsync(string path)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Library>(
            Queries.GetLibraryByPath, new { path });
    }

    public async Task<int> AddLibraryAsync(string path, string? label = null)
    {
        using var conn = _db.CreateConnection();
        return await conn.QuerySingleAsync<int>(
            Queries.InsertLibrary, new { Path = path, Label = label });
    }

    public async Task DeleteLibraryAsync(int id)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(Queries.DeleteLibrary, new { id });
    }

    // ── Series ───────────────────────────────────────────────

    public async Task<IEnumerable<Series>> GetAllSeriesAsync()
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryAsync<Series>(Queries.GetAllSeries);
    }

    public async Task<IEnumerable<Series>> GetSeriesByLibraryIdAsync(int libraryId)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryAsync<Series>(
            Queries.GetSeriesByLibraryId, new { libraryId });
    }

    public async Task<Series?> GetSeriesByIdAsync(int id)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Series>(
            Queries.GetSeriesById, new { id });
    }

    public async Task<Series?> GetSeriesByPathAsync(string path)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Series>(
            Queries.GetSeriesByPath, new { path });
    }

    public async Task<IEnumerable<Series>> GetSeriesByGroupNameAsync(string seriesGroupName)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryAsync<Series>(
            Queries.GetSeriesByGroupName, new { seriesGroupName });
    }

    public async Task<IEnumerable<Series>> GetRecentlyAddedSeriesAsync(int days)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryAsync<Series>(
            Queries.GetRecentlyAddedSeries, new { daysOffset = $"-{days} days" });
    }

    public async Task<int> UpsertSeriesAsync(int libraryId, string folderName, string path, string seriesGroupName, int seasonNumber)
    {
        using var conn = _db.CreateConnection();
        return await conn.QuerySingleAsync<int>(
            Queries.InsertSeries,
            new
            {
                LibraryId = libraryId,
                FolderName = folderName,
                Path = path,
                SeriesGroupName = seriesGroupName,
                SeasonNumber = seasonNumber
            });
    }

    public async Task UpdateSeriesMetadataAsync(Series series)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(Queries.UpdateSeriesMetadata, series);
    }

    public async Task DeleteSeriesAsync(int id)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(Queries.DeleteSeries, new { id });
    }

    // ── Episodes ─────────────────────────────────────────────

    public async Task<IEnumerable<Episode>> GetEpisodesBySeriesIdAsync(int seriesId)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryAsync<Episode>(
            Queries.GetEpisodesBySeriesId, new { seriesId });
    }

    public async Task<Episode?> GetEpisodeByIdAsync(int id)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Episode>(
            Queries.GetEpisodeById, new { id });
    }

    public async Task<Episode?> GetEpisodeByFilePathAsync(string filePath)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Episode>(
            Queries.GetEpisodeByFilePath, new { filePath });
    }

    public async Task<int> UpsertEpisodeAsync(int seriesId, string filePath,
        string? title, double? episodeNumber, string episodeType)
    {
        using var conn = _db.CreateConnection();
        return await conn.QuerySingleAsync<int>(Queries.InsertEpisode, new
        {
            SeriesId = seriesId,
            FilePath = filePath,
            Title = title,
            EpisodeNumber = episodeNumber,
            EpisodeType = episodeType
        });
    }

    public async Task DeleteEpisodeAsync(int id)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(Queries.DeleteEpisode, new { id });
    }

    public async Task<IEnumerable<string>> GetEpisodeFilePathsBySeriesIdAsync(int seriesId)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryAsync<string>(
            Queries.GetEpisodeFilePathsBySeriesId, new { seriesId });
    }

    // ── Track Preferences ─────────────────────────────────────

    public async Task<TrackPreferences?> GetSeriesTrackPreferenceAsync(int seriesId)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<TrackPreferences>(
            Queries.GetTrackPreferencesBySeriesId, new { seriesId });
    }

    public async Task UpsertSeriesAudioPreferenceAsync(int seriesId, string audioLanguage, string? audioTitle, int? audioTrackId = null)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            Queries.UpsertSeriesTrackPreference,
            new { seriesId, audioLang = audioLanguage, audioTitle, audioTrackId, subLang = (string?)null, subName = (string?)null });
    }

    public async Task UpsertSeriesSubtitlePreferenceAsync(int seriesId, string subtitleLanguage, string? subtitleName)
    {
        using var conn = _db.CreateConnection();
        // Preserve existing audio preferences while updating subtitle preferences
        var existing = await GetSeriesTrackPreferenceAsync(seriesId);
        await conn.ExecuteAsync(
            Queries.UpsertSeriesTrackPreference,
            new {
                seriesId,
                audioLang = existing?.PreferredAudioLanguage,
                audioTitle = existing?.PreferredAudioTitle,
                audioTrackId = existing?.PreferredAudioTrackId,
                subLang = subtitleLanguage,
                subName = subtitleName
            });
    }
}
