using System.Text.RegularExpressions;

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
    public string? ExternalSubtitlePath { get; set; }
    public string CreatedAt { get; set; } = string.Empty;

    // Populated by chapter detection
    public double IntroStart { get; set; } = -1;
    public double IntroEnd { get; set; } = -1;
    public double OutroStart { get; set; } = -1;
    public double OutroEnd { get; set; } = -1;
    public bool HasIntro => IntroStart >= 0 && IntroEnd > IntroStart;
    public bool HasOutro => OutroStart >= 0;

    /// <summary>
    /// Display name shown in the episode list. Includes subfolder context for
    /// multi-season shows, e.g. "S1 Episode 3" or "S2 - OVA Episode 1".
    /// </summary>
    public string DisplayName
    {
        get
        {
            var prefix = GetSubfolderPrefix();
            if (EpisodeNumber.HasValue)
            {
                var epStr = $"Episode {EpisodeNumber.Value:0.##}";
                return string.IsNullOrEmpty(prefix) ? epStr : $"{prefix} {epStr}";
            }
            return System.IO.Path.GetFileNameWithoutExtension(FilePath);
        }
    }

    /// <summary>
    /// Extracts a short season/subfolder prefix from the file path.
    /// "High School DxD S2/ep01.mkv" → "S2"
    /// "High School DxD S1 - OVA/ova01.mkv" → "S1 - OVA"
    /// </summary>
    private string? GetSubfolderPrefix()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(FilePath);
            if (string.IsNullOrEmpty(dir))
                return null;

            var folderName = System.IO.Path.GetFileName(dir);
            if (string.IsNullOrEmpty(folderName))
                return null;

            // "High School DxD S1 - OVA" → "S1 - OVA"
            var match = Regex.Match(folderName, @"(S\d+\s*[-–—]\s*\w+)\s*$", RegexOptions.IgnoreCase);
            if (match.Success)
                return match.Groups[1].Value.Trim();

            // "High School DxD S2" or "Season 2" → "S2"
            match = Regex.Match(folderName, @"(S(?:eason\s*)?\d+)\s*$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var val = match.Groups[1].Value.Trim();
                return Regex.Replace(val, @"Season\s*", "S", RegexOptions.IgnoreCase);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
