using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Aniplayer.Core.Constants;
using Aniplayer.Core.Helpers;
using Aniplayer.Core.Interfaces;
using Aniplayer.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AniPlayer.UI;

public partial class ShowInfoPage : UserControl
{
    public event Action? BackRequested;
    public event Action<string>? EpisodePlayRequested;
    public event Action? MetadataRefreshRequested;

    private List<Series> _seriesList = new();
    private List<Episode> _allEpisodes = new();
    private ILibraryService? _libraryService;
    private IWatchProgressService? _watchProgressService;
    private string? _selectedSubtitleFilePath;

    public class SeasonGroup
    {
        public string Header { get; set; } = string.Empty;
        public List<Episode> Episodes { get; set; } = new();
        public bool IsExpanded { get; set; } = true;
    }

    public class SubtitleOverride
    {
        public int EpisodeId { get; set; }
        public string DisplayText { get; set; } = string.Empty;
    }

    public ObservableCollection<SeasonGroup> SeasonGroups { get; } = new();
    public ObservableCollection<SubtitleOverride> SubtitleOverrides { get; } = new();

    public ShowInfoPage()
    {
        InitializeComponent();
        SeasonListControl.ItemsSource = SeasonGroups;
        OverridesList.ItemsSource = SubtitleOverrides;
        _libraryService = App.Services.GetService<ILibraryService>();
        _watchProgressService = App.Services.GetService<IWatchProgressService>();
    }

