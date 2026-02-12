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

public partial class HomePage : UserControl
{
    public event Action<string>? PlayFileRequested;
    public event Action? AddLibraryRequested;
    public event Action<int>? SeriesSelected;
    public event Action<int>? ResumeEpisodeRequested;

    public HomePage()
    {
        InitializeComponent();
    }

    public void DisplayContinueWatching(IEnumerable<(Episode Episode, WatchProgress Progress, Series? Series)> items)
    {
        var list = items.ToList();
        Logger.Log($"[HomePage] DisplayContinueWatching: {list.Count} items", LogRegion.UI);

        ContinueWatchingList.ItemsSource = null;
        var cards = new List<Control>();

        foreach (var (ep, progress, series) in list)
            cards.Add(CreateContinueWatchingCard(ep, progress, series));

        ContinueWatchingList.ItemsSource = cards;
        ContinueWatchingEmpty.IsVisible = list.Count == 0;
        ContinueWatchingList.IsVisible = list.Count > 0;
    }

    public void DisplayRecentlyAdded(IEnumerable<Series> series)
    {
        var list = series.ToList();
        Logger.Log($"[HomePage] DisplayRecentlyAdded: {list.Count} series", LogRegion.UI);

        RecentlyAddedList.ItemsSource = null;
        var cards = new List<Control>();

        foreach (var s in list)
            cards.Add(CreateSeriesCard(s));

        RecentlyAddedList.ItemsSource = cards;
        RecentlyAddedEmpty.IsVisible = list.Count == 0;
        RecentlyAddedList.IsVisible = list.Count > 0;
    }

    public void SetHasLibraries(bool hasLibraries)
    {
        Logger.Log($"[HomePage] SetHasLibraries: {hasLibraries}", LogRegion.UI);
        QuickActionsSection.IsVisible = !hasLibraries;
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

    private Border CreateContinueWatchingCard(Episode episode, WatchProgress progress, Series? series)
    {
        var stack = new StackPanel { Spacing = 4, Width = 180 };

        // Cover with progress bar overlay
        var coverPanel = new Panel { Height = 110 };

        if (series != null && !string.IsNullOrEmpty(series.CoverImagePath) && File.Exists(series.CoverImagePath))
        {
            var img = new Image
            {
                Source = new Bitmap(series.CoverImagePath),
                Height = 110,
                Stretch = Stretch.UniformToFill,
            };
            coverPanel.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(6),
                ClipToBounds = true,
                Child = img,
            });
        }
        else
        {
            coverPanel.Children.Add(new Border
            {
                Height = 110,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                Child = new TextBlock
                {
                    Text = series?.DisplayTitle.Length > 0
                        ? series.DisplayTitle[0].ToString()
                        : "?",
                    FontSize = 28,
                    Opacity = 0.3,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });
        }

        // Timestamp badge (top-right) — e.g. "12:34 left"
        var remaining = (progress.DurationSeconds ?? 0) - progress.PositionSeconds;
        if (remaining > 0)
        {
            var ts = TimeSpan.FromSeconds(remaining);
            var timeText = ts.Hours > 0 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
            coverPanel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 6, 6, 0),
                Child = new TextBlock
                {
                    Text = $"{timeText} left",
                    FontSize = 10,
                    Foreground = Brushes.White,
                },
            });
        }

        // Progress bar at bottom of cover (proportional grid)
        var progressPercent = progress.ProgressPercent;
        var progressGrid = new Grid
        {
            Height = 3,
            VerticalAlignment = VerticalAlignment.Bottom,
            ColumnDefinitions = new ColumnDefinitions($"{progressPercent:F4}*,{(1 - progressPercent):F4}*"),
        };
        var fillBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(90, 60, 180)),
            CornerRadius = new CornerRadius(0, 0, 0, 6),
        };
        Grid.SetColumn(fillBar, 0);
        var emptyBar = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            CornerRadius = new CornerRadius(0, 0, 6, 0),
        };
        Grid.SetColumn(emptyBar, 1);
        progressGrid.Children.Add(fillBar);
        progressGrid.Children.Add(emptyBar);
        coverPanel.Children.Add(progressGrid);

        stack.Children.Add(coverPanel);

        // Episode name
        stack.Children.Add(new TextBlock
        {
            Text = episode.DisplayName,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        });

        // Series title (smaller, dimmed)
        if (series != null)
        {
            stack.Children.Add(new TextBlock
            {
                Text = series.DisplayTitle,
                FontSize = 11,
                Opacity = 0.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
            });
        }

        var card = new Border
        {
            Padding = new Thickness(6),
            Margin = new Thickness(4),
            CornerRadius = new CornerRadius(8),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = stack,
        };

        var episodeId = episode.Id;
        card.PointerPressed += (_, _) => ResumeEpisodeRequested?.Invoke(episodeId);

        return card;
    }

    private async void OpenFileButton_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Video File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Video Files")
                {
                    Patterns = new[] { "*.mkv", "*.mp4", "*.avi", "*.m4v", "*.mov", "*.wmv" }
                }
            }
        });

        if (files.Count > 0)
        {
            PlayFileRequested?.Invoke(files[0].Path.LocalPath);
        }
    }

    private void AddLibraryButton_Click(object? sender, RoutedEventArgs e)
    {
        AddLibraryRequested?.Invoke();
    }
}
