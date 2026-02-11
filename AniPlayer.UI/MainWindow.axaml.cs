using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Aniplayer.Core.Interfaces;
using System;
using System.ComponentModel;
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

            // Start watching all existing libraries
            _ = StartFolderWatchersAsync();

            Closing += MainWindow_Closing;
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

            // LibraryPage
            _libraryPage.SeriesSelected += id =>
            {
                _showInfoPage.LoadSeries(id);
                NavigateTo("ShowInfo");
            };
            _libraryPage.FolderAdded += path => _ = AddLibraryAsync(path);

            // ShowInfoPage
            _showInfoPage.BackRequested += () => NavigateTo("Library");

            // PlayerPage
            _playerPage.PlaybackStopped += () => NavigateTo("Home");

            // OptionsPage
            _optionsPage.LibraryFolderAdded += path => _ = AddLibraryAsync(path);

            // FolderWatcher — re-scan when files change on disk
            _folderWatcher.LibraryChanged += libraryId =>
            {
                Logger.Log($"FolderWatcher: library {libraryId} changed, re-scanning");
                _ = _scannerService.ScanLibraryAsync(libraryId);
            };
        }

        // ── Add Library (shared by LibraryPage + OptionsPage) ────

        private async Task AddLibraryAsync(string path)
        {
            try
            {
                Logger.Log($"Adding library: {path}");

                // Use folder name as the label
                var label = System.IO.Path.GetFileName(path);
                var libraryId = await _libraryService.AddLibraryAsync(path, label);
                Logger.Log($"Library added with ID {libraryId}");

                // Scan the newly added library for series + episodes
                await _scannerService.ScanLibraryAsync(libraryId);
                Logger.Log($"Library {libraryId} scanned successfully");

                // Start watching for file changes
                _folderWatcher.WatchLibrary(libraryId, path);
                Logger.Log($"Now watching library {libraryId}");
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to add library '{path}': {ex.Message}");
            }
        }

        // ── Startup helpers ──────────────────────────────────────

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

        // ── Shutdown ─────────────────────────────────────────────

        private async void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            Logger.Log("MainWindow closing — shutting down");
            _folderWatcher.StopAll();
            await _playerPage.ShutdownAsync();
        }
    }
}
