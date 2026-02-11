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
        if (lib == null)
        {
            Report($"ERROR: Library ID {libraryId} not found in database");
            return;
        }
        if (!Directory.Exists(lib.Path))
        {
            Report($"ERROR: Library path does not exist: {lib.Path}");
            return;
        }

        Report($"Scanning library: {lib.Label ?? lib.Path} (ID: {lib.Id})");
        Report($"Library path: {lib.Path}");

        // Each top-level subfolder in the library path is a series
        var topDirs = Directory.GetDirectories(lib.Path);
        Report($"Found {topDirs.Length} top-level folders");

        var seriesCount = 0;
        foreach (var seriesDir in topDirs)
        {
            ct.ThrowIfCancellationRequested();
            var folderName = Path.GetFileName(seriesDir);

            if (EpisodeTypes.IsKnownSubfolder(folderName))
            {
                Report($"Skipping known subfolder at library root: {folderName}");
                continue;
            }

            seriesCount++;
            await ScanSeriesDirectoryAsync(libraryId, seriesDir, folderName, ct);
        }

        Report($"Scanned {seriesCount} series folders");

        // Handle loose video files at the library root (treated as an "Unsorted" series)
        await ScanLooseFilesAsync(libraryId, lib.Path, ct);

        // Prune episodes whose files no longer exist
        await PruneDeletedEpisodesAsync(libraryId, ct);

        Report($"Scan complete for library {lib.Id}");
    }

    private async Task ScanSeriesDirectoryAsync(int libraryId, string seriesDir,
        string folderName, CancellationToken ct)
    {
        Report($"Scanning series: {folderName} ({seriesDir})");

        var seriesId = await _library.UpsertSeriesAsync(libraryId, folderName, seriesDir);
        Report($"  Series upserted — ID: {seriesId}, folder: {folderName}");

        // Scan video files directly in the series folder (regular episodes)
        var epCount = await ScanEpisodesInFolderAsync(seriesId, seriesDir, EpisodeTypes.Episode, ct);
        Report($"  Found {epCount} episode(s) in root folder");

        // Scan known subfolders (Specials, OVA, etc.)
        var subDirs = Directory.GetDirectories(seriesDir);
        if (subDirs.Length > 0)
            Report($"  Found {subDirs.Length} subfolder(s) in series");

        foreach (var subDir in subDirs)
        {
            ct.ThrowIfCancellationRequested();
            var subFolderName = Path.GetFileName(subDir);
            if (EpisodeTypes.IsKnownSubfolder(subFolderName))
            {
                var episodeType = EpisodeTypes.FromFolderName(subFolderName);
                var subCount = await ScanEpisodesInFolderAsync(seriesId, subDir, episodeType, ct);
                Report($"  Found {subCount} {episodeType} episode(s) in {subFolderName}/");
            }
            else
            {
                var subCount = await ScanEpisodesInFolderAsync(seriesId, subDir, EpisodeTypes.Episode, ct);
                Report($"  Found {subCount} episode(s) in subfolder {subFolderName}/");
            }
        }
    }

    private async Task<int> ScanEpisodesInFolderAsync(int seriesId, string folder,
        string episodeType, CancellationToken ct)
    {
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            ct.ThrowIfCancellationRequested();
            if (!FileHelper.IsSupportedVideo(file))
                continue;

            var fileName = Path.GetFileName(file);
            var episodeNumber = EpisodeParser.ParseEpisodeNumber(file);
            var title = EpisodeParser.ParseTitle(file);

            Report($"    Episode: {fileName} → ep={episodeNumber?.ToString() ?? "?"}, type={episodeType}, title={title ?? "(none)"}");

            await _library.UpsertEpisodeAsync(seriesId, file, title, episodeNumber, episodeType);
            count++;
        }
        return count;
    }

    private async Task ScanLooseFilesAsync(int libraryId, string libraryPath, CancellationToken ct)
    {
        var looseFiles = Directory.EnumerateFiles(libraryPath)
            .Where(FileHelper.IsSupportedVideo)
            .ToList();

        if (looseFiles.Count == 0)
        {
            Report("No loose video files at library root");
            return;
        }

        Report($"Found {looseFiles.Count} loose video file(s) at library root");
        var seriesId = await _library.UpsertSeriesAsync(libraryId, "Unsorted", libraryPath);
        Report($"Unsorted series upserted — ID: {seriesId}");

        foreach (var file in looseFiles)
        {
            ct.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(file);
            var episodeNumber = EpisodeParser.ParseEpisodeNumber(file);
            var title = EpisodeParser.ParseTitle(file);
            Report($"  Loose file: {fileName} → ep={episodeNumber?.ToString() ?? "?"}, title={title ?? "(none)"}");
            await _library.UpsertEpisodeAsync(seriesId, file, title, episodeNumber, EpisodeTypes.Episode);
        }
    }

    private async Task PruneDeletedEpisodesAsync(int libraryId, CancellationToken ct)
    {
        Report("Pruning deleted files...");
        var allSeries = await _library.GetSeriesByLibraryIdAsync(libraryId);
        var prunedEpisodes = 0;
        var prunedSeries = 0;

        foreach (var series in allSeries)
        {
            ct.ThrowIfCancellationRequested();
            var episodes = await _library.GetEpisodesBySeriesIdAsync(series.Id);
            foreach (var ep in episodes)
            {
                if (!File.Exists(ep.FilePath))
                {
                    Report($"  Pruning missing episode: {Path.GetFileName(ep.FilePath)}");
                    await _library.DeleteEpisodeAsync(ep.Id);
                    prunedEpisodes++;
                }
            }

            if (!Directory.Exists(series.Path))
            {
                Report($"  Pruning missing series: {series.FolderName}");
                await _library.DeleteSeriesAsync(series.Id);
                prunedSeries++;
            }
        }

        Report($"Pruning done: removed {prunedEpisodes} episode(s), {prunedSeries} series");
    }

    private void Report(string message) => ScanProgress?.Invoke(message);
}
