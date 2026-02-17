using Aniplayer.Core.Database;
using Aniplayer.Core.Interfaces;
using Aniplayer.Core.Models;
using Dapper;

namespace Aniplayer.Core.Services;

public class WatchProgressService : IWatchProgressService
{
    private readonly IDatabaseService _db;
    private DateTime _lastSaveTime = DateTime.MinValue;
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(5);

    public WatchProgressService(IDatabaseService db)
    {
        _db = db;
    }

    public async Task<WatchProgress?> GetProgressByEpisodeIdAsync(int episodeId)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<WatchProgress>(
            Queries.GetProgressByEpisodeId, new { episodeId });
    }

    public async Task<IEnumerable<WatchProgress>> GetProgressForSeriesAsync(int seriesId)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryAsync<WatchProgress>(
            Queries.GetProgressForSeries, new { seriesId });
    }

    public async Task UpdateProgressAsync(int episodeId, int positionSeconds, int durationSeconds, bool forceSave = false)
    {
        if (!forceSave && DateTime.UtcNow - _lastSaveTime < SaveInterval)
        {
            return;
        }

        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(Queries.UpsertWatchProgress, new
        {
            episodeId,
            positionSeconds,
            durationSeconds
        });
        _lastSaveTime = DateTime.UtcNow;
    }

    public async Task MarkCompletedAsync(int episodeId)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(Queries.MarkEpisodeCompleted, new { episodeId });
    }

    public async Task<IEnumerable<(Episode Episode, WatchProgress Progress)>> GetRecentlyWatchedAsync(int limit)
    {
        using var conn = _db.CreateConnection();
        var results = await conn.QueryAsync<Episode, WatchProgress, (Episode, WatchProgress)>(
            Queries.GetRecentlyWatched,
            (episode, progress) => (episode, progress),
            new { limit },
            splitOn: "WpId");
        return results;
    }
}
