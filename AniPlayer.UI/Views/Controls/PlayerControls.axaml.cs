using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;

namespace AniPlayer.UI;

public partial class PlayerControls : UserControl
{
    public event EventHandler? PlayPauseClicked;
    public event EventHandler? StopClicked;
    public event EventHandler? NextClicked;
    public event EventHandler? PreviousClicked;
    public event EventHandler? FullscreenClicked;
    
    public Slider Progress => ProgressSlider;
    public Slider Volume => VolumeSlider;

    public PlayerControls()
    {
        InitializeComponent();
        PlayPauseButton.Click += (s, e) => PlayPauseClicked?.Invoke(this, e);
        StopButton.Click += (s, e) => StopClicked?.Invoke(this, e);
        NextButton.Click += (s, e) => NextClicked?.Invoke(this, e);
        PreviousButton.Click += (s, e) => PreviousClicked?.Invoke(this, e);
        FullscreenButton.Click += (s, e) => FullscreenClicked?.Invoke(this, e);
    }

    public void SetPlayState(bool isPlaying)
    {
        var iconKey = isPlaying ? "IconPause" : "IconPlay";
        if (Application.Current?.TryFindResource(iconKey, out var res) == true && res is StreamGeometry geo)
        {
            PlayPauseIcon.Data = geo;
        }
    }
    
    public void SetNowPlaying(string title, string status)
    {
        NowPlayingText.Text = title;
        StatusText.Text = status;
    }
    
    public void SetTime(string current, string total)
    {
        TimeCurrentText.Text = current;
        TimeTotalText.Text = total;
    }
    
    public void SetNextEnabled(bool enabled)
    {
        NextButton.IsEnabled = enabled;
    }
}
