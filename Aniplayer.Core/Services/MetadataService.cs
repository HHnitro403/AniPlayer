using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Aniplayer.Core.Constants;
using Aniplayer.Core.Interfaces;
using Aniplayer.Core.Models;
using Microsoft.Extensions.Logging;

namespace Aniplayer.Core.Services;

public class MetadataService : IMetadataService
{
    private readonly ILibraryService _library;
    private readonly ILogger<MetadataService> _logger;
    private readonly HttpClient _http;

    private const string SearchQuery = @"
        query ($search: String) {
          Media(search: $search, type: ANIME, isAdult: false, format_in: [TV, TV_SHORT, OVA, ONA, MOVIE, SPECIAL]) {
            id
            title { romaji english native }
            coverImage { large }
            description(asHtml: false)
            genres
            averageScore
            episodes
            status
            startDate { year }
          }
        }";

    public MetadataService(ILibraryService library, ILogger<MetadataService> logger)
    {
        _library = library;
        _logger = logger;
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(AppConstants.AniListTimeoutSeconds)
        };
    }

    public async Task<AniListMetadata?> SearchAsync(string title, CancellationToken ct = default)
    {
        try
        {
            var payload = new { query = SearchQuery, variables = new { search = title } };
            var response = await _http.PostAsJsonAsync(AppConstants.AniListEndpoint, payload, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AniList returned {Status} for query '{Title}'",
                    response.StatusCode, title);
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync<AniListResponse>(
                AniListJsonOptions, ct);

            var media = json?.Data?.Media;
            if (media == null) return null;

            return new AniListMetadata
            {
                AnilistId = media.Id,
                TitleRomaji = media.Title?.Romaji,
                TitleEnglish = media.Title?.English,
                TitleNative = media.Title?.Native,
                CoverImageUrl = media.CoverImage?.Large,
                Synopsis = media.Description,
                Genres = media.Genres,
                AverageScore = media.AverageScore,
                TotalEpisodes = media.Episodes,
                Status = media.Status,
                StartYear = media.StartDate?.Year
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogError(ex, "AniList search failed for '{Title}'", title);
            return null;
        }
    }

    public async Task ApplyMetadataToSeriesAsync(int seriesId, CancellationToken ct = default)
    {
        var series = await _library.GetSeriesByIdAsync(seriesId);
        if (series == null) return;

        var searchTitle = CleanTitleForSearch(series.DisplayTitle);
        var metadata = await SearchAsync(searchTitle, ct);
        if (metadata == null)
        {
            _logger.LogInformation("No AniList match for '{SearchTitle}' (folder: '{FolderName}')",
                searchTitle, series.FolderName);
            return;
        }

        series.AnilistId = metadata.AnilistId;
        series.TitleRomaji = metadata.TitleRomaji;
        series.TitleEnglish = metadata.TitleEnglish;
        series.TitleNative = metadata.TitleNative;
        series.Synopsis = SanitizeSynopsis(metadata.Synopsis);
        series.Genres = metadata.GenresJson;
        series.AverageScore = metadata.AverageScore;
        series.TotalEpisodes = metadata.TotalEpisodes;
        series.Status = metadata.Status;

        // Download cover image if available
        if (!string.IsNullOrEmpty(metadata.CoverImageUrl))
        {
            var localPath = await DownloadCoverAsync(metadata.CoverImageUrl, seriesId, ct);
            if (localPath != null)
                series.CoverImagePath = localPath;
        }

        await _library.UpdateSeriesMetadataAsync(series);
        _logger.LogInformation("Applied AniList metadata to series {Id} ({Title})",
            seriesId, series.DisplayTitle);
    }

    public async Task<string?> DownloadCoverAsync(string imageUrl, int seriesId,
        CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(AppConstants.CoversPath);
            var ext = Path.GetExtension(new Uri(imageUrl).AbsolutePath);
            if (string.IsNullOrEmpty(ext)) ext = ".jpg";
            var localPath = Path.Combine(AppConstants.CoversPath, $"{seriesId}{ext}");

            var bytes = await _http.GetByteArrayAsync(imageUrl, ct);
            await File.WriteAllBytesAsync(localPath, bytes, ct);

            _logger.LogInformation("Downloaded cover for series {Id} to {Path}", seriesId, localPath);
            return localPath;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            _logger.LogError(ex, "Failed to download cover from {Url}", imageUrl);
            return null;
        }
    }

    public async Task FetchAllMissingMetadataAsync(CancellationToken ct = default)
    {
        var allSeries = await _library.GetAllSeriesAsync();
        var missing = allSeries.Where(s => s.MetadataFetchedAt == null).ToList();

        _logger.LogInformation("Fetching metadata for {Count} series", missing.Count);

        foreach (var series in missing)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await ApplyMetadataToSeriesAsync(series.Id, ct);
                await Task.Delay(1000, ct); // Rate limit: 1 request/second
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Metadata fetch failed for {Title}", series.FolderName);
            }
        }

        _logger.LogInformation("Batch metadata fetch complete");
    }

    private static string? SanitizeSynopsis(string? rawSynopsis)
    {
        if (string.IsNullOrWhiteSpace(rawSynopsis))
            return null;

        var sanitized = rawSynopsis;

        // Replace <br> with newlines
        sanitized = Regex.Replace(sanitized, @"<br\s*/?>", Environment.NewLine, RegexOptions.IgnoreCase);

        // Strip all other HTML tags
        sanitized = Regex.Replace(sanitized, @"<[^>]+>", string.Empty);

        // Strip AniList's markdown-like tags (e.g. [i], [/i])
        sanitized = Regex.Replace(sanitized, @"\[/?\w+\]", string.Empty);

        // Decode HTML entities (&quot;, &amp;, etc.)
        sanitized = WebUtility.HtmlDecode(sanitized);

        return sanitized.Trim();
    }

    private static string CleanTitleForSearch(string title)
    {
        // Strip leading [Group] tags (e.g. "[SubGroup] Title")
        var cleaned = Regex.Replace(title, @"^\[.*?\]\s*", "");

        // Strip common suffixes that break AniList matching
        cleaned = Regex.Replace(cleaned, @"\s+(?:Season|Part|Cour|S)\s*\d+$", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\s+\(.*?\)\s*$", ""); // "(Dub)", "(2024)"

        // Strip trailing quality/resolution tags (e.g. "1080p", "BD", "BluRay")
        cleaned = Regex.Replace(cleaned, @"\s+(?:\d{3,4}p|BD|BluRay|BDRip|WEB-?DL|WEBRip)\s*$", "", RegexOptions.IgnoreCase);

        // Strip trailing separator + number patterns (e.g. "- 01", "- S01E01")
        cleaned = Regex.Replace(cleaned, @"\s*[-–—]\s*(?:S\d+E\d+|\d+)\s*$", "", RegexOptions.IgnoreCase);

        return cleaned.Trim();
    }

    // ── AniList JSON response mapping ────────────────────────────

    private static readonly JsonSerializerOptions AniListJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private class AniListResponse
    {
        [JsonPropertyName("data")]
        public AniListData? Data { get; set; }
    }

    private class AniListData
    {
        [JsonPropertyName("Media")]
        public AniListMedia? Media { get; set; }
    }

    private class AniListMedia
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public AniListTitle? Title { get; set; }

        [JsonPropertyName("coverImage")]
        public AniListCoverImage? CoverImage { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("genres")]
        public List<string>? Genres { get; set; }

        [JsonPropertyName("averageScore")]
        public double? AverageScore { get; set; }

        [JsonPropertyName("episodes")]
        public int? Episodes { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("startDate")]
        public AniListDate? StartDate { get; set; }
    }

    private class AniListTitle
    {
        [JsonPropertyName("romaji")]
        public string? Romaji { get; set; }

        [JsonPropertyName("english")]
        public string? English { get; set; }

        [JsonPropertyName("native")]
        public string? Native { get; set; }
    }

    private class AniListCoverImage
    {
        [JsonPropertyName("large")]
        public string? Large { get; set; }
    }

    private class AniListDate
    {
        [JsonPropertyName("year")]
        public int? Year { get; set; }
    }
}
