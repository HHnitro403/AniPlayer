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
        
        private readonly ILibraryService _libraryService;
        private readonly IScannerService _scannerService;
        private readonly IFolderWatcherService _folderWatcher;
        private readonly IWatchProgressService _watchProgressService;
        
        private IReadOnlyList<Episode> _currentPlaylist = new List<Episode>();
        private bool _isFullscreen;

        public MainWindow()
        {
            Logger.Log("MainWindow constructor called");
            InitializeComponent();
            
            _libraryService       = App.Services.GetRequiredService<ILibraryService>();
            _scannerService       = App.Services.GetRequiredService<IScannerService>();
            _folderWatcher        = App.Services.GetRequiredService<IFolderWatcherService>();
            _watchProgressService = App.Services.GetRequiredService<IWatchProgressService>();
            
            _homePage = new HomePage();
            _libraryPage = new LibraryPage();
            _playerPage = new PlayerPage();
            _showInfoPage = new ShowInfoPage();
            _optionsPage = new OptionsPage();

            WireEvents();
            NavigateTo("Home");
            
            _ = InitializeAsync();

            Closing += MainWindow_Closing;
            KeyDown += OnMainWindowKeyDown;
            Logger.Log("MainWindow constructor completed");
        }
        
        private void NavigateTo(string page)
        {
            Logger.Log($"NavigateTo: {page}");

            if (page != "Player" && PageHost.Content == _playerPage)
                _playerPage.PausePlayback();

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
        
        private void WireEvents()
        {
            SidebarControl.HomeClicked    += () => NavigateTo("Home");
            SidebarControl.LibraryClicked += () => NavigateTo("Library");
            SidebarControl.PlayerClicked  += () => NavigateTo("Player");
            SidebarControl.SettingsClicked += () => NavigateTo("Settings");
            
            _homePage.AddLibraryRequested += () => NavigateTo("Settings");
            _homePage.SeriesSelected += seriesId =>
            {
                _ = OpenSeriesAsync(seriesId);
            };
            _homePage.ResumeEpisodeRequested += id =>
            {
                _ = ResumeEpisodeAsync(id);
            };
            
            _libraryPage.SeriesSelected += groupName =>
            {
                _ = OpenSeriesAsync(groupName);
            };
            _libraryPage.FolderAdded += path =>
            {
                _ = AddLibraryAsync(path, "LibraryPage");
            };
            
            _showInfoPage.BackRequested += () => NavigateTo("Library");
            _showInfoPage.EpisodePlayRequested += filePath =>
            {
                var index = _currentPlaylist.ToList().FindIndex(e => e.FilePath == filePath);
                if (index >= 0)
                {
                    NavigateTo("Player");
                    _ = _playerPage.LoadPlaylistAsync(_currentPlaylist, index);
                }
            };
            _showInfoPage.MetadataRefreshRequested += () =>
            {
                _ = RefreshPagesAsync();
            };
            
            _playerPage.PlaybackStopped += () =>
            {
                if (_isFullscreen) ToggleFullscreen();
                NavigateTo("Home");
            };
            _playerPage.FullscreenToggleRequested += ToggleFullscreen;
            
            _optionsPage.LibraryFolderAdded += path =>
            {
                _ = AddLibraryAsync(path, "OptionsPage");
            };
            _optionsPage.LibraryRemoveRequested += id =>
            {
                _ = RemoveLibraryAsync(id);
            };
            
            _scannerService.ScanProgress += msg => Logger.Log($"[Scanner] {msg}", LogRegion.Scanner);
            
            _folderWatcher.LibraryChanged += libraryId =>
            {
                _ = RescanLibraryAsync(libraryId);
            };
        }
        
        private async Task AddLibraryAsync(string path, string source)
        {
            Logger.Log($"[AddLibrary] START (source: {source})");
            path = path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            
            if (!System.IO.Directory.Exists(path))
            {
                Logger.Log($"[AddLibrary] ERROR: Directory does not exist, aborting");
                return;
            }

            try
            {
                var existing = await _libraryService.GetLibraryByPathAsync(path)
                    ?? await _libraryService.GetLibraryByPathAsync(path + System.IO.Path.DirectorySeparatorChar);
                if (existing != null)
                {
                    await _scannerService.ScanLibraryAsync(existing.Id);
                    await RefreshPagesAsync();
                    return;
                }
                
                var label = System.IO.Path.GetFileName(path);
                var libraryId = await _libraryService.AddLibraryAsync(path, label);
                await _scannerService.ScanLibraryAsync(libraryId);
                _folderWatcher.WatchLibrary(libraryId, path);
                await RefreshPagesAsync();
            }
            catch (Exception ex) { Logger.Log($"[AddLibrary] FAILED: {ex.Message}"); }
        }

        private async Task RemoveLibraryAsync(int libraryId)
        {
            try
            {
                _folderWatcher.StopWatching(libraryId);
                await _libraryService.DeleteLibraryAsync(libraryId);
                await RefreshPagesAsync();
            }
            catch (Exception ex) { Logger.Log($"[RemoveLibrary] Failed: {ex.Message}"); }
        }

        private async Task OpenSeriesAsync(string seriesGroupName)
        {
            try
            {
                var seriesGroup = (await _libraryService.GetSeriesByGroupNameAsync(seriesGroupName)).ToList();
                if (seriesGroup.Count == 0) return;

                var allEpisodes = new List<Episode>();
                foreach (var series in seriesGroup.OrderBy(s => s.SeasonNumber))
                {
                    var episodes = (await _libraryService.GetEpisodesBySeriesIdAsync(series.Id)).ToList();
                    allEpisodes.AddRange(episodes.OrderBy(e => e.EpisodeNumber));
                }
                _currentPlaylist = allEpisodes;

                _showInfoPage.LoadSeriesData(seriesGroup, allEpisodes);
                NavigateTo("ShowInfo");
            }
            catch (Exception ex) { Logger.Log($"[OpenSeries] Failed: {ex.Message}"); }
        }

        private async Task OpenSeriesAsync(int seriesId)
        {
            try
            {
                var series = await _libraryService.GetSeriesByIdAsync(seriesId);
                if (series?.SeriesGroupName != null)
                {
                    await OpenSeriesAsync(series.SeriesGroupName);
                }
            }
            catch (Exception ex) { Logger.Log($"[OpenSeries] Failed to open single series {seriesId}: {ex.Message}"); }
        }

        private async Task ResumeEpisodeAsync(int episodeId)
        {
            try
            {
                var episode = await _libraryService.GetEpisodeByIdAsync(episodeId);
                if (episode == null) return;
                
                var series = await _libraryService.GetSeriesByIdAsync(episode.SeriesId);
                if (series?.SeriesGroupName == null) return;

                var seriesGroup = await _libraryService.GetSeriesByGroupNameAsync(series.SeriesGroupName);
                var allEpisodes = new List<Episode>();
                foreach (var s in seriesGroup.OrderBy(s => s.SeasonNumber))
                {
                    var episodes = (await _libraryService.GetEpisodesBySeriesIdAsync(s.Id)).ToList();
                    allEpisodes.AddRange(episodes.OrderBy(e => e.EpisodeNumber));
                }
                
                var index = allEpisodes.FindIndex(e => e.Id == episodeId);
                if (index >= 0)
                {
                    _currentPlaylist = allEpisodes;
                    NavigateTo("Player");
                    await _playerPage.LoadPlaylistAsync(_currentPlaylist, index);
                }
            }
            catch (Exception ex) { Logger.Log($"[ResumeEpisode] Failed: {ex.Message}"); }
        }
        
        private async Task RefreshPagesAsync()
        {
            try
            {
                var libraries = (await _libraryService.GetAllLibrariesAsync()).ToList();
                var allSeries = (await _libraryService.GetAllSeriesAsync()).ToList();
                // Group recently added series by SeriesGroupName so each anime appears once
                var recentSeries = allSeries
                    .Where(s => s.CreatedAt != null && DateTime.Parse(s.CreatedAt) >= DateTime.Now.AddDays(-14))
                    .GroupBy(s => s.SeriesGroupName, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(s => s.CoverImagePath != null).First())
                    .ToList();
                
                var recentlyWatched = (await _watchProgressService.GetRecentlyWatchedAsync(10)).ToList();
                
                var seriesLookup = allSeries.ToDictionary(s => s.Id);
                var continueItems = recentlyWatched.Select(rw =>
                {
                    seriesLookup.TryGetValue(rw.Episode.SeriesId, out var series);
                    return (rw.Episode, rw.Progress, Series: series);
                }).ToList();
                
                _optionsPage.DisplayLibraries(libraries);
                _libraryPage.DisplaySeries(allSeries);
                _homePage.DisplayContinueWatching(continueItems);
                _homePage.DisplayRecentlyAdded(recentSeries);
                _homePage.SetHasLibraries(libraries.Count > 0);
            }
            catch (Exception ex) { Logger.Log($"[RefreshPages] FAILED: {ex.Message}"); }
        }
        
        private async Task InitializeAsync()
        {
            await StartFolderWatchersAsync();
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
            }
            catch (Exception ex) { Logger.Log($"Failed to start folder watchers: {ex.Message}"); }
        }

        private async Task RescanLibraryAsync(int libraryId)
        {
            await _scannerService.ScanLibraryAsync(libraryId);
            await RefreshPagesAsync();
        }
        
        private void ToggleFullscreen()
        {
            _isFullscreen = !_isFullscreen;
            WindowState = _isFullscreen ? WindowState.FullScreen : WindowState.Normal;
            SidebarControl.IsVisible = !_isFullscreen;
            _playerPage.SetFullscreen(_isFullscreen);
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
            else if (PageHost.Content == _playerPage && _playerPage.HandleKeyDown(e.Key))
            {
                e.Handled = true;
            }
        }
        
        private async void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            Logger.Log("MainWindow closing — shutting down");
            _folderWatcher.StopAll();
            await _playerPage.ShutdownAsync();
        }
    }
}
