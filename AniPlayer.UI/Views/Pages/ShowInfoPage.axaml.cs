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
using System.IO;
using System.Linq;

namespace AniPlayer.UI;

public partial class ShowInfoPage : UserControl
{
    public event Action? BackRequested;
    public event Action<string>? EpisodePlayRequested;
    public event Action<int>? MetadataRefreshRequested;

    private int _seriesId;
    private List<Episode> _allEpisodes = new();

    public ShowInfoPage()
    {
        InitializeComponent();
        EpisodeTypeFilter.SelectionChanged += (_, _) => ApplyEpisodeFilter();
    }

    public void LoadSeriesData(Series series, IEnumerable<Episode> episodes)
    {
        _seriesId = series.Id;
        _allEpisodes = episodes.ToList();

        Logger.Log($"[ShowInfoPage] LoadSeriesData: series ID={series.Id}, title='{series.DisplayTitle}', episodes={_allEpisodes.Count}", LogRegion.UI);

        // Header info
        TitleText.Text = series.DisplayTitle;
        AlternateTitleText.Text = series.TitleRomaji != null && series.TitleRomaji != series.TitleEnglish
            ? series.TitleRomaji : "";
        AlternateTitleText.IsVisible = !string.IsNullOrEmpty(AlternateTitleText.Text);

        EpisodeCountText.Text = _allEpisodes.Count == 1 ? "1 episode" : $"{_allEpisodes.Count} episodes";
        StatusBadge.Text = series.Status ?? "";
        ScoreText.Text = series.AverageScore.HasValue ? $"Score: {series.AverageScore:0.#}" : "";
        SynopsisText.Text = series.Synopsis ?? "";
        SynopsisText.IsVisible = !string.IsNullOrEmpty(series.Synopsis);

        // Cover image
        if (!string.IsNullOrEmpty(series.CoverImagePath) && File.Exists(series.CoverImagePath))
        {
            CoverImage.Source = new Bitmap(series.CoverImagePath);
            CoverPlaceholder.IsVisible = false;
        }
        else
        {
            CoverImage.Source = null;
            CoverPlaceholder.IsVisible = true;
        }

        // Genres
        GenresPanel.Children.Clear();
        if (!string.IsNullOrEmpty(series.Genres))
        {
            foreach (var genre in series.Genres.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
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

        ApplyEpisodeFilter();
    }

    private void ApplyEpisodeFilter()
    {
        var selectedIndex = EpisodeTypeFilter.SelectedIndex;
        var filtered = selectedIndex switch
        {
            1 => _allEpisodes.Where(e => e.EpisodeType == EpisodeTypes.Episode).ToList(),
            2 => _allEpisodes.Where(e => e.EpisodeType == EpisodeTypes.Special).ToList(),
            3 => _allEpisodes.Where(e => e.EpisodeType == EpisodeTypes.Ova).ToList(),
            _ => _allEpisodes,
        };

        EpisodeListPanel.Children.Clear();

        foreach (var ep in filtered)
            EpisodeListPanel.Children.Add(CreateEpisodeRow(ep));

        Logger.Log($"[ShowInfoPage] ApplyEpisodeFilter: filterIndex={selectedIndex}, showing {filtered.Count}/{_allEpisodes.Count} episodes", LogRegion.UI);
    }

    private Border CreateEpisodeRow(Episode ep)
    {
        var epLabel = new TextBlock
        {
            Text = ep.DisplayName,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var titleLabel = new TextBlock
        {
            Text = ep.Title ?? "",
            FontSize = 12,
            Opacity = 0.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var textStack = new StackPanel { Spacing = 2 };
        textStack.Children.Add(epLabel);
        if (!string.IsNullOrEmpty(ep.Title))
            textStack.Children.Add(titleLabel);

        var playBtn = new Button
        {
            Content = "Play",
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(12, 6),
        };

        var filePath = ep.FilePath;
        playBtn.Click += (_, _) => EpisodePlayRequested?.Invoke(filePath);

        var grid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
        };
        grid.Children.Add(textStack);
        Grid.SetColumn(playBtn, 1);
        grid.Children.Add(playBtn);

        var row = new Border
        {
            Padding = new Thickness(12, 10),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = grid,
        };

        row.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
                EpisodePlayRequested?.Invoke(filePath);
        };

        return row;
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
            if (metadata != null)
            {
                await metadata.ApplyMetadataToSeriesAsync(_seriesId);
                MetadataRefreshRequested?.Invoke(_seriesId);
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
