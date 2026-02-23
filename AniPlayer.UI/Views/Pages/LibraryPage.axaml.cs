using Aniplayer.Core.Constants;
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
        var cards = new List<SeriesCard>();

        foreach (var group in groups)
        {
            if (string.IsNullOrEmpty(group.Key)) continue;

            var members = group.OrderBy(s => s.SeasonNumber).ToList();
            // Pick representative: prefer the one with a cover / AniList metadata
            var representative = members.FirstOrDefault(s => s.CoverImagePath != null) ?? members[0];
            
            var card = new SeriesCard();
            card.SetData(representative, members.Count);
            card.GroupClicked += (groupName) => SeriesSelected?.Invoke(groupName);
            cards.Add(card);
        }

        SeriesGrid.ItemsSource = cards;

        var seriesCount = groups.Count;
        SeriesCountText.Text = seriesCount == 1 ? "1 series" : $"{seriesCount} series";
        
        if (seriesCount == 0)
        {
            if (string.IsNullOrEmpty(query))
            {
                EmptyStateTitle.Text = "No series in your library";
                EmptyStateDescription.Text = "Add a folder to start scanning for anime.";
            }
            else
            {
                EmptyStateTitle.Text = "No results found";
                EmptyStateDescription.Text = $"We couldn't find anything matching \"{query}\"";
            }
            EmptyState.IsVisible = true;
            SeriesGrid.IsVisible = false;
        }
        else
        {
            EmptyState.IsVisible = false;
            SeriesGrid.IsVisible = true;
        }

        Logger.Log($"[LibraryPage] ApplyFilter: query='{query}', total={_allSeries.Count}, filtered={filtered.Count}, groups={groups.Count}, cards={cards.Count}", LogRegion.UI);
        Logger.Log($"[LibraryPage] EmptyState.IsVisible={EmptyState.IsVisible}, SeriesGrid.IsVisible={SeriesGrid.IsVisible}", LogRegion.UI);
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
