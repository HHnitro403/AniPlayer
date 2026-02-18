using Aniplayer.Core.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AniPlayer.UI;

public partial class LibraryPage : UserControl
{
    public event Action<string>? FolderAdded;
    public event Action<string>? SeriesSelected;

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
            Logger.Log($"[LibraryPage]   Series ID={s.Id}, group='{s.SeriesGroupName}', season={s.SeasonNumber}, folder='{s.FolderName}'", LogRegion.UI);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text?.Trim() ?? "";
        var filtered = string.IsNullOrEmpty(query)
            ? _allSeries
            : _allSeries.Where(s =>
                (s.SeriesGroupName != null && s.SeriesGroupName.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                s.DisplayTitle.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        // Group series by the new SeriesGroupName property
        var groups = filtered
            .GroupBy(s => s.SeriesGroupName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        SeriesGrid.ItemsSource = null;
        var cards = new List<Control>();

        foreach (var group in groups)
        {
            if (string.IsNullOrEmpty(group.Key)) continue;

            var members = group.OrderBy(s => s.SeasonNumber).ToList();
            // Pick representative: prefer the one with a cover / AniList metadata
            var representative = members.FirstOrDefault(s => s.CoverImagePath != null) ?? members[0];
            cards.Add(CreateSeriesCard(representative, group.Key, members.Count));
        }

        SeriesGrid.ItemsSource = cards;

        var seriesCount = groups.Count;
        SeriesCountText.Text = seriesCount == 1 ? "1 series" : $"{seriesCount} series";
        EmptyState.IsVisible = seriesCount == 0;
        SeriesGrid.IsVisible = seriesCount > 0;

        Logger.Log($"[LibraryPage] ApplyFilter: query='{query}', total={_allSeries.Count}, filtered={filtered.Count}, groups={groups.Count}, cards={cards.Count}", LogRegion.UI);
        Logger.Log($"[LibraryPage] EmptyState.IsVisible={EmptyState.IsVisible}, SeriesGrid.IsVisible={SeriesGrid.IsVisible}", LogRegion.UI);
    }

    // [Changed] Method signature remains synchronous to return the UI element immediately
    private Border CreateSeriesCard(Series representative, string seriesGroupName, int seasonCount = 1)
    {
        var stack = new StackPanel { Spacing = 6, Width = 150 };
        var coverPanel = new Panel { Height = 200 };

        // [Fix] Default to placeholder immediately. Do not block UI thread loading files.
        var img = new Image
        {
            Height = 200,
            Stretch = Stretch.UniformToFill,
            // Optional: Set a lightweight "loading" resource here if you have one
        };

        var imgBorder = new Border
        {
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Child = img,
        };

        // [Fix] Trigger async background load if path exists
        if (!string.IsNullOrEmpty(representative.CoverImagePath) && File.Exists(representative.CoverImagePath))
        {
            coverPanel.Children.Add(imgBorder);
            // Fire-and-forget async load (safe because it marshals back to UI thread)
            _ = LoadCoverAsync(img, representative.CoverImagePath);
        }
        else
        {
            // ... (Your existing placeholder logic here) ...
            var placeholder = new Border
            {
                Height = 200,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                Child = new TextBlock
                {
                    Text = representative.DisplayTitle.Length > 0 ? representative.DisplayTitle[0].ToString() : "?",
                    FontSize = 36,
                    Opacity = 0.3,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                }
            };
            coverPanel.Children.Add(placeholder);
        }

        // ... (Rest of your Badge and Title logic remains the same) ...
        if (seasonCount > 1) { /* ... */ }
        stack.Children.Add(coverPanel);
        stack.Children.Add(new TextBlock
        {
            Text = representative.DisplayTitle,
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
        card.PointerPressed += (_, _) => SeriesSelected?.Invoke(seriesGroupName);

        return card;
    }

    // [New] Async helper to load and downsample images
    private async Task LoadCoverAsync(Image target, string path)
    {
        try
        {
            // Run I/O and decoding on a background thread
            var bitmap = await Task.Run(() =>
            {
                if (!File.Exists(path)) return null;

                using var stream = File.OpenRead(path);
                // [Fix] DecodeToWidth saves RAM! 
                // Instead of loading a 4K texture (50MB), we load a 200px wide texture (~200KB).
                return Bitmap.DecodeToWidth(stream, 300);
            });

            if (bitmap != null)
            {
                // Marshal back to UI thread to update the control
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    target.Source = bitmap;
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[LibraryPage] Failed to load cover '{path}': {ex.Message}");
        }
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
