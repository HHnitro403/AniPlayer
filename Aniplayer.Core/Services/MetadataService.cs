using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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
          Media(search: $search, type: ANIME) {
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

        var metadata = await SearchAsync(series.FolderName, ct);
        if (metadata == null)
        {
            _logger.LogInformation("No AniList match for '{FolderName}'", series.FolderName);
            return;
        }

        series.AnilistId = metadata.AnilistId;
        series.TitleRomaji = metadata.TitleRomaji;
        series.TitleEnglish = metadata.TitleEnglish;
        series.TitleNative = metadata.TitleNative;
        series.Synopsis = metadata.Synopsis;
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
