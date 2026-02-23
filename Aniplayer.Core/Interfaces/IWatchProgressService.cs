using Aniplayer.Core.Models;

namespace Aniplayer.Core.Interfaces;

public interface IWatchProgressService
{
    Task<WatchProgress?> GetProgressByEpisodeIdAsync(int episodeId);
    Task<IEnumerable<WatchProgress>> GetProgressForSeriesAsync(int seriesId);
    Task UpdateProgressAsync(int episodeId, int positionSeconds, int durationSeconds, bool forceSave = false);
    Task MarkCompletedAsync(int episodeId);
    Task<IEnumerable<(Episode Episode, WatchProgress Progress)>> GetRecentlyWatchedAsync(int limit);
}
