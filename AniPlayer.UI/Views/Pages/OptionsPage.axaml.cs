using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;

namespace AniPlayer.UI;

public partial class OptionsPage : UserControl
{
    public event Action<string>? LibraryFolderAdded;

    public OptionsPage()
    {
        InitializeComponent();
    }

    private async void AddLibraryFolderButton_Click(object? sender, RoutedEventArgs e)
    {
        Logger.Log("[OptionsPage] Add Library Folder button clicked — opening folder picker");
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
        {
            Logger.Log("[OptionsPage] ERROR: TopLevel is null, cannot open folder picker");
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
            Logger.Log($"[OptionsPage] Folder selected: {path}");
            Logger.Log($"[OptionsPage] LibraryFolderAdded event has {(LibraryFolderAdded != null ? "subscribers" : "NO subscribers")} — invoking");
            LibraryFolderAdded?.Invoke(path);
        }
        else
        {
            Logger.Log("[OptionsPage] Folder picker cancelled by user");
        }
    }
}
