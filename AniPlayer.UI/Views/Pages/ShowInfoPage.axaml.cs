using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace AniPlayer.UI;

public partial class ShowInfoPage : UserControl
{
    public event Action? BackRequested;
    public event Action<int>? EpisodePlayRequested;

    private int _seriesId;

    public ShowInfoPage()
    {
        InitializeComponent();
    }

    public void LoadSeries(int seriesId)
    {
        _seriesId = seriesId;
        // Will be wired to services later — for now just sets the ID
        Logger.Log($"ShowInfoPage: LoadSeries({seriesId})");
    }

    private void BackButton_Click(object? sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke();
    }
}
