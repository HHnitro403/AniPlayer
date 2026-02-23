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

        // Show progress bar for in-progress episodes (not completed, has progress)
        if (episode.Progress != null && !episode.Progress.IsCompleted && episode.Progress.ProgressPercent > 0)
        {
            ProgressBar.IsVisible = true;
            var progressWidth = 60 * episode.Progress.ProgressPercent; // Max width is 60px
            ProgressFill.Width = progressWidth;
        }
        else
        {
            ProgressBar.IsVisible = false;
        }
    }
}
