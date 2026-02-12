using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Aniplayer.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AniPlayer.UI;

public partial class LibraryPage : UserControl
{
    public event Action<string>? FolderAdded;
    public event Action<int>? SeriesSelected;

    private List<Series> _allSeries = new();

    public LibraryPage()
    {
        InitializeComponent();
        SearchBox.PropertyChanged += (s, e) =>
        {
            if (e.Property == TextBox.TextProperty)
                ApplyFilter();
        };
    }

    public void DisplaySeries(IEnumerable<Series> series)
    {
        _allSeries = series.ToList();
        Logger.Log($"[LibraryPage] DisplaySeries called with {_allSeries.Count} series", LogRegion.UI);
        foreach (var s in _allSeries)
            Logger.Log($"[LibraryPage]   Series ID={s.Id}, display='{s.DisplayTitle}', folder='{s.FolderName}'", LogRegion.UI);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text?.Trim() ?? "";
        var filtered = string.IsNullOrEmpty(query)
            ? _allSeries
            : _allSeries.Where(s =>
                s.DisplayTitle.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        // Group series that share a base name (e.g. "High School DxD S1" + "S2" + "S3")
        var groups = filtered
            .GroupBy(s => GetBaseName(s.DisplayTitle), StringComparer.OrdinalIgnoreCase)
            .ToList();

        SeriesGrid.ItemsSource = null;
        var cards = new List<Control>();

        foreach (var group in groups)
        {
            var members = group.OrderBy(s => GetSeasonNumber(s.Path)).ToList();
            // Pick representative: prefer the one with a cover / AniList metadata
            var representative = members.FirstOrDefault(s => s.CoverImagePath != null) ?? members[0];
            var seasonCount = members.Count;
            cards.Add(CreateSeriesCard(representative, seasonCount));
        }

        SeriesGrid.ItemsSource = cards;

        var seriesCount = filtered.Count;
        SeriesCountText.Text = seriesCount == 1 ? "1 series" : $"{seriesCount} series";
        EmptyState.IsVisible = seriesCount == 0;
        SeriesGrid.IsVisible = seriesCount > 0;

        Logger.Log($"[LibraryPage] ApplyFilter: query='{query}', total={_allSeries.Count}, filtered={seriesCount}, groups={groups.Count}, cards={cards.Count}", LogRegion.UI);
        Logger.Log($"[LibraryPage] EmptyState.IsVisible={EmptyState.IsVisible}, SeriesGrid.IsVisible={SeriesGrid.IsVisible}", LogRegion.UI);
    }

    private Border CreateSeriesCard(Series series, int seasonCount = 1)
    {
        var stack = new StackPanel { Spacing = 6, Width = 150 };

        // Cover image or placeholder (wrapped in Panel for badge overlay)
        var coverPanel = new Panel { Height = 200 };

        if (!string.IsNullOrEmpty(series.CoverImagePath) && File.Exists(series.CoverImagePath))
        {
            var img = new Image
            {
                Source = new Bitmap(series.CoverImagePath),
                Height = 200,
                Stretch = Stretch.UniformToFill,
            };
            var imgBorder = new Border
            {
                CornerRadius = new CornerRadius(6),
                ClipToBounds = true,
                Child = img,
            };
            coverPanel.Children.Add(imgBorder);
        }
        else
        {
            var placeholder = new Border
            {
                Height = 200,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                Child = new TextBlock
                {
                    Text = series.DisplayTitle.Length > 0
                        ? series.DisplayTitle[0].ToString()
                        : "?",
                    FontSize = 36,
                    Opacity = 0.3,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            coverPanel.Children.Add(placeholder);
        }

        // Seasons badge overlay
        if (seasonCount > 1)
        {
            var badge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(90, 60, 180)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 6, 6, 0),
                Child = new TextBlock
                {
                    Text = $"{seasonCount} seasons",
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brushes.White,
                },
            };
            coverPanel.Children.Add(badge);
        }

        stack.Children.Add(coverPanel);

        // Title — show base name for grouped series
        var displayName = seasonCount > 1
            ? GetBaseName(series.DisplayTitle)
            : series.DisplayTitle;

        stack.Children.Add(new TextBlock
        {
            Text = displayName,
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 36,
        });

        var card = new Border
        {
            Padding = new Thickness(8),
            Margin = new Thickness(4),
            CornerRadius = new CornerRadius(8),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = stack,
        };

        var seriesId = series.Id;
        card.PointerPressed += (_, _) => SeriesSelected?.Invoke(seriesId);

        return card;
    }

    /// <summary>
    /// Strips season suffixes to get the base series name for grouping.
    /// "High School DxD S2" → "High School DxD"
    /// "Attack on Titan Season 3" → "Attack on Titan"
    /// </summary>
    private static string GetBaseName(string title)
    {
        var cleaned = Regex.Replace(title, @"\s+S(?:eason\s*)?\d+\s*$", "", RegexOptions.IgnoreCase);
        return cleaned.Trim();
    }

    /// <summary>
    /// Extracts a season number from a series path for sort ordering.
    /// ".../High School DxD S2" → 2
    /// </summary>
    private static int GetSeasonNumber(string path)
    {
        var folder = System.IO.Path.GetFileName(path.TrimEnd(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar));
        var match = Regex.Match(folder ?? "", @"S(?:eason\s*)?(\d+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var num) ? num : 0;
    }

    private async void AddFolderButton_Click(object? sender, RoutedEventArgs e)
    {
        Logger.Log("[LibraryPage] Add Folder button clicked — opening folder picker");
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
        {
            Logger.Log("[LibraryPage] ERROR: TopLevel is null, cannot open folder picker");
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Anime Library Folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            var path = folders[0].Path.LocalPath;
            Logger.Log($"[LibraryPage] Folder selected: {path}");
            Logger.Log($"[LibraryPage] FolderAdded event has {(FolderAdded != null ? "subscribers" : "NO subscribers")} — invoking");
            FolderAdded?.Invoke(path);
        }
        else
        {
            Logger.Log("[LibraryPage] Folder picker cancelled by user");
        }
    }
}
