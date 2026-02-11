namespace Aniplayer.Core.Models;

public class AniListMetadata
{
    public int AnilistId { get; set; }
    public string? TitleRomaji { get; set; }
    public string? TitleEnglish { get; set; }
    public string? TitleNative { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? Synopsis { get; set; }
    public List<string>? Genres { get; set; }
    public double? AverageScore { get; set; }
    public int? TotalEpisodes { get; set; }
    public string? Status { get; set; }
    public int? StartYear { get; set; }

    public string? GenresJson =>
        Genres is { Count: > 0 }
            ? System.Text.Json.JsonSerializer.Serialize(Genres)
            : null;
}
