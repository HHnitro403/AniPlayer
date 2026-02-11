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

    public HomePage()
    {
        InitializeComponent();
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
