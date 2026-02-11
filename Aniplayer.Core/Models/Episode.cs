namespace Aniplayer.Core.Models;

public class Episode
{
    public int Id { get; set; }
    public int SeriesId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string? Title { get; set; }
    public double? EpisodeNumber { get; set; }
    public string EpisodeType { get; set; } = "EPISODE";
    public int? DurationSeconds { get; set; }
    public string? ThumbnailPath { get; set; }
    public int? AnilistEpId { get; set; }
    public string CreatedAt { get; set; } = string.Empty;

    public string DisplayName =>
        EpisodeNumber.HasValue
            ? $"Episode {EpisodeNumber.Value:0.##}"
            : System.IO.Path.GetFileNameWithoutExtension(FilePath);
}
