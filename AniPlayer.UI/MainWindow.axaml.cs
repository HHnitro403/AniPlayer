using Avalonia.Controls;
using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using Aniplayer.Core.Interfaces;
using Aniplayer.Core.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace AniPlayer.UI
{
    public partial class MainWindow : Window
    {
        private readonly HomePage _homePage;
        private readonly LibraryPage _libraryPage;
        private readonly PlayerPage _playerPage;
        private readonly ShowInfoPage _showInfoPage;
        private readonly OptionsPage _optionsPage;

        // Services resolved from DI
        private readonly ILibraryService _libraryService;
        private readonly IScannerService _scannerService;
        private readonly IFolderWatcherService _folderWatcher;

        private List<Episode> _currentEpisodes = new();
        private bool _isFullscreen;

        public MainWindow()
        {
            Logger.Log("MainWindow constructor called");
            InitializeComponent();

            // Resolve services from DI container
            _libraryService = App.Services.GetRequiredService<ILibraryService>();
            _scannerService = App.Services.GetRequiredService<IScannerService>();
            _folderWatcher  = App.Services.GetRequiredService<IFolderWatcherService>();

            // Create pages once — PlayerPage holds the mpv instance for its lifetime
            _homePage = new HomePage();
            _libraryPage = new LibraryPage();
            _playerPage = new PlayerPage();
            _showInfoPage = new ShowInfoPage();
            _optionsPage = new OptionsPage();

            WireEvents();
            NavigateTo("Home");

            // Load initial data and start watchers
            _ = InitializeAsync();

            Closing += MainWindow_Closing;
            KeyDown += OnMainWindowKeyDown;
            Logger.Log("MainWindow constructor completed");
        }

        // ── Navigation ───────────────────────────────────────────

        private void NavigateTo(string page)
        {
            Logger.Log($"NavigateTo: {page}");

            PageHost.Content = page switch
            {
                "Home"     => _homePage,
                "Library"  => _libraryPage,
                "Player"   => _playerPage,
                "ShowInfo" => _showInfoPage,
                "Settings" => _optionsPage,
                _          => _homePage
            };

            SidebarControl.SetActive(page);
        }

        public async void PlayFile(string filePath)
        {
            NavigateTo("Player");

            // Pass full episode playlist so auto-next works
            var files = _currentEpisodes.Select(e => e.FilePath).ToArray();
            var index = Array.IndexOf(files, filePath);
            if (index >= 0)
                _playerPage.SetPlaylist(files, index);

            await _playerPage.LoadFileAsync(filePath);
        }

        // ── Event wiring ─────────────────────────────────────────

        private void WireEvents()
        {
            // Sidebar
            SidebarControl.HomeClicked    += () => NavigateTo("Home");
            SidebarControl.LibraryClicked += () => NavigateTo("Library");
            SidebarControl.PlayerClicked  += () => NavigateTo("Player");
            SidebarControl.SettingsClicked += () => NavigateTo("Settings");

            // HomePage
            _homePage.PlayFileRequested   += PlayFile;
            _homePage.AddLibraryRequested += () => NavigateTo("Settings");
            _homePage.SeriesSelected += id =>
            {
                Logger.Log($"[MainWindow] HomePage.SeriesSelected: ID={id}");
                _ = OpenSeriesAsync(id);
            };

            // LibraryPage
            _libraryPage.SeriesSelected += id =>
            {
                Logger.Log($"[MainWindow] SeriesSelected: ID={id}");
                _ = OpenSeriesAsync(id);
            };
            _libraryPage.FolderAdded += path =>
            {
                Logger.Log($"[MainWindow] LibraryPage.FolderAdded event received: {path}");
                _ = AddLibraryAsync(path, "LibraryPage");
            };

            // ShowInfoPage
            _showInfoPage.BackRequested += () => NavigateTo("Library");
            _showInfoPage.EpisodePlayRequested += filePath =>
            {
                Logger.Log($"[MainWindow] EpisodePlayRequested: {filePath}");
                PlayFile(filePath);
            };

            // PlayerPage
            _playerPage.PlaybackStopped += () =>
            {
                if (_isFullscreen) ToggleFullscreen();
                NavigateTo("Home");
            };
            _playerPage.FullscreenToggleRequested += ToggleFullscreen;

            // OptionsPage
            _optionsPage.LibraryFolderAdded += path =>
            {
                Logger.Log($"[MainWindow] OptionsPage.LibraryFolderAdded event received: {path}");
                _ = AddLibraryAsync(path, "OptionsPage");
            };
            _optionsPage.LibraryRemoveRequested += id =>
            {
                Logger.Log($"[MainWindow] OptionsPage.LibraryRemoveRequested: ID {id}");
                _ = RemoveLibraryAsync(id);
            };

            // ScannerService — pipe scan progress into debug.log (Scanner region)
            _scannerService.ScanProgress += msg => Logger.Log($"[Scanner] {msg}", LogRegion.Scanner);

            // FolderWatcher — re-scan when files change on disk
            _folderWatcher.LibraryChanged += libraryId =>
            {
                Logger.Log($"[FolderWatcher] Library {libraryId} changed on disk, triggering re-scan");
                _ = RescanLibraryAsync(libraryId);
            };
        }

        // ── Add Library (shared by LibraryPage + OptionsPage) ────

        private async Task AddLibraryAsync(string path, string source)
        {
            Logger.Log($"[AddLibrary] === START (source: {source}) ===");
            Logger.Log($"[AddLibrary] Raw path: {path}");

            // Normalize: trim trailing directory separators so Path.GetFileName works
            path = path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            Logger.Log($"[AddLibrary] Normalized path: {path}");

            // Validate path
            var exists = System.IO.Directory.Exists(path);
            Logger.Log($"[AddLibrary] Directory exists: {exists}");
            if (!exists)
            {
                Logger.Log($"[AddLibrary] ERROR: Directory does not exist, aborting");
                return;
            }

            try
            {
                // Check if this library path is already registered (try with and without trailing slash)
                var existing = await _libraryService.GetLibraryByPathAsync(path)
                    ?? await _libraryService.GetLibraryByPathAsync(path + System.IO.Path.DirectorySeparatorChar);
                if (existing != null)
                {
                    Logger.Log($"[AddLibrary] Library already exists (ID: {existing.Id}), re-scanning instead of inserting");
                    await _scannerService.ScanLibraryAsync(existing.Id);
                    Logger.Log($"[AddLibrary] === RE-SCAN COMPLETE (library {existing.Id}) ===");
                    await RefreshPagesAsync();
                    return;
                }

                // Step 1: Insert into database
                var label = System.IO.Path.GetFileName(path);
                Logger.Log($"[AddLibrary] Step 1: Inserting into DB — path='{path}', label='{label}'");
                var libraryId = await _libraryService.AddLibraryAsync(path, label);
                Logger.Log($"[AddLibrary] Step 1 DONE: Library inserted with ID {libraryId}");

                // Step 2: Scan for series + episodes
                Logger.Log($"[AddLibrary] Step 2: Scanning library {libraryId} for series and episodes...");
                await _scannerService.ScanLibraryAsync(libraryId);
                Logger.Log($"[AddLibrary] Step 2 DONE: Scan complete for library {libraryId}");

                // Step 3: Start folder watcher
                Logger.Log($"[AddLibrary] Step 3: Starting folder watcher for library {libraryId}");
                _folderWatcher.WatchLibrary(libraryId, path);
                Logger.Log($"[AddLibrary] Step 3 DONE: Now watching library {libraryId}");

                // Step 4: Refresh all pages to show new data
                await RefreshPagesAsync();

                Logger.Log($"[AddLibrary] === SUCCESS (library {libraryId}) ===");
            }
            catch (Exception ex)
            {
                Logger.Log($"[AddLibrary] === FAILED ===");
                Logger.Log($"[AddLibrary] Exception type: {ex.GetType().Name}");
                Logger.Log($"[AddLibrary] Message: {ex.Message}");
                Logger.Log($"[AddLibrary] StackTrace: {ex.StackTrace}");
                if (ex.InnerException != null)
                    Logger.Log($"[AddLibrary] InnerException: {ex.InnerException.Message}");
            }
        }

        private async Task RemoveLibraryAsync(int libraryId)
        {
            try
            {
                Logger.Log($"[RemoveLibrary] Removing library {libraryId}");
                _folderWatcher.StopWatching(libraryId);
                await _libraryService.DeleteLibraryAsync(libraryId);
                Logger.Log($"[RemoveLibrary] Library {libraryId} deleted");
                await RefreshPagesAsync();
            }
            catch (Exception ex)
            {
                Logger.Log($"[RemoveLibrary] Failed: {ex.Message}");
            }
        }

        private async Task OpenSeriesAsync(int seriesId)
        {
            try
            {
                Logger.Log($"[OpenSeries] Loading series {seriesId}...");
                var series = await _libraryService.GetSeriesByIdAsync(seriesId);
                if (series == null)
                {
                    Logger.Log($"[OpenSeries] ERROR: Series {seriesId} not found in DB");
                    return;
                }

                var episodes = (await _libraryService.GetEpisodesBySeriesIdAsync(seriesId)).ToList();
                _currentEpisodes = episodes;
                Logger.Log($"[OpenSeries] Loaded series '{series.DisplayTitle}' with {episodes.Count} episode(s)");

                _showInfoPage.LoadSeriesData(series, episodes);
                NavigateTo("ShowInfo");
            }
            catch (Exception ex)
            {
                Logger.Log($"[OpenSeries] Failed: {ex.Message}");
            }
        }

        // ── Data refresh ────────────────────────────────────────

        private async Task RefreshPagesAsync()
        {
            try
            {
                Logger.Log("[RefreshPages] === START ===");

                var libraries = (await _libraryService.GetAllLibrariesAsync()).ToList();
                Logger.Log($"[RefreshPages] DB returned {libraries.Count} libraries");
                foreach (var lib in libraries)
                    Logger.Log($"[RefreshPages]   Library ID={lib.Id}, path='{lib.Path}', label='{lib.Label}'", LogRegion.DB);

                var allSeries = (await _libraryService.GetAllSeriesAsync()).ToList();
                Logger.Log($"[RefreshPages] DB returned {allSeries.Count} series");
                foreach (var s in allSeries)
                    Logger.Log($"[RefreshPages]   Series ID={s.Id}, libId={s.LibraryId}, folder='{s.FolderName}', display='{s.DisplayTitle}'", LogRegion.DB);

                // Log episode counts per series (DB region — verbose)
                foreach (var s in allSeries)
                {
                    var episodes = (await _libraryService.GetEpisodesBySeriesIdAsync(s.Id)).ToList();
                    Logger.Log($"[RefreshPages]   Series '{s.FolderName}' (ID={s.Id}) has {episodes.Count} episodes", LogRegion.DB);
                    foreach (var ep in episodes)
                        Logger.Log($"[RefreshPages]     Episode ID={ep.Id}, ep#={ep.EpisodeNumber?.ToString() ?? "null"}, file='{ep.FilePath}'", LogRegion.DB);
                }

                // Recently added series (last 14 days)
                var recentSeries = (await _libraryService.GetRecentlyAddedSeriesAsync(14)).ToList();
                Logger.Log($"[RefreshPages] Recently added (14 days): {recentSeries.Count} series");

                // Push data to pages
                _optionsPage.DisplayLibraries(libraries);
                _libraryPage.DisplaySeries(allSeries);
                _homePage.DisplayRecentlyAdded(recentSeries);
                _homePage.SetHasLibraries(libraries.Count > 0);

                Logger.Log("[RefreshPages] === DONE ===");
            }
            catch (Exception ex)
            {
                Logger.Log($"[RefreshPages] === FAILED ===");
                Logger.Log($"[RefreshPages] Exception: {ex.GetType().Name}: {ex.Message}");
                Logger.Log($"[RefreshPages] StackTrace: {ex.StackTrace}");
            }
        }

        // ── Startup helpers ──────────────────────────────────────

        private async Task InitializeAsync()
        {
            await StartFolderWatchersAsync();

            // Scan all libraries on startup to pick up any that were never
            // successfully scanned (e.g. previous crash) or have new files
            Logger.Log("[Startup] Scanning all libraries...");
            await _scannerService.ScanAllLibrariesAsync();
            Logger.Log("[Startup] Startup scan complete");

            await RefreshPagesAsync();
        }

        private async Task StartFolderWatchersAsync()
        {
            try
            {
                var libraries = await _libraryService.GetAllLibrariesAsync();
                foreach (var lib in libraries)
                {
                    _folderWatcher.WatchLibrary(lib.Id, lib.Path);
                }
                Logger.Log("Folder watchers started for all libraries");
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to start folder watchers: {ex.Message}");
            }
        }

        private async Task RescanLibraryAsync(int libraryId)
        {
            await _scannerService.ScanLibraryAsync(libraryId);
            await RefreshPagesAsync();
        }

        // ── Fullscreen ───────────────────────────────────────────

        private void ToggleFullscreen()
        {
            _isFullscreen = !_isFullscreen;
            if (_isFullscreen)
            {
                WindowState = WindowState.FullScreen;
                SidebarControl.IsVisible = false;
            }
            else
            {
                WindowState = WindowState.Normal;
                SidebarControl.IsVisible = true;
            }
            Logger.Log($"Fullscreen: {_isFullscreen}");
        }

        private void OnMainWindowKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _isFullscreen)
            {
                ToggleFullscreen();
                e.Handled = true;
            }
            else if (e.Key == Key.F11 && PageHost.Content == _playerPage)
            {
                ToggleFullscreen();
                e.Handled = true;
            }
        }

        // ── Shutdown ─────────────────────────────────────────────

        private async void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            Logger.Log("MainWindow closing — shutting down");
            _folderWatcher.StopAll();
            await _playerPage.ShutdownAsync();
        }
    }
}
