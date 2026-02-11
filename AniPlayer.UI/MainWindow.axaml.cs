using Avalonia.Controls;
using System;
using System.ComponentModel;

namespace AniPlayer.UI
{
    public partial class MainWindow : Window
    {
        private readonly HomePage _homePage;
        private readonly LibraryPage _libraryPage;
        private readonly PlayerPage _playerPage;
        private readonly ShowInfoPage _showInfoPage;
        private readonly OptionsPage _optionsPage;

        public MainWindow()
        {
            Logger.Log("MainWindow constructor called");
            InitializeComponent();

            // Create pages once — PlayerPage holds the mpv instance for its lifetime
            _homePage = new HomePage();
            _libraryPage = new LibraryPage();
            _playerPage = new PlayerPage();
            _showInfoPage = new ShowInfoPage();
            _optionsPage = new OptionsPage();

            WireEvents();
            NavigateTo("Home");

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

            // ShowInfoPage
            _showInfoPage.BackRequested += () => NavigateTo("Library");

            // PlayerPage
            _playerPage.PlaybackStopped += () => NavigateTo("Home");

            // OptionsPage
            _optionsPage.LibraryFolderAdded += path =>
            {
                Logger.Log($"Library folder added: {path}");
                // Will wire to ScannerService later
            };
        }

        // ── Shutdown ─────────────────────────────────────────────

        private async void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            Logger.Log("MainWindow closing — shutting down player");
            await _playerPage.ShutdownAsync();
        }
    }
}
