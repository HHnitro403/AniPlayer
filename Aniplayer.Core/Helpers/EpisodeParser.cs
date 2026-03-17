using System.Text.RegularExpressions;
using AnitomySharp;

namespace Aniplayer.Core.Helpers;

public static class EpisodeParser
{
    // Set to true to enable verbose parse logging (piped through ScanProgress)
    public static Action<string>? LogCallback;

    private static void Log(string msg) => LogCallback?.Invoke(msg);

    // Cache parsed results to avoid re-parsing the same file multiple times per scan
    private static readonly Dictionary<string, List<Element>> _parseCache = new();

    public static void ClearCache() => _parseCache.Clear();

    /// <summary>
    /// Parses anime filename metadata using AnitomySharp.
    /// Returns all parsed elements for a given filename. Results are cached.
    /// </summary>
    public static List<Element> ParseAll(string filePath)
    {
        if (_parseCache.TryGetValue(filePath, out var cached))
            return cached;

        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrEmpty(fileName))
        {
            Log($"[Parser] ParseAll: empty filename for path '{filePath}'");
            return new List<Element>();
        }

        try
        {
            var results = (List<Element>)AnitomySharp.AnitomySharp.Parse(fileName);
            Log($"[Parser] AnitomySharp parsed '{fileName}' → {results.Count} elements:");
            foreach (var el in results)
                Log($"[Parser]   {el.Category} = '{el.Value}'");
            _parseCache[filePath] = results;
            return results;
        }
        catch (Exception ex)
        {
            Log($"[Parser] AnitomySharp EXCEPTION for '{fileName}': {ex.Message}");
            return new List<Element>();
        }
    }

    public static double? ParseEpisodeNumber(string filePath)
    {
        var elements = ParseAll(filePath);
        var epElement = elements.FirstOrDefault(
            e => e.Category == Element.ElementCategory.ElementEpisodeNumber);

        if (epElement != null && double.TryParse(epElement.Value, out var num))
        {
            Log($"[Parser] EpisodeNumber: '{epElement.Value}' → {num}");
            return num;
        }

        Log($"[Parser] AnitomySharp found no episode number, trying fallback regex...");
        var fallback = ParseEpisodeNumberFallback(filePath);
        Log($"[Parser] Fallback episode number: {fallback?.ToString() ?? "null"}");
        return fallback;
    }

    public static string? ParseTitle(string filePath)
    {
        var elements = ParseAll(filePath);
        var titleElement = elements.FirstOrDefault(
            e => e.Category == Element.ElementCategory.ElementAnimeTitle);

        if (titleElement != null && !string.IsNullOrWhiteSpace(titleElement.Value))
        {
            Log($"[Parser] Title: '{titleElement.Value}'");
            return titleElement.Value;
        }

        Log($"[Parser] AnitomySharp found no title, trying fallback regex...");
        var fallback = ParseTitleFallback(filePath);
        Log($"[Parser] Fallback title: '{fallback ?? "null"}'");
        return fallback;
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

    public static int? ParseSeasonNumber(string filePath)
    {
        var elements = ParseAll(filePath);
        var seasonElement = elements.FirstOrDefault(
            e => e.Category == Element.ElementCategory.ElementAnimeSeason);

        if (seasonElement != null && int.TryParse(seasonElement.Value, out var num))
        {
            Log($"[Parser] Season: '{seasonElement.Value}' → {num}");
            return num;
        }

        // Fallback: detect from folder name "S2", "Season 2"
        var match = Regex.Match(filePath, @"[/\\]S(?:eason\s*)?(\d+)", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var season))
        {
            Log($"[Parser] Fallback season from path: {season}");
            return season;
        }

        return null;
    }

    public static string? ParseEpisodeTitle(string filePath)
    {
        var elements = ParseAll(filePath);
        var titleElement = elements.FirstOrDefault(
            e => e.Category == Element.ElementCategory.ElementEpisodeTitle);

        if (titleElement != null && !string.IsNullOrWhiteSpace(titleElement.Value))
        {
            Log($"[Parser] EpisodeTitle: '{titleElement.Value}'");
            return titleElement.Value;
        }

        return null;
    }

    public static bool TryParseSeasonFromFolder(string folderName, out int seasonNumber)
    {
        seasonNumber = 1;

        // 1. Detect Specials / OVAs
        if (folderName.Equals("Specials", StringComparison.OrdinalIgnoreCase) ||
            folderName.Equals("OVA", StringComparison.OrdinalIgnoreCase) ||
            folderName.Equals("OVAs", StringComparison.OrdinalIgnoreCase))
        {
            seasonNumber = 0; // 0 denotes Specials
            return true;
        }

        // 2. Detect Season Patterns (e.g., "Season 1", "S02", "Book 3")
        var patterns = new[]
        {
            @"^Season\s*(\d+)",
            @"^S(\d+)$",
            @"^Book\s*(\d+)",
            @"^(\d+)(?:st|nd|rd|th)?\s*Season"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(folderName, pattern, RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
            {
                seasonNumber = num;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Strips release group tags, quality tags, season suffixes, website URLs, 
    /// and other common "garbage" from series folder names.
    /// Used for presentation and as a search query for metadata fetching.
    /// </summary>
    public static string CleanSeriesTitle(string rawFolderName)
    {
        if (string.IsNullOrWhiteSpace(rawFolderName))
            return rawFolderName;

        // 1. Strip ALL leading tags: [Group], 【Group】, (Group) — handles multiple consecutive tags like [SubsPlease][720p]
        var cleaned = Regex.Replace(rawFolderName, @"^(?:(?:\[.*?\]|【.*?】|\(.*?\))\s*)+", "");

        // 2. Strip trailing [tag] blocks (quality, hash, checksum, codec, etc.)
        cleaned = Regex.Replace(cleaned, @"(\s*\[.*?\])+\s*$", "");

        // 3. Strip trailing metadata/quality parenthetical suffixes:
        // (Seasons 1-4 + OVAs + Specials), (Season 1), (Complete), (Dub), (2024), (BD 1080p), (720p), etc.
        cleaned = Regex.Replace(cleaned,
            @"\s*\((?:Seasons?\s|S\d|Part|Cour|Complete|Batch|Dual\s?Audio|Multi\s?Subs?|Dub|\d{4}|BD|BluRay|BDRip|WEB-?DL|WEBRip|\d{3,4}p|HEVC|AVC|Hi10P|10bit|8bit|x26[45]|FLAC|AAC).*\)\s*$",
            "", RegexOptions.IgnoreCase);

        // 4. Strip trailing Season/Cour/Part suffixes without parentheses:
        // "Show Season 2", "Show S2", "Show Part 1"
        cleaned = Regex.Replace(cleaned, @"\s+(?:Season|Part|Cour|S|v)\s*\d+$", "", RegexOptions.IgnoreCase);

        // 5. Strip trailing quality tags without brackets:
        // "Show 1080p", "Show BD", "Show BluRay"
        cleaned = Regex.Replace(cleaned, @"\s+(?:\d{3,4}p|BD|BluRay|BDRip|WEB-?DL|WEBRip|x26[45]|HEVC|AVC)\s*$", "", RegexOptions.IgnoreCase);

        // 6. Strip common website URLs or hashes
        cleaned = Regex.Replace(cleaned, @"\s+[\w-]+\.(?:com|net|org|io|me|tv|cc)\s*$", "", RegexOptions.IgnoreCase);

        // 7. Strip trailing separator + number patterns (e.g. "- 01")
        cleaned = Regex.Replace(cleaned, @"\s*[-–—]\s*(?:S\d+E\d+|\d+)\s*$", "", RegexOptions.IgnoreCase);

        return cleaned.Trim();
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
