using Aniplayer.Core.Constants;
using Aniplayer.Core.Helpers;
using Aniplayer.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Aniplayer.Core.Services;

public class ScannerService : IScannerService
{
    private readonly ILibraryService _library;
    private readonly ILogger<ScannerService> _logger;

    public event Action<string>? ScanProgress;

    public ScannerService(ILibraryService library, ILogger<ScannerService> logger)
    {
        _library = library;
        _logger = logger;
    }

    public async Task ScanAllLibrariesAsync(CancellationToken ct = default)
    {
        var libraries = await _library.GetAllLibrariesAsync();
        foreach (var lib in libraries)
        {
            ct.ThrowIfCancellationRequested();
            await ScanLibraryAsync(lib.Id, ct);
        }
    }

    public async Task ScanLibraryAsync(int libraryId, CancellationToken ct = default)
    {
        var lib = await _library.GetLibraryByIdAsync(libraryId);
        if (lib == null || !Directory.Exists(lib.Path))
        {
            _logger.LogWarning("Library {LibraryId} not found or path missing", libraryId);
            return;
        }

        Report($"Scanning library: {lib.Label ?? lib.Path}");
        _logger.LogInformation("Scanning library {Id} at {Path}", lib.Id, lib.Path);

        // Each top-level subfolder in the library path is a series
        var topDirs = Directory.GetDirectories(lib.Path);
        foreach (var seriesDir in topDirs)
        {
            ct.ThrowIfCancellationRequested();
            var folderName = Path.GetFileName(seriesDir);

            // Skip known episode-type subfolders at the library root level
            if (EpisodeTypes.IsKnownSubfolder(folderName))
                continue;

            await ScanSeriesDirectoryAsync(libraryId, seriesDir, folderName, ct);
        }

        // Handle loose video files at the library root (treated as an "Unsorted" series)
        await ScanLooseFilesAsync(libraryId, lib.Path, ct);

        // Prune episodes whose files no longer exist
        await PruneDeletedEpisodesAsync(libraryId, ct);

        Report("Scan complete.");
        _logger.LogInformation("Scan complete for library {Id}", lib.Id);
    }

    private async Task ScanSeriesDirectoryAsync(int libraryId, string seriesDir,
        string folderName, CancellationToken ct)
    {
        Report($"Scanning series: {folderName}");

        var seriesId = await _library.UpsertSeriesAsync(libraryId, folderName, seriesDir);

        // Scan video files directly in the series folder (regular episodes)
        await ScanEpisodesInFolderAsync(seriesId, seriesDir, EpisodeTypes.Episode, ct);

        // Scan known subfolders (Specials, OVA, etc.)
        foreach (var subDir in Directory.GetDirectories(seriesDir))
        {
            ct.ThrowIfCancellationRequested();
            var subFolderName = Path.GetFileName(subDir);
            if (EpisodeTypes.IsKnownSubfolder(subFolderName))
            {
                var episodeType = EpisodeTypes.FromFolderName(subFolderName);
                await ScanEpisodesInFolderAsync(seriesId, subDir, episodeType, ct);
            }
            else
            {
                // Treat unknown subfolders as containing regular episodes
                await ScanEpisodesInFolderAsync(seriesId, subDir, EpisodeTypes.Episode, ct);
            }
        }
    }

    private async Task ScanEpisodesInFolderAsync(int seriesId, string folder,
        string episodeType, CancellationToken ct)
    {
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            ct.ThrowIfCancellationRequested();
            if (!FileHelper.IsSupportedVideo(file))
                continue;

            var episodeNumber = EpisodeParser.ParseEpisodeNumber(file);
            var title = EpisodeParser.ParseTitle(file);

            await _library.UpsertEpisodeAsync(seriesId, file, title, episodeNumber, episodeType);
        }
    }

    private async Task ScanLooseFilesAsync(int libraryId, string libraryPath, CancellationToken ct)
    {
        var looseFiles = Directory.EnumerateFiles(libraryPath)
            .Where(FileHelper.IsSupportedVideo)
            .ToList();

        if (looseFiles.Count == 0)
            return;

        Report("Scanning loose files...");
        var seriesId = await _library.UpsertSeriesAsync(libraryId, "Unsorted", libraryPath);

        foreach (var file in looseFiles)
        {
            ct.ThrowIfCancellationRequested();
            var episodeNumber = EpisodeParser.ParseEpisodeNumber(file);
            var title = EpisodeParser.ParseTitle(file);
            await _library.UpsertEpisodeAsync(seriesId, file, title, episodeNumber, EpisodeTypes.Episode);
        }
    }

    private async Task PruneDeletedEpisodesAsync(int libraryId, CancellationToken ct)
    {
        var allSeries = await _library.GetSeriesByLibraryIdAsync(libraryId);
        foreach (var series in allSeries)
        {
            ct.ThrowIfCancellationRequested();
            var episodes = await _library.GetEpisodesBySeriesIdAsync(series.Id);
            foreach (var ep in episodes)
            {
                if (!File.Exists(ep.FilePath))
                {
                    _logger.LogInformation("Pruning missing episode: {FilePath}", ep.FilePath);
                    await _library.DeleteEpisodeAsync(ep.Id);
                }
            }

            // If series directory is gone, remove the series too
            if (!Directory.Exists(series.Path))
            {
                _logger.LogInformation("Pruning missing series: {Path}", series.Path);
                await _library.DeleteSeriesAsync(series.Id);
            }
        }
    }

    private void Report(string message) => ScanProgress?.Invoke(message);
}
