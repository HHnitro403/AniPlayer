using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System;
using System.IO;
using System.Threading.Tasks;
using Aniplayer.Core.Models;

namespace AniPlayer.UI;

public partial class SeriesCard : UserControl
{
    public event Action<int>? Clicked;
    public event Action<string>? GroupClicked;
    
    public int SeriesId { get; private set; }
    public string? SeriesGroupName { get; private set; }

    public SeriesCard()
    {
        InitializeComponent();
        PointerPressed += OnPointerPressed;
    }

    public void SetData(Series series, int seasonCount = 1)
    {
        SeriesId = series.Id;
        SeriesGroupName = series.SeriesGroupName;
        TitleText.Text = series.DisplayTitle;
        
        if (seasonCount > 1)
        {
            SubtitleText.Text = $"{series.TotalEpisodes ?? 0} episodes";
            SeasonsText.Text = $"{seasonCount} Seasons";
            SeasonsBadge.IsVisible = true;
        }
        else
        {
            SubtitleText.Text = series.TotalEpisodes.HasValue ? $"{series.TotalEpisodes} episodes" : "";
            SeasonsBadge.IsVisible = false;
        }

        LoadCover(series.CoverImagePath);
    }

    private void LoadCover(string? coverPath)
    {
        if (!string.IsNullOrEmpty(coverPath) && File.Exists(coverPath))
        {
            CoverPlaceholder.IsVisible = false;
            // Load asynchronously to avoid UI stutter
            _ = Task.Run(() =>
            {
                try
                {
                    using var stream = File.OpenRead(coverPath);
                    var bitmap = Bitmap.DecodeToWidth(stream, 300);
                    Dispatcher.UIThread.InvokeAsync(() => CoverImage.Source = bitmap);
                }
                catch
                {
                    Dispatcher.UIThread.InvokeAsync(() => CoverPlaceholder.IsVisible = true);
                }
            });
        }
        else
        {
            CoverImage.Source = null;
            CoverPlaceholder.IsVisible = true;
            var title = TitleText.Text ?? "";
            CoverPlaceholder.Text = title.Length > 0 ? title[0].ToString().ToUpper() : "?";
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!string.IsNullOrEmpty(SeriesGroupName))
        {
            GroupClicked?.Invoke(SeriesGroupName);
        }
        else
        {
            Clicked?.Invoke(SeriesId);
        }
    }
}
