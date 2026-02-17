using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Aniplayer.Core.Constants;
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

    public class SeasonGroup
    {
        public string Header { get; set; } = string.Empty;
        public List<Episode> Episodes { get; set; } = new();
        public bool IsExpanded { get; set; } = true;
    }

    public ObservableCollection<SeasonGroup> SeasonGroups { get; } = new();

    public ShowInfoPage()
    {
        InitializeComponent();
        SeasonListControl.ItemsSource = SeasonGroups;
    }

    private void OnEpisodePlayRequest(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: Episode episode })
        {
            EpisodePlayRequested?.Invoke(episode.FilePath);
        }
    }

    public void LoadSeriesData(List<Series> seriesGroup, List<Episode> allEpisodes)
    {
        _seriesList = seriesGroup; // Store the list for the refresh button
        var sortedSeries = seriesGroup.OrderBy(s => s.SeasonNumber == 0 ? 999 : s.SeasonNumber).ToList();
        var representative = sortedSeries.FirstOrDefault();
        if (representative == null) return;

        Logger.Log($"[ShowInfoPage] LoadSeriesData: group='{representative.SeriesGroupName}', seasons={sortedSeries.Count}, episodes={allEpisodes.Count}", LogRegion.UI);

        // Header info (from representative series)
        TitleText.Text = representative.SeriesGroupName;
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
                    Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                    Child = new TextBlock { Text = genre, FontSize = 12 },
                });
            }
        }

        // Group episodes into seasons
        SeasonGroups.Clear();
        var episodesBySeason = allEpisodes.GroupBy(e => e.SeriesId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var series in sortedSeries)
        {
            if (episodesBySeason.TryGetValue(series.Id, out var episodes))
            {
                var header = series.SeasonNumber == 0 ? "Specials / OVAs" : $"Season {series.SeasonNumber}";
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
            Logger.Log($"[ShowInfoPage] Metadata refresh failed: {ex.Message}", LogRegion.UI);
        }
        finally
        {
            RefreshMetadataButton.Content = "Refresh Metadata";
            RefreshMetadataButton.IsEnabled = true;
        }
    }
}
