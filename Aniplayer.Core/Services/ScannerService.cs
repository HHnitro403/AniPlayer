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

        // Wire parser logging into scan progress so it shows in debug.log
        EpisodeParser.LogCallback = msg => Report(msg);
    }

    public async Task ScanAllLibrariesAsync(CancellationToken ct = default)
    {
        var libraries = (await _library.GetAllLibrariesAsync()).ToList();
        Report($"ScanAllLibraries: found {libraries.Count} library(ies) in DB");
        foreach (var lib in libraries)
        {
            Report($"ScanAllLibraries: scanning library ID={lib.Id}, path='{lib.Path}'");
            ct.ThrowIfCancellationRequested();
            await ScanLibraryAsync(lib.Id, ct);
        }
    }

    public async Task ScanLibraryAsync(int libraryId, CancellationToken ct = default)
    {
        Report($"=== ScanLibrary START: libraryId={libraryId} ===");
        var lib = await _library.GetLibraryByIdAsync(libraryId);
        if (lib == null)
        {
            Report($"ERROR: Library ID {libraryId} not found in database");
            return;
        }
        Report($"Library from DB: ID={lib.Id}, path='{lib.Path}', label='{lib.Label}'");

        var pathExists = Directory.Exists(lib.Path);
        Report($"Directory.Exists('{lib.Path}') = {pathExists}");
        if (!pathExists)
        {
            // Try trimming trailing slash
            var trimmed = lib.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var trimmedExists = Directory.Exists(trimmed);
            Report($"Directory.Exists(trimmed '{trimmed}') = {trimmedExists}");
            if (!trimmedExists)
            {
                Report($"ERROR: Library path does not exist in either form");
                return;
            }
            Report($"Using trimmed path for scan");
        }

        Report($"Scanning library: {lib.Label ?? lib.Path} (ID: {lib.Id})");
        Report($"Library path: {lib.Path}");

        // Each top-level subfolder in the library path is a series
        var topDirs = Directory.GetDirectories(lib.Path);
        Report($"Found {topDirs.Length} top-level folders");
        for (int i = 0; i < topDirs.Length; i++)
            Report($"  top-dir[{i}]: '{topDirs[i]}'");

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

            // Detect episode type from folder name — handles both exact ("OVA")
            // and suffixed ("High School DxD S1 - OVA") folder names
            var episodeType = EpisodeTypes.FromFolderName(subFolderName);
            var subCount = await ScanEpisodesInFolderAsync(seriesId, subDir, episodeType, ct);
            Report($"  Found {subCount} {episodeType} episode(s) in {subFolderName}/");
        }
    }

    private async Task<int> ScanEpisodesInFolderAsync(int seriesId, string folder,
        string episodeType, CancellationToken ct)
    {
        Report($"    ScanEpisodesInFolder: folder='{folder}', seriesId={seriesId}, type={episodeType}");
        Report($"    ScanEpisodesInFolder: Directory.Exists='{Directory.Exists(folder)}'");

        var allFiles = Directory.EnumerateFiles(folder).ToList();
        Report($"    ScanEpisodesInFolder: total files in folder: {allFiles.Count}");

        var count = 0;
        foreach (var file in allFiles)
        {
            ct.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(file);
            var ext = Path.GetExtension(file);
            var isSupported = FileHelper.IsSupportedVideo(file);

            if (!isSupported)
            {
                Report($"    SKIP (unsupported ext '{ext}'): {fileName}");
                continue;
            }

            Report($"    FOUND video: {fileName} (ext='{ext}')");

            var episodeNumber = EpisodeParser.ParseEpisodeNumber(file);
            var title = EpisodeParser.ParseEpisodeTitle(file) ?? EpisodeParser.ParseTitle(file);

            // Try to detect type from filename (e.g. "Nced-1.mkv" in an Extra folder)
            var fileType = EpisodeTypes.FromFileName(fileName);
            if (fileType == null)
            {
                fileType = episodeType; // Fall back to folder type
            }
            else if (fileType != episodeType && episodeType != EpisodeTypes.Episode)
            {
                Report($"    WARNING: Type mismatch — file suggests '{fileType}', folder suggests '{episodeType}', using file type");
            }
            Report($"    PARSED: ep={episodeNumber?.ToString() ?? "null"}, title='{title ?? "null"}', type={fileType}");

            var epId = await _library.UpsertEpisodeAsync(seriesId, file, title, episodeNumber, fileType);
            Report($"    UPSERTED episode ID={epId} for seriesId={seriesId}");
            count++;
        }
        Report($"    ScanEpisodesInFolder result: {count} episode(s) added");
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

        // Group files by parsed anime title so each show gets its own series entry
        var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in looseFiles)
        {
            var parsedTitle = EpisodeParser.ParseTitle(file) ?? "Unsorted";
            if (!groups.ContainsKey(parsedTitle))
                groups[parsedTitle] = new List<string>();
            groups[parsedTitle].Add(file);
        }

        Report($"Grouped into {groups.Count} series by parsed title");

        foreach (var (seriesName, files) in groups)
        {
            ct.ThrowIfCancellationRequested();

            var seriesId = await _library.UpsertSeriesAsync(libraryId, seriesName, libraryPath);
            Report($"  Series '{seriesName}' upserted — ID: {seriesId} ({files.Count} file(s))");

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(file);
                var episodeNumber = EpisodeParser.ParseEpisodeNumber(file);
                var title = EpisodeParser.ParseTitle(file);
                Report($"    Loose file: {fileName} → ep={episodeNumber?.ToString() ?? "?"}, title={title ?? "(none)"}");
                await _library.UpsertEpisodeAsync(seriesId, file, title, episodeNumber, EpisodeTypes.Episode);
            }
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
