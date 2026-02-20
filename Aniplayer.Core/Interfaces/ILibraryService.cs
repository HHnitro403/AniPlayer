using Aniplayer.Core.Models;

namespace Aniplayer.Core.Interfaces;

public interface ILibraryService
{
    // Libraries
    Task<IEnumerable<Library>> GetAllLibrariesAsync();
    Task<Library?> GetLibraryByIdAsync(int id);
    Task<Library?> GetLibraryByPathAsync(string path);
    Task<int> AddLibraryAsync(string path, string? label = null);
    Task DeleteLibraryAsync(int id);

    // Series
    Task<IEnumerable<Series>> GetAllSeriesAsync();
    Task<IEnumerable<Series>> GetSeriesByLibraryIdAsync(int libraryId);
    Task<Series?> GetSeriesByIdAsync(int id);
    Task<Series?> GetSeriesByPathAsync(string path);
    Task<IEnumerable<Series>> GetSeriesByGroupNameAsync(string seriesGroupName);
    Task<IEnumerable<Series>> GetRecentlyAddedSeriesAsync(int days);
    Task<int> UpsertSeriesAsync(int libraryId, string folderName, string path, string seriesGroupName, int seasonNumber);
    Task UpdateSeriesMetadataAsync(Series series);
    Task DeleteSeriesAsync(int id);

    // Episodes
    Task<IEnumerable<Episode>> GetEpisodesBySeriesIdAsync(int seriesId);
    Task<Episode?> GetEpisodeByIdAsync(int id);
    Task<Episode?> GetEpisodeByFilePathAsync(string filePath);
    Task<int> UpsertEpisodeAsync(int seriesId, string filePath, string? title,
        double? episodeNumber, string episodeType);
    Task DeleteEpisodeAsync(int id);
    Task<IEnumerable<string>> GetEpisodeFilePathsBySeriesIdAsync(int seriesId);

    // Track Preferences
    Task<TrackPreferences?> GetSeriesTrackPreferenceAsync(int seriesId);
    Task UpsertSeriesAudioPreferenceAsync(int seriesId, string audioLanguage, string? audioTitle, int? audioTrackId = null);
    Task UpsertSeriesSubtitlePreferenceAsync(int seriesId, string subtitleLanguage, string? subtitleName);

    // External Subtitle Override
    Task SetEpisodeExternalSubtitleAsync(int episodeId, string? subtitlePath);
}
