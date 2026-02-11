namespace Aniplayer.Core.Models;

public class TrackPreferences
{
    public int Id { get; set; }
    public int? EpisodeId { get; set; }
    public int? SeriesId { get; set; }
    public string? PreferredAudioLanguage { get; set; }
    public string? PreferredSubtitleLanguage { get; set; }
    public string? PreferredSubtitleName { get; set; }
}
