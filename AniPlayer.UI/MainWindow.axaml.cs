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
            _libraryPage.FolderAdded += path =>
            {
                Logger.Log($"[MainWindow] LibraryPage.FolderAdded event received: {path}");
                _ = AddLibraryAsync(path, "LibraryPage");
            };

            // ShowInfoPage
            _showInfoPage.BackRequested += () => NavigateTo("Library");

            // PlayerPage
            _playerPage.PlaybackStopped += () => NavigateTo("Home");

            // OptionsPage
            _optionsPage.LibraryFolderAdded += path =>
            {
                Logger.Log($"[MainWindow] OptionsPage.LibraryFolderAdded event received: {path}");
                _ = AddLibraryAsync(path, "OptionsPage");
            };

            // ScannerService — pipe scan progress into debug.log
            _scannerService.ScanProgress += msg => Logger.Log($"[Scanner] {msg}");

            // FolderWatcher — re-scan when files change on disk
            _folderWatcher.LibraryChanged += libraryId =>
            {
                Logger.Log($"[FolderWatcher] Library {libraryId} changed on disk, triggering re-scan");
                _ = _scannerService.ScanLibraryAsync(libraryId);
            };
        }

        // ── Add Library (shared by LibraryPage + OptionsPage) ────

        private async Task AddLibraryAsync(string path, string source)
        {
            Logger.Log($"[AddLibrary] === START (source: {source}) ===");
            Logger.Log($"[AddLibrary] Path: {path}");

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
