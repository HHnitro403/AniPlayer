using Aniplayer.Core.Models;

namespace Aniplayer.Core.Interfaces;

public interface IMetadataService
{
    Task<AniListMetadata?> SearchAsync(string title, CancellationToken ct = default);
    Task ApplyMetadataToSeriesAsync(int seriesId, CancellationToken ct = default);
    Task<string?> DownloadCoverAsync(string imageUrl, int seriesId, CancellationToken ct = default);
}
