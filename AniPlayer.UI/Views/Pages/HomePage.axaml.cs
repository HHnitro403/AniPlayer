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
using System.Globalization;
using System.Linq;
using Aniplayer.Core.Constants;

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
        var cards = new List<SeriesCard>();

        foreach (var (ep, progress, series) in list)
        {
            var card = new SeriesCard();
            card.SetContinueWatchingData(ep, progress, series);
            card.Clicked += (id) => ResumeEpisodeRequested?.Invoke(id);
            cards.Add(card);
        }

        ContinueWatchingList.ItemsSource = cards;
        ContinueWatchingEmpty.IsVisible = list.Count == 0;
        ContinueWatchingList.IsVisible = list.Count > 0;
    }

    public void DisplayRecentlyAdded(IEnumerable<Series> series)
    {
        var list = series.ToList();
        Logger.Log($"[HomePage] DisplayRecentlyAdded: {list.Count} series", LogRegion.UI);

        RecentlyAddedList.ItemsSource = null;
        var cards = new List<SeriesCard>();

        foreach (var s in list)
        {
            var card = new SeriesCard();
            card.SetData(s);
            card.Clicked += (id) => SeriesSelected?.Invoke(id);
            cards.Add(card);
        }

        RecentlyAddedList.ItemsSource = cards;
        RecentlyAddedEmpty.IsVisible = list.Count == 0;
        RecentlyAddedList.IsVisible = list.Count > 0;
    }

    public void SetHasLibraries(bool hasLibraries)
    {
        Logger.Log($"[HomePage] SetHasLibraries: {hasLibraries}", LogRegion.UI);
        QuickActionsSection.IsVisible = !hasLibraries;
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
