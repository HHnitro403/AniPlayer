using Aniplayer.Core.Constants;
using Aniplayer.Core.Helpers;
using Aniplayer.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Aniplayer.Core.Services;

public class FolderWatcherService : IFolderWatcherService
{
    private readonly ILogger<FolderWatcherService> _logger;
    private readonly Dictionary<int, (FileSystemWatcher Watcher, DebounceHelper Debounce)> _watchers = new();

    public event Action<int>? LibraryChanged;

    public FolderWatcherService(ILogger<FolderWatcherService> logger)
    {
        _logger = logger;
    }

    public void WatchLibrary(int libraryId, string path)
    {
        if (_watchers.ContainsKey(libraryId))
        {
            _logger.LogWarning("Already watching library {Id}", libraryId);
            return;
        }

        if (!Directory.Exists(path))
        {
            _logger.LogWarning("Cannot watch library {Id}: path '{Path}' does not exist", libraryId, path);
            return;
        }

        var debounce = new DebounceHelper(
            () => { LibraryChanged?.Invoke(libraryId); return Task.CompletedTask; },
            AppConstants.ScanDebounceDelayMs);

        var watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
        };

        // Watch for supported video file changes
        watcher.Created += (_, e) => OnFileChanged(libraryId, e.FullPath, debounce);
        watcher.Deleted += (_, e) => OnFileChanged(libraryId, e.FullPath, debounce);
        watcher.Renamed += (_, e) => OnFileChanged(libraryId, e.FullPath, debounce);

        watcher.EnableRaisingEvents = true;
        _watchers[libraryId] = (watcher, debounce);

        _logger.LogInformation("Watching library {Id} at {Path}", libraryId, path);
    }

    public void StopWatching(int libraryId)
    {
        if (!_watchers.Remove(libraryId, out var entry))
            return;

        entry.Watcher.EnableRaisingEvents = false;
        entry.Watcher.Dispose();
        entry.Debounce.Dispose();

        _logger.LogInformation("Stopped watching library {Id}", libraryId);
    }

    public void StopAll()
    {
        foreach (var id in _watchers.Keys.ToList())
            StopWatching(id);
    }

    public void Dispose()
    {
        StopAll();
        GC.SuppressFinalize(this);
    }

    private void OnFileChanged(int libraryId, string filePath, DebounceHelper debounce)
    {
        // Only trigger for video files or directory changes
        if (!string.IsNullOrEmpty(Path.GetExtension(filePath)) &&
            !FileHelper.IsSupportedVideo(filePath))
            return;

        _logger.LogDebug("Change detected in library {Id}: {Path}", libraryId, filePath);
        debounce.Trigger();
    }
}
