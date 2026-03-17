using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System;
using System.IO;
using System.Threading.Tasks;
using Aniplayer.Core.Models;

namespace AniPlayer.UI
{
    public partial class ContinueWatchingCard : UserControl
    {
        public event Action<int>? Clicked;
        public int EpisodeId { get; private set; }

        public ContinueWatchingCard()
        {
            InitializeComponent();
            MainBorder.PointerPressed += OnPointerPressed;
        }

        public void SetData(Episode episode, WatchProgress progress, Series? series)
        {
            EpisodeId = episode.Id;
            
            // Text
            EpisodeTitleText.Text = episode.DisplayName;
            SeriesTitleText.Text = series?.DisplayTitle ?? "Unknown Series";
            
            // Progress Bar
            ProgressBar.Value = progress.ProgressPercent * 100;
            
            // Time Remaining
            var remaining = TimeSpan.FromSeconds((progress.DurationSeconds ?? 0) - progress.PositionSeconds);
            if (remaining.TotalMinutes > 0)
            {
                TimeRemainingText.Text = remaining.TotalMinutes < 60 
                    ? $"{remaining.TotalMinutes:F0}m left"
                    : $"{remaining.TotalHours:F1}h left";
            }
            else
            {
                TimeRemainingText.Text = "Finished";
            }

            LoadCover(episode.ThumbnailPath ?? series?.CoverImagePath);
        }

        private void LoadCover(string? coverPath)
        {
            if (!string.IsNullOrEmpty(coverPath) && File.Exists(coverPath))
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        using var stream = File.OpenRead(coverPath);
                        var bitmap = Bitmap.DecodeToWidth(stream, 300);
                        Dispatcher.UIThread.InvokeAsync(() => CoverImage.Source = bitmap);
                    }
                    catch { /* Ignore */ }
                });
            }
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            Clicked?.Invoke(EpisodeId);
        }
    }
}
