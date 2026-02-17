using Avalonia.Controls;
using Avalonia.Interactivity;
using Aniplayer.Core.Models;
using System;

namespace AniPlayer.UI.Views.Controls;

public partial class EpisodeRow : UserControl
{
    public EpisodeRow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not Episode episode)
        {
            return;
        }

        EpisodeNumberText.Text = episode.EpisodeNumber.HasValue
            ? episode.EpisodeNumber.Value.ToString("00.##")
            : "--";

        EpisodeTitleText.Text = episode.Title ?? $"Episode {EpisodeNumberText.Text}";

        var episodeType = episode.EpisodeType ?? "EPISODE";
        TypeBadge.IsVisible = episodeType != "EPISODE";
        TypeBadgeText.Text = episodeType;

        // The ShowInfoPage does not currently track progress for all episodes,
        // so the progress bar is always hidden here.
        ProgressBar.IsVisible = false;
    }
}
