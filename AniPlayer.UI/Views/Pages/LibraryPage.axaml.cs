using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;

namespace AniPlayer.UI;

public partial class LibraryPage : UserControl
{
    public event Action<string>? FolderAdded;
    public event Action<int>? SeriesSelected;

    public LibraryPage()
    {
        InitializeComponent();
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