    private void OnEpisodePlayRequest(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: Episode episode })
        {
            EpisodePlayRequested?.Invoke(episode.FilePath);
        }
    }

    public async void LoadSeriesData(List<Series> seriesGroup, List<Episode> allEpisodes)
    {
        _seriesList = seriesGroup; // Store the list for the refresh button
        _allEpisodes = allEpisodes;
        var sortedSeries = seriesGroup.OrderBy(s => s.SeasonNumber == 0 ? 999 : s.SeasonNumber).ToList();
        var representative = sortedSeries.FirstOrDefault();
        if (representative == null) return;

        Logger.Log($"[ShowInfoPage] LoadSeriesData: group='{representative.SeriesGroupName}', seasons={sortedSeries.Count}, episodes={allEpisodes.Count}", LogRegion.UI);

        // Load track preferences
        await LoadTrackPreferencesAsync(representative.Id);

        // Header info (from representative series)
        TitleText.Text = representative.DisplayTitle;
        AlternateTitleText.Text = ""; // This might need adjustment if alternate titles are per-season
        AlternateTitleText.IsVisible = false;
        RefreshMetadataButton.IsVisible = true;

        EpisodeCountText.Text = allEpisodes.Count == 1 ? "1 episode" : $"{allEpisodes.Count} episodes";
        StatusBadge.Text = representative.Status ?? "";
        ScoreText.Text = representative.AverageScore.HasValue ? $"Score: {representative.AverageScore:0.#}" : "";
        SynopsisText.Text = representative.Synopsis ?? "";
        SynopsisText.IsVisible = !string.IsNullOrEmpty(representative.Synopsis);

        // Cover image
        if (!string.IsNullOrEmpty(representative.CoverImagePath) && File.Exists(representative.CoverImagePath))
        {
            CoverImage.Source = new Bitmap(representative.CoverImagePath);
            CoverPlaceholder.IsVisible = false;
        }
        else
        {
            CoverImage.Source = null;
            CoverPlaceholder.IsVisible = true;
        }

        // Genres
        GenresPanel.Children.Clear();
        if (!string.IsNullOrEmpty(representative.Genres))
        {
            foreach (var genre in representative.Genres.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                GenresPanel.Children.Add(new Border
                {
                    Padding = new Thickness(8, 4),
                    Margin = new Thickness(0, 0, 6, 6),
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(Color.Parse("#2A2A5A")), // AccentSubtle
                    Child = new TextBlock { Text = genre, FontSize = 12, Foreground = Brushes.White },
                });
            }
        }

        // Fetch watch progress for all episodes in this series group
        if (_watchProgressService != null)
        {
            try
            {
                foreach (var series in sortedSeries)
                {
                    var progressList = await _watchProgressService.GetProgressForSeriesAsync(series.Id);
                    var progressDict = progressList.ToDictionary(p => p.EpisodeId);

                    foreach (var episode in allEpisodes.Where(e => e.SeriesId == series.Id))
                    {
                        if (progressDict.TryGetValue(episode.Id, out var progress))
                        {
                            episode.Progress = progress;
                        }
                    }
                }
                Logger.Log($"[ShowInfoPage] Loaded progress for {allEpisodes.Count(e => e.Progress != null)} episodes", LogRegion.UI);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ShowInfoPage] Failed to load progress: {ex.Message}", LogRegion.UI);
            }
        }

        // Group episodes into seasons
        SeasonGroups.Clear();
        var episodesBySeason = allEpisodes.GroupBy(e => e.SeriesId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var series in sortedSeries)
        {
            if (episodesBySeason.TryGetValue(series.Id, out var episodes))
            {
                string header;
                if (series.SeasonNumber == 0)
                    header = "Specials / OVAs";
                else if (EpisodeParser.TryParseSeasonFromFolder(series.FolderName, out _))
                    header = $"Season {series.SeasonNumber}";
                else
                    header = series.FolderName; // Non-standard name (e.g. "New", "BorN", "Hero")
                SeasonGroups.Add(new SeasonGroup
                {
                    Header = header,
                    Episodes = episodes.OrderBy(e => e.EpisodeNumber).ToList(),
                    IsExpanded = true // All seasons expanded by default
                });
            }
        }
    }


    private void BackButton_Click(object? sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke();
    }

    private async void RefreshMetadata_Click(object? sender, RoutedEventArgs e)
    {
        RefreshMetadataButton.IsEnabled = false;
        RefreshMetadataButton.Content = "Fetching...";
        try
        {
            var metadata = App.Services.GetService<IMetadataService>();
            var libraryService = App.Services.GetService<ILibraryService>(); // Get library service

            if (metadata != null && libraryService != null && _seriesList.Any())
            {
                // Refresh metadata for all series in this group
                foreach (var series in _seriesList)
                {
                    await metadata.ApplyMetadataToSeriesAsync(series.Id);
                }

                // After updating metadata, refresh the page's own data from the DB
                var seriesGroupName = _seriesList.First().SeriesGroupName;
                var refreshedSeriesGroup = (await libraryService.GetSeriesByGroupNameAsync(seriesGroupName)).ToList();

                var refreshedAllEpisodes = new List<Episode>();
                foreach (var series in refreshedSeriesGroup.OrderBy(s => s.SeasonNumber))
                {
                    var episodes = (await libraryService.GetEpisodesBySeriesIdAsync(series.Id)).ToList();
                    refreshedAllEpisodes.AddRange(episodes.OrderBy(e => e.EpisodeNumber));
                }

                // Now call LoadSeriesData with the refreshed data to update UI
                LoadSeriesData(refreshedSeriesGroup, refreshedAllEpisodes);

                // Notify the main window to refresh all pages (e.g. for library page title updates)
                MetadataRefreshRequested?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[ShowInfoPage] Metadata refresh FAILED: {ex.GetType().Name}: {ex.Message}");
            Logger.LogError("Metadata refresh exception", ex);
            MainWindow.ShowToast($"Refresh failed: {ex.Message}", true);
        }
        finally
        {
            RefreshMetadataButton.Content = "Refresh Metadata";
            RefreshMetadataButton.IsEnabled = true;
        }
    }

    private async Task LoadTrackPreferencesAsync(int representativeSeriesId)
    {
        if (_libraryService == null) return;

        try
        {
            var prefs = await _libraryService.GetSeriesTrackPreferenceAsync(representativeSeriesId);
            if (prefs != null)
            {
                AudioPreferenceText.Text = !string.IsNullOrEmpty(prefs.PreferredAudioLanguage)
                    ? $"{prefs.PreferredAudioLanguage}" + (!string.IsNullOrEmpty(prefs.PreferredAudioTitle) ? $" — {prefs.PreferredAudioTitle}" : "")
                    : "Auto (not set)";

                SubtitlePreferenceText.Text = !string.IsNullOrEmpty(prefs.PreferredSubtitleLanguage)
                    ? $"{prefs.PreferredSubtitleLanguage}" + (!string.IsNullOrEmpty(prefs.PreferredSubtitleName) ? $" — {prefs.PreferredSubtitleName}" : "")
                    : "Auto (not set)";
            }
            else
            {
                AudioPreferenceText.Text = "Auto (not set)";
                SubtitlePreferenceText.Text = "Auto (not set)";
            }

            // Load subtitle overrides
            RefreshSubtitleOverridesList();
        }
        catch (Exception ex)
        {
            Logger.Log($"[ShowInfoPage] Failed to load track preferences: {ex.Message}", LogRegion.UI);
        }
    }

    private async void ResetAudioPreference_Click(object? sender, RoutedEventArgs e)
    {
        if (_libraryService == null || !_seriesList.Any()) return;

        try
        {
            var seriesId = _seriesList.First().Id;
            await _libraryService.UpsertSeriesAudioPreferenceAsync(seriesId, "", null, null);
            AudioPreferenceText.Text = "Auto (not set)";
        }
        catch (Exception ex)
        {
            Logger.Log($"[ShowInfoPage] Failed to reset audio preference: {ex.Message}", LogRegion.UI);
        }
    }

    private async void ResetSubtitlePreference_Click(object? sender, RoutedEventArgs e)
    {
        if (_libraryService == null || !_seriesList.Any()) return;

        try
        {
            var seriesId = _seriesList.First().Id;
            await _libraryService.UpsertSeriesSubtitlePreferenceAsync(seriesId, "", null);
            SubtitlePreferenceText.Text = "Auto (not set)";
        }
        catch (Exception ex)
        {
            Logger.Log($"[ShowInfoPage] Failed to reset subtitle preference: {ex.Message}", LogRegion.UI);
        }
    }

    private void ManualSubtitleToggle_Changed(object? sender, RoutedEventArgs e)
    {
        ManualSubtitlePanel.IsVisible = ManualSubtitleToggle.IsChecked == true;

        if (ManualSubtitlePanel.IsVisible)
        {
            // Populate episode selector
            OverrideEpisodeSelector.Items.Clear();
            foreach (var episode in _allEpisodes)
            {
                OverrideEpisodeSelector.Items.Add(new ComboBoxItem
                {
                    Content = $"{episode.DisplayName} - {Path.GetFileName(episode.FilePath)}",
                    Tag = episode.Id
                });
            }

            if (OverrideEpisodeSelector.Items.Count > 0)
                OverrideEpisodeSelector.SelectedIndex = 0;

            RefreshSubtitleOverridesList();
        }
    }

    private void OverrideEpisodeSelector_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (OverrideEpisodeSelector.SelectedItem is ComboBoxItem item && item.Tag is int episodeId)
        {
            var episode = _allEpisodes.FirstOrDefault(ep => ep.Id == episodeId);
            if (episode != null && !string.IsNullOrEmpty(episode.ExternalSubtitlePath))
            {
                _selectedSubtitleFilePath = episode.ExternalSubtitlePath;
                SelectedSubtitlePath.Text = Path.GetFileName(episode.ExternalSubtitlePath);
                SaveSubtitleOverrideButton.IsEnabled = true;
            }
            else
            {
                _selectedSubtitleFilePath = null;
                SelectedSubtitlePath.Text = "No file selected";
                SaveSubtitleOverrideButton.IsEnabled = false;
            }
        }
    }

    private async void BrowseSubtitle_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Select Subtitle File",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Subtitle Files")
                    {
                        Patterns = new[] { "*.srt", "*.ass", "*.ssa", "*.vtt", "*.sub" }
                    },
                    new Avalonia.Platform.Storage.FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
                }
            };

            var window = TopLevel.GetTopLevel(this) as Window;
            if (window != null)
            {
                var result = await window.StorageProvider.OpenFilePickerAsync(dialog);
                if (result.Count > 0)
                {
                    _selectedSubtitleFilePath = result[0].Path.LocalPath;
                    SelectedSubtitlePath.Text = Path.GetFileName(_selectedSubtitleFilePath);
                    SaveSubtitleOverrideButton.IsEnabled = true;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[ShowInfoPage] Failed to browse subtitle file: {ex.Message}", LogRegion.UI);
        }
    }

    private async void SaveSubtitleOverride_Click(object? sender, RoutedEventArgs e)
    {
        if (_libraryService == null || OverrideEpisodeSelector.SelectedItem is not ComboBoxItem item || item.Tag is not int episodeId)
            return;

        if (string.IsNullOrEmpty(_selectedSubtitleFilePath))
        {
            SelectedSubtitlePath.Text = "Please select a subtitle file first";
            return;
        }

        try
        {
            await _libraryService.SetEpisodeExternalSubtitleAsync(episodeId, _selectedSubtitleFilePath);
            SelectedSubtitlePath.Text = $"✓ Saved: {Path.GetFileName(_selectedSubtitleFilePath)}";

            // Update local episode list
            var episode = _allEpisodes.FirstOrDefault(ep => ep.Id == episodeId);
            if (episode != null)
                episode.ExternalSubtitlePath = _selectedSubtitleFilePath;

            RefreshSubtitleOverridesList();
        }
        catch (Exception ex)
        {
            Logger.Log($"[ShowInfoPage] Failed to save subtitle override: {ex.Message}", LogRegion.UI);
            SelectedSubtitlePath.Text = $"Error: {ex.Message}";
        }
    }

    private async void ClearSubtitleOverride_Click(object? sender, RoutedEventArgs e)
    {
        if (_libraryService == null || OverrideEpisodeSelector.SelectedItem is not ComboBoxItem item || item.Tag is not int episodeId)
            return;

        try
        {
            await _libraryService.SetEpisodeExternalSubtitleAsync(episodeId, null);
            _selectedSubtitleFilePath = null;
            SelectedSubtitlePath.Text = "Override cleared";

            // Update local episode list
            var episode = _allEpisodes.FirstOrDefault(ep => ep.Id == episodeId);
            if (episode != null)
                episode.ExternalSubtitlePath = null;

            RefreshSubtitleOverridesList();
        }
        catch (Exception ex)
        {
            Logger.Log($"[ShowInfoPage] Failed to clear subtitle override: {ex.Message}", LogRegion.UI);
        }
    }

    private async void RemoveOverride_Click(object? sender, RoutedEventArgs e)
    {
        if (_libraryService == null || sender is not Button btn || btn.Tag is not int episodeId)
            return;

        try
        {
            await _libraryService.SetEpisodeExternalSubtitleAsync(episodeId, null);

            // Update local episode list
            var episode = _allEpisodes.FirstOrDefault(ep => ep.Id == episodeId);
            if (episode != null)
                episode.ExternalSubtitlePath = null;

            RefreshSubtitleOverridesList();
        }
        catch (Exception ex)
        {
            Logger.Log($"[ShowInfoPage] Failed to remove override: {ex.Message}", LogRegion.UI);
        }
    }

    private void RefreshSubtitleOverridesList()
    {
        SubtitleOverrides.Clear();
        foreach (var episode in _allEpisodes.Where(ep => !string.IsNullOrEmpty(ep.ExternalSubtitlePath)))
        {
            SubtitleOverrides.Add(new SubtitleOverride
            {
                EpisodeId = episode.Id,
                DisplayText = $"{episode.DisplayName} → {Path.GetFileName(episode.ExternalSubtitlePath)}"
            });
        }
    }
}
