using System.Text.RegularExpressions;

namespace Aniplayer.Core.Models;

public class Series
{
    public int Id { get; set; }
    public int LibraryId { get; set; }
    public string FolderName { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string SeriesGroupName { get; set; } = string.Empty;
    public int SeasonNumber { get; set; }
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
        TitleEnglish ?? TitleRomaji ?? CleanFolderName(
            !string.IsNullOrEmpty(SeriesGroupName) ? SeriesGroupName : FolderName);

    /// <summary>
    /// Strips release group tags [Group], trailing bracket tags [quality][hash],
    /// and common metadata suffixes from folder names for display.
    /// "[Judas] High School DxD (Seasons 1-4 + OVAs + Specials)" → "High School DxD"
    /// "[Anime Time] Kenja no Mago (Wise Man's Grandchild) [Dual Audio]" → "Kenja no Mago (Wise Man's Grandchild)"
    /// </summary>
    internal static string CleanFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;

        // Strip leading [group] tags
        var cleaned = Regex.Replace(name, @"^\[.*?\]\s*", "");

        // Strip trailing [tag] blocks (quality, hash, codec, etc.)
        cleaned = Regex.Replace(cleaned, @"(\s*\[.*?\])+\s*$", "");

        // Strip common metadata parenthetical suffixes at end:
        // (Seasons 1-4 + OVAs + Specials), (Season 1), (S1-S4), (Complete), (Batch)
        cleaned = Regex.Replace(cleaned, @"\s*\((?:Seasons?\s|S\d|Complete|Batch|Dual\s?Audio|Multi\s?Subs?).*\)\s*$",
            "", RegexOptions.IgnoreCase);

        return cleaned.Trim();
    }
}
