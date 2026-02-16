using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace AniPlayer.UI;

public partial class EpisodeRow : UserControl
{
    public event Action<int>? PlayRequested;
    public int EpisodeId { get; private set; }

    public EpisodeRow()
    {
        InitializeComponent();
    }

    public void SetData(int episodeId, double? episodeNumber, string? title,
                        string episodeType, double? progressPercent)
    {
        EpisodeId = episodeId;

        EpisodeNumberText.Text = episodeNumber.HasValue
            ? episodeNumber.Value.ToString("00.##")
            : "--";

        EpisodeTitleText.Text = title ?? $"Episode {EpisodeNumberText.Text}";

        if (episodeType != "EPISODE")
        {
            TypeBadge.IsVisible = true;
            TypeBadgeText.Text = episodeType;
        }

        if (progressPercent is > 0)
        {
            ProgressBar.IsVisible = true;
            ProgressFill.Width = Math.Min(progressPercent.Value * 60.0, 60.0);
        }
    }

    private void PlayButton_Click(object? sender, RoutedEventArgs e)
    {
        PlayRequested?.Invoke(EpisodeId);
    }
}
