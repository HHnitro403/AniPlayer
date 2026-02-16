using Avalonia.Controls;
using Avalonia.Input;
using System;

namespace AniPlayer.UI;

public partial class SeriesCard : UserControl
{
    public event Action<int>? Clicked;
    public int SeriesId { get; private set; }

    public SeriesCard()
    {
        InitializeComponent();
        PointerPressed += OnPointerPressed;
    }

    public void SetData(int seriesId, string title, int? episodeCount, string? coverPath)
    {
        SeriesId = seriesId;
        TitleText.Text = title;
        EpisodeCountText.Text = episodeCount.HasValue ? $"{episodeCount} episodes" : "";

        if (!string.IsNullOrEmpty(coverPath) && System.IO.File.Exists(coverPath))
        {
            CoverImage.Source = new Avalonia.Media.Imaging.Bitmap(coverPath);
            CoverPlaceholder.IsVisible = false;
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Clicked?.Invoke(SeriesId);
    }
}
