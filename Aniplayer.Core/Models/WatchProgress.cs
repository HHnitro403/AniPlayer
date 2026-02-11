namespace Aniplayer.Core.Models;

public class WatchProgress
{
    public int Id { get; set; }
    public int EpisodeId { get; set; }
    public int PositionSeconds { get; set; }
    public int? DurationSeconds { get; set; }
    public bool IsCompleted { get; set; }
    public string? LastWatchedAt { get; set; }

    public double ProgressPercent =>
        DurationSeconds is > 0
            ? Math.Clamp((double)PositionSeconds / DurationSeconds.Value, 0.0, 1.0)
            : 0.0;
}
