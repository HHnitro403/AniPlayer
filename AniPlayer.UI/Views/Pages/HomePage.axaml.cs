using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
using System.Linq;

namespace AniPlayer.UI;

public partial class HomePage : UserControl
{
    public event Action<string>? PlayFileRequested;
    public event Action? AddLibraryRequested;

    public HomePage()
    {
        InitializeComponent();
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
