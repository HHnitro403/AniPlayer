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
using Aniplayer.Core.Constants;

namespace AniPlayer.UI;

public partial class OptionsPage : UserControl
{
    public event Action<string>? LibraryFolderAdded;
    public event Action<int>? LibraryRemoveRequested;

    private readonly ISettingsService _settings;
    private bool _isProgrammaticChange; // Prevent event feedback loops

    public OptionsPage()
    {
        InitializeComponent();
        _settings = App.Services.GetRequiredService<ISettingsService>();
        _ = LoadSettingsAsync();
        
        VsyncToggle.IsCheckedChanged += OnVsyncToggleChanged;
        MasterLogToggle.IsCheckedChanged += OnMasterLogToggleChanged;
        ScannerLogToggle.IsCheckedChanged += OnLogRegionToggleChanged;
        ParserLogToggle.IsCheckedChanged += OnLogRegionToggleChanged;
        UiLogToggle.IsCheckedChanged += OnLogRegionToggleChanged;
        DbLogToggle.IsCheckedChanged += OnLogRegionToggleChanged;
        ProgressLogToggle.IsCheckedChanged += OnLogRegionToggleChanged;
    }

    private async System.Threading.Tasks.Task LoadSettingsAsync()
    {
        _isProgrammaticChange = true;
        
        VsyncToggle.IsChecked = await _settings.GetBoolAsync("vsync", false);

        var masterEnabled = await _settings.GetBoolAsync("logging_master_enabled", false);
        MasterLogToggle.IsChecked = masterEnabled;

        // Set region toggles based on saved settings, defaulting UI/DB to true if master is on
        ScannerLogToggle.IsChecked = await _settings.GetBoolAsync("logging_region_scanner", false);
        ParserLogToggle.IsChecked = await _settings.GetBoolAsync("logging_region_parser", false);
        UiLogToggle.IsChecked = await _settings.GetBoolAsync("logging_region_ui", masterEnabled);
        DbLogToggle.IsChecked = await _settings.GetBoolAsync("logging_region_db", masterEnabled);
        ProgressLogToggle.IsChecked = await _settings.GetBoolAsync("logging_region_progress", false);

        _isProgrammaticChange = false;
    }
    
    private async void OnMasterLogToggleChanged(object? sender, RoutedEventArgs e)
    {
        if (_isProgrammaticChange) return;

        var isEnabled = MasterLogToggle.IsChecked == true;
        Logger.MasterLoggingEnabled = isEnabled;
        await _settings.SetAsync("logging_master_enabled", isEnabled ? "1" : "0");

        // If the master toggle was just turned on, we might need to set the default states for UI/DB
        if (isEnabled)
        {
            _isProgrammaticChange = true;
            // Check if a value has been explicitly saved before. If not, apply default.
            if (await _settings.GetAsync("logging_region_ui") == null)
            {
                UiLogToggle.IsChecked = true;
            }
            if (await _settings.GetAsync("logging_region_db") == null)
            {
                DbLogToggle.IsChecked = true;
            }
            _isProgrammaticChange = false;
        }
    }

    private async void OnLogRegionToggleChanged(object? sender, RoutedEventArgs e)
    {
        if (_isProgrammaticChange || sender is not ToggleSwitch ts) return;

        var (region, key) = ts.Name switch
        {
            "ScannerLogToggle" => (LogRegion.Scanner, "logging_region_scanner"),
            "ParserLogToggle" => (LogRegion.Parser, "logging_region_parser"),
            "UiLogToggle" => (LogRegion.UI, "logging_region_ui"),
            "DbLogToggle" => (LogRegion.DB, "logging_region_db"),
            "ProgressLogToggle" => (LogRegion.Progress, "logging_region_progress"),
            _ => (LogRegion.None, string.Empty)
        };
        
        if (region == LogRegion.None) return;

        var isChecked = ts.IsChecked == true;
        if (isChecked)
        {
            Logger.EnableRegion(region);
        }
        else
        {
            Logger.DisableRegion(region);
        }
        await _settings.SetAsync(key, isChecked ? "1" : "0");
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
        removeBtn.Classes.Add("Danger");

        var libId = lib.Id;
        removeBtn.Click += (_, _) => LibraryRemoveRequested?.Invoke(libId);

        var grid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
        };
        grid.Children.Add(textStack);
        Grid.SetColumn(removeBtn, 1);
        grid.Children.Add(removeBtn);

        var border = new Border
        {
            Padding = new Avalonia.Thickness(12, 8),
            CornerRadius = new Avalonia.CornerRadius(6),
            Child = grid,
        };
        border.Classes.Add("LibraryRow");
        return border;
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
