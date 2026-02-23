using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<int, long> _lastSaveTimes = new();
    private readonly ConcurrentDictionary<int, bool> _completedEpisodesCache = new();
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
            if (_lastSaveTimes.TryGetValue(episodeId, out var lastSave) && now - lastSave < SaveIntervalTicks)
            {
                return;
            }
        }

        // If user rewinds and starts watching again, clear completion cache
        // This allows episode to be re-marked as complete if they watch it again
        if (_completedEpisodesCache.ContainsKey(episodeId))
        {
            _completedEpisodesCache.TryRemove(episodeId, out _);
            _logger.LogDebug("Cleared completion cache for episode {EpisodeId} (user rewound)", episodeId);
        }

        _logger.LogDebug("Saving progress for episode {EpisodeId} at {Position}s", episodeId, positionSeconds);

        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(Queries.UpsertWatchProgress, new
        {
            episodeId,
            positionSeconds,
            durationSeconds
        });
        _lastSaveTimes[episodeId] = Stopwatch.GetTimestamp();

        // Also ensure the episode table has the duration if it was missing
        await conn.ExecuteAsync(Queries.UpdateEpisodeDuration, new { id = episodeId, duration = durationSeconds });
    }

    public async Task MarkCompletedAsync(int episodeId)
    {
        // Check cache first to prevent redundant DB writes
        if (_completedEpisodesCache.ContainsKey(episodeId))
        {
            _logger.LogDebug("Episode {EpisodeId} already marked complete (cached), skipping DB write", episodeId);
            return;
        }

        _logger.LogDebug("Marking episode {EpisodeId} as completed", episodeId);
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(Queries.MarkEpisodeCompleted, new { episodeId });

        // Add to cache and clear progress tracking
        _completedEpisodesCache[episodeId] = true;
        _lastSaveTimes.TryRemove(episodeId, out _);
    }

    public async Task<IEnumerable<(Episode Episode, WatchProgress Progress)>> GetRecentlyWatchedAsync(int limit)
    {
        using var conn = _db.CreateConnection();
        
        // Use splitOn: "Id" because both Episode and WatchProgress have an 'Id' column.
        // Dapper will split the row at the SECOND 'Id' column found in the SELECT.
        var results = await conn.QueryAsync<Episode, WatchProgress, (Episode, WatchProgress)>(
            Queries.GetRecentlyWatched,
            (episode, progress) => (episode, progress),
            new { limit },
            splitOn: "Id");

        return results;
    }
}
