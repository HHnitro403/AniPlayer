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

        SeriesGrid.ItemsSource = null;
        var cards = new List<Control>();

        foreach (var s in filtered)
            cards.Add(CreateSeriesCard(s));

        SeriesGrid.ItemsSource = cards;

        SeriesCountText.Text = filtered.Count == 1 ? "1 series" : $"{filtered.Count} series";
        EmptyState.IsVisible = filtered.Count == 0;
        SeriesGrid.IsVisible = filtered.Count > 0;

        Logger.Log($"[LibraryPage] ApplyFilter: query='{query}', total={_allSeries.Count}, filtered={filtered.Count}, cards={cards.Count}", LogRegion.UI);
        Logger.Log($"[LibraryPage] EmptyState.IsVisible={EmptyState.IsVisible}, SeriesGrid.IsVisible={SeriesGrid.IsVisible}", LogRegion.UI);
    }

    private Border CreateSeriesCard(Series series)
    {
        var stack = new StackPanel { Spacing = 6, Width = 150 };

        // Cover image or placeholder
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
            stack.Children.Add(imgBorder);
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
            stack.Children.Add(placeholder);
        }

        // Title
        stack.Children.Add(new TextBlock
        {
            Text = series.DisplayTitle,
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
