using System.Diagnostics;
using Aniplayer.Core.Database;
using Aniplayer.Core.Interfaces;
using Aniplayer.Core.Models;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Aniplayer.Core.Services;

public class WatchProgressService : IWatchProgressService
{
    private readonly IDatabaseService _db;
    private long _lastSaveTimestamp = 0; // Use Stopwatch ticks
    private static readonly long SaveIntervalTicks = Stopwatch.Frequency * 5; // 5 seconds in ticks
    private readonly ILogger<WatchProgressService> _logger;

    public WatchProgressService(IDatabaseService db, ILogger<WatchProgressService> logger)
    {
        _db = db;
        _logger = logger;
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
        if (!forceSave)
        {
            var now = Stopwatch.GetTimestamp();
            if (now - _lastSaveTimestamp < SaveIntervalTicks)
            {
                // Optionally log debounce, but can be noisy.
                // _logger.LogTrace("Debounced progress save for episode {EpisodeId}", episodeId);
                return;
            }
        }

        _logger.LogDebug("Saving progress for episode {EpisodeId} at {Position}s", episodeId, positionSeconds);
        
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(Queries.UpsertWatchProgress, new
        {
            episodeId,
            positionSeconds,
            durationSeconds
        });
        _lastSaveTimestamp = Stopwatch.GetTimestamp(); // Update timestamp only after successful save
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
