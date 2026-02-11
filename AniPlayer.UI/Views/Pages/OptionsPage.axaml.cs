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
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Anime Library Folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            LibraryFolderAdded?.Invoke(folders[0].Path.LocalPath);
        }
    }
}
