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

            var dirInfo = new DirectoryInfo(seriesDir);
            if (EpisodeTypes.IsKnownSubfolder(dirInfo.Name))
            {
                Report($"Skipping known subfolder at library root: {dirInfo.Name}");
                continue;
            }

            seriesCount++;

            // ── Categorize subfolders into three distinct groups ──
            var subDirs = dirInfo.GetDirectories();

            // 1. Standard season folders: "Season 1", "S02", "Book 3" (but NOT "Specials"/"OVA")
            var seasonFolders = subDirs
                .Where(d => EpisodeParser.TryParseSeasonFromFolder(d.Name, out _)
                            && !EpisodeTypes.IsKnownSubfolder(d.Name))
                .ToList();

            // 2. Known special-type folders: "Specials", "OVA", "OVAs", "Extras", "NCOP", "NCED"
            var specialFolders = subDirs
                .Where(d => EpisodeTypes.IsKnownSubfolder(d.Name)
                            && FileHelper.ContainsVideoFiles(d.FullName))
                .ToList();

            // 3. Non-standard content folders: "BorN", "New", "Hero" — have videos, no recognized pattern
            var contentFolders = subDirs
                .Where(d => !EpisodeParser.TryParseSeasonFromFolder(d.Name, out _)
                            && !EpisodeTypes.IsKnownSubfolder(d.Name)
                            && FileHelper.ContainsVideoFiles(d.FullName))
                .ToList();

            var hasRootVideos = FileHelper.ContainsVideoFiles(dirInfo.FullName);

            if (seasonFolders.Count > 0)
            {
                // ── Multi-season (standard): "Show" -> "Season 1", "Season 2" ──
                Report($"Scanning multi-season series (standard): {dirInfo.Name}");
                foreach (var seasonDir in seasonFolders)
                    await ScanSeasonAsync(libraryId, dirInfo.Name, seasonDir.FullName, ct);

                // Non-standard content folders alongside seasons (e.g. "Movies" next to "Season 1")
                foreach (var extraDir in contentFolders)
                {
                    Report($"  Also scanning non-standard subfolder: {extraDir.Name}");
                    await ScanSeasonAsync(libraryId, dirInfo.Name, extraDir.FullName, ct);
                }

                // Top-level special folders (OVA, Specials alongside Season folders)
                foreach (var specialDir in specialFolders)
                    await ScanSeasonAsync(libraryId, dirInfo.Name, specialDir.FullName, ct);

                // Root videos alongside seasons (rare but valid — e.g. a movie file next to Season folders)
                if (hasRootVideos)
                    await ScanSeasonAsync(libraryId, dirInfo.Name, dirInfo.FullName, ct);
            }
            else if (contentFolders.Count > 0 && !hasRootVideos)
            {
                // ── Non-standard multi-season: "High School DxD" -> "New", "BorN", "Hero" ──
                Report($"Scanning multi-season series (non-standard names): {dirInfo.Name}");
                var seasonCounter = 1;
                foreach (var seasonDir in contentFolders.OrderBy(d => d.Name))
                    await ScanSeasonAsync(libraryId, dirInfo.Name, seasonDir.FullName, ct, fallbackSeasonNumber: seasonCounter++);

                // Special folders alongside non-standard seasons
                foreach (var specialDir in specialFolders)
                    await ScanSeasonAsync(libraryId, dirInfo.Name, specialDir.FullName, ct);
            }
            else
            {
                // ── Single-season: video files at root, specials in subfolders ──
                Report($"Scanning single-season series: {dirInfo.Name}");
                await ScanSeasonAsync(libraryId, dirInfo.Name, dirInfo.FullName, ct);
                // ScanSeasonAsync internally handles special subfolders within the season folder
            }
        }

        Report($"Scanned {seriesCount} series folders");

        // Handle loose video files at the library root (treated as an "Unsorted" series)
        await ScanLooseFilesAsync(libraryId, lib.Path, ct);

        // Prune episodes whose files no longer exist
        await PruneDeletedEpisodesAsync(libraryId, ct);

        Report($"Scan complete for library {lib.Id}");
    }

    private async Task ScanSeasonAsync(int libraryId, string seriesGroupName, string seasonPath, CancellationToken ct, int? fallbackSeasonNumber = null)
    {
        var seasonDirInfo = new DirectoryInfo(seasonPath);
        var seasonFolderName = seasonDirInfo.Name;

        var seasonNumber = 1;
        if (!EpisodeParser.TryParseSeasonFromFolder(seasonFolderName, out seasonNumber) && fallbackSeasonNumber.HasValue)
        {
            seasonNumber = fallbackSeasonNumber.Value;
            Report($"  Using fallback season number {seasonNumber} for '{seasonFolderName}'");
        }

        Report($"  Scanning season: group='{seriesGroupName}', folder='{seasonFolderName}', season={seasonNumber}");

        var seriesId = await _library.UpsertSeriesAsync(libraryId, seasonFolderName, seasonPath, seriesGroupName, seasonNumber);
        Report($"    Series upserted — ID: {seriesId}, group: '{seriesGroupName}', folder: '{seasonFolderName}'");

        // Determine the default episode type from the folder name
        // "Specials" → SPECIAL, "OVA" → OVA, "Season 1" → EPISODE, etc.
        var folderEpisodeType = EpisodeTypes.FromFolderName(seasonFolderName);

        // Scan for episodes directly in this folder
        var totalEpCount = await ScanEpisodesInFolderAsync(seriesId, seasonPath, folderEpisodeType, ct);
        Report($"    Found {totalEpCount} episode(s) in season root");

        // Also scan for special sub-folders within a season folder, e.g. "Season 1/Specials"
        var specialSubDirs = seasonDirInfo.GetDirectories()
            .Where(d => EpisodeTypes.IsKnownSubfolder(d.Name))
            .ToList();

        foreach (var subDir in specialSubDirs)
        {
            ct.ThrowIfCancellationRequested();
            var subFolderName = Path.GetFileName(subDir.FullName);
            var episodeType = EpisodeTypes.FromFolderName(subFolderName);
            var subCount = await ScanEpisodesInFolderAsync(seriesId, subDir.FullName, episodeType, ct);
            totalEpCount += subCount;
            Report($"    Found {subCount} {episodeType} episode(s) in {subFolderName}/");
        }

        // Clean up: if no episodes were found at all, remove the empty series entry
        if (totalEpCount == 0)
        {
            Report($"    Series '{seasonFolderName}' has 0 episodes — removing empty entry (ID: {seriesId})");
            await _library.DeleteSeriesAsync(seriesId);
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

            var seriesId = await _library.UpsertSeriesAsync(libraryId, seriesName, libraryPath, seriesName, 1);
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

        // [Fix] Safety Guard: Ensure Library Root is still accessible.
        // If the network dropped mid-scan, 'Scan' might have just skipped files, 
        // but 'Prune' would incorrectly delete everything.
        var lib = await _library.GetLibraryByIdAsync(libraryId);
        if (lib == null || !Directory.Exists(lib.Path))
        {
            Report($"[CRITICAL WARNING] Library path '{lib?.Path}' is inaccessible. ABORTING PRUNE to prevent data loss.");
            return;
        }

        var allSeries = await _library.GetSeriesByLibraryIdAsync(libraryId);
        var prunedEpisodes = 0;
        var prunedSeries = 0;

        foreach (var series in allSeries)
        {
            ct.ThrowIfCancellationRequested();

            // [Fix] Secondary Safety: Check if the series drive/root is available before checking the specific folder.
            // If 'D:\Anime' is the library, and 'D:\' is gone, don't delete 'Naruto'.
            if (!Directory.Exists(Path.GetPathRoot(series.Path)))
            {
                Report($"  Skipping prune for '{series.FolderName}': Drive root inaccessible.");
                continue;
            }

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
