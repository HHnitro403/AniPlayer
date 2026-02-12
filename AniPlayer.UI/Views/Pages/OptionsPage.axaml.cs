using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Aniplayer.Core.Interfaces;
using Aniplayer.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;

namespace AniPlayer.UI;

public partial class OptionsPage : UserControl
{
    public event Action<string>? LibraryFolderAdded;
    public event Action<int>? LibraryRemoveRequested;

    private readonly ISettingsService _settings;

    public OptionsPage()
    {
        InitializeComponent();
        _settings = App.Services.GetRequiredService<ISettingsService>();
        _ = LoadSettingsAsync();
        VsyncToggle.IsCheckedChanged += OnVsyncToggleChanged;
    }

    private async System.Threading.Tasks.Task LoadSettingsAsync()
    {
        VsyncToggle.IsChecked = await _settings.GetBoolAsync("vsync", false);
    }

    private void OnVsyncToggleChanged(object? sender, RoutedEventArgs e)
    {
        _ = _settings.SetAsync("vsync", VsyncToggle.IsChecked == true ? "1" : "0");
    }

    public void DisplayLibraries(IEnumerable<Library> libraries)
    {
        Logger.Log("[OptionsPage] DisplayLibraries called");
        LibraryFoldersList.Children.Clear();

        var count = 0;
        foreach (var lib in libraries)
        {
            count++;
            var row = CreateLibraryRow(lib);
            LibraryFoldersList.Children.Add(row);
        }

        Logger.Log($"[OptionsPage] Displayed {count} library folder(s)");
    }

    private Border CreateLibraryRow(Library lib)
    {
        var labelText = new TextBlock
        {
            Text = lib.Label ?? System.IO.Path.GetFileName(lib.Path.TrimEnd(
                System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar)),
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var pathText = new TextBlock
        {
            Text = lib.Path,
            FontSize = 12,
            Opacity = 0.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var textStack = new StackPanel { Spacing = 2 };
        textStack.Children.Add(labelText);
        textStack.Children.Add(pathText);

        var removeBtn = new Button
        {
            Content = "Remove",
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Avalonia.Thickness(8, 4),
        };

        var libId = lib.Id;
        removeBtn.Click += (_, _) => LibraryRemoveRequested?.Invoke(libId);

        var grid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
        };
        grid.Children.Add(textStack);
        Grid.SetColumn(removeBtn, 1);
        grid.Children.Add(removeBtn);

        return new Border
        {
            Padding = new Avalonia.Thickness(12, 8),
            CornerRadius = new Avalonia.CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
            Child = grid,
        };
    }

    private async void FetchAllMetadata_Click(object? sender, RoutedEventArgs e)
    {
        FetchAllMetadataButton.IsEnabled = false;
        FetchAllMetadataButton.Content = "Fetching...";
        FetchStatusText.Text = "Fetching metadata for series without data...";
        try
        {
            var metadata = App.Services.GetRequiredService<IMetadataService>();
            await metadata.FetchAllMissingMetadataAsync();
            FetchStatusText.Text = "Done — metadata fetched for all series.";
        }
        catch (Exception ex)
        {
            Logger.Log($"[OptionsPage] Batch metadata fetch failed: {ex.Message}");
            FetchStatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            FetchAllMetadataButton.Content = "Fetch All Missing Metadata";
            FetchAllMetadataButton.IsEnabled = true;
        }
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
