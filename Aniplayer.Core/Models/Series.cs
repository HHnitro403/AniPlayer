namespace Aniplayer.Core.Models;

public class Series
{
    public int Id { get; set; }
    public int LibraryId { get; set; }
    public string FolderName { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int? AnilistId { get; set; }
    public string? TitleRomaji { get; set; }
    public string? TitleEnglish { get; set; }
    public string? TitleNative { get; set; }
    public string? CoverImagePath { get; set; }
    public string? Synopsis { get; set; }
    public string? Genres { get; set; }
    public double? AverageScore { get; set; }
    public int? TotalEpisodes { get; set; }
    public string? Status { get; set; }
    public string? MetadataFetchedAt { get; set; }
    public string CreatedAt { get; set; } = string.Empty;

    // Navigation — populated by service when needed, not by Dapper auto-map
    public List<Episode>? Episodes { get; set; }

    public string DisplayTitle =>
        TitleEnglish ?? TitleRomaji ?? FolderName;
}
