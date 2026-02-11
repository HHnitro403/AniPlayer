using System.Text.RegularExpressions;

namespace Aniplayer.Core.Helpers;

public static class EpisodeParser
{
    // Ordered by specificity — first match wins
    private static readonly Regex[] Patterns =
    {
        // [Group] Title - 01 [tags].mkv
        new(@"[-–—]\s*(\d{1,4}(?:\.\d)?)\s*(?:\[|\(|v\d|$)", RegexOptions.Compiled),

        // S01E01 or S1E01
        new(@"[Ss]\d{1,2}[Ee](\d{1,4}(?:\.\d)?)", RegexOptions.Compiled),

        // EP01, Ep01, ep01
        new(@"[Ee][Pp]\.?\s*(\d{1,4}(?:\.\d)?)", RegexOptions.Compiled),

        // Episode 01
        new(@"[Ee]pisode\s*(\d{1,4}(?:\.\d)?)", RegexOptions.Compiled),

        // E01 (standalone, not part of a word)
        new(@"(?<![A-Za-z])[Ee](\d{2,4}(?:\.\d)?)(?![A-Za-z])", RegexOptions.Compiled),

        // Bare number at end of filename: "Title 01.mkv" or "Title - 01v2.mkv"
        new(@"[\s._](\d{1,4}(?:\.\d)?)(?:\s*v\d)?(?:\s*[\[(\.]|$)", RegexOptions.Compiled),
    };

    public static double? ParseEpisodeNumber(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrEmpty(fileName))
            return null;

        foreach (var pattern in Patterns)
        {
            var match = pattern.Match(fileName);
            if (match.Success && double.TryParse(match.Groups[1].Value, out var num))
                return num;
        }

        return null;
    }

    public static string? ParseTitle(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrEmpty(fileName))
            return null;

        // Strip leading [group] tags
        var cleaned = Regex.Replace(fileName, @"^\[.*?\]\s*", "");

        // Take everything before the first " - " separator or episode pattern
        var titleMatch = Regex.Match(cleaned, @"^(.+?)(?:\s*[-–—]\s*\d|\s*[Ss]\d|\s*[Ee][Pp]|\s*[Ee]pisode)");
        if (titleMatch.Success)
            return titleMatch.Groups[1].Value.Trim();

        // Fallback: strip common tags and return what's left
        cleaned = Regex.Replace(cleaned, @"\[.*?\]|\(.*?\)", "").Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }
}
