using System.Text.RegularExpressions;
using AnitomySharp;

namespace Aniplayer.Core.Helpers;

public static class EpisodeParser
{
    /// <summary>
    /// Parses anime filename metadata using AnitomySharp.
    /// Returns all parsed elements for a given filename.
    /// </summary>
    public static List<Element> ParseAll(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrEmpty(fileName))
            return new List<Element>();

        try
        {
            return AnitomySharp.AnitomySharp.Parse(fileName);
        }
        catch
        {
            return new List<Element>();
        }
    }

    public static double? ParseEpisodeNumber(string filePath)
    {
        var elements = ParseAll(filePath);
        var epElement = elements.FirstOrDefault(
            e => e.Category == Element.ElementCategory.ElementEpisodeNumber);

        if (epElement != null && double.TryParse(epElement.Value, out var num))
            return num;

        // Fallback: regex patterns for edge cases AnitomySharp might miss
        return ParseEpisodeNumberFallback(filePath);
    }

    public static string? ParseTitle(string filePath)
    {
        var elements = ParseAll(filePath);
        var titleElement = elements.FirstOrDefault(
            e => e.Category == Element.ElementCategory.ElementAnimeTitle);

        if (titleElement != null && !string.IsNullOrWhiteSpace(titleElement.Value))
            return titleElement.Value;

        // Fallback: regex-based title extraction
        return ParseTitleFallback(filePath);
    }

    public static string? ParseGroup(string filePath)
    {
        var elements = ParseAll(filePath);
        var groupElement = elements.FirstOrDefault(
            e => e.Category == Element.ElementCategory.ElementReleaseGroup);
        return groupElement?.Value;
    }

    public static string? ParseResolution(string filePath)
    {
        var elements = ParseAll(filePath);
        var resElement = elements.FirstOrDefault(
            e => e.Category == Element.ElementCategory.ElementVideoResolution);
        return resElement?.Value;
    }

    // ── Fallback regex parsers ──────────────────────────────

    private static readonly Regex[] EpisodePatterns =
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
        // Bare number at end: "Title 01.mkv"
        new(@"[\s._](\d{1,4}(?:\.\d)?)(?:\s*v\d)?(?:\s*[\[(\.]|$)", RegexOptions.Compiled),
    };

    private static double? ParseEpisodeNumberFallback(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrEmpty(fileName))
            return null;

        foreach (var pattern in EpisodePatterns)
        {
            var match = pattern.Match(fileName);
            if (match.Success && double.TryParse(match.Groups[1].Value, out var num))
                return num;
        }

        return null;
    }

    private static string? ParseTitleFallback(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrEmpty(fileName))
            return null;

        // Strip leading [group] tags
        var cleaned = Regex.Replace(fileName, @"^\[.*?\]\s*", "");

        // Take everything before the first separator or episode pattern
        var titleMatch = Regex.Match(cleaned, @"^(.+?)(?:\s*[-–—]\s*\d|\s*[Ss]\d|\s*[Ee][Pp]|\s*[Ee]pisode)");
        if (titleMatch.Success)
            return titleMatch.Groups[1].Value.Trim();

        // Fallback: strip common tags and return what's left
        cleaned = Regex.Replace(cleaned, @"\[.*?\]|\(.*?\)", "").Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }
}
