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
    public event EventHandler? RewindClicked;
    public event EventHandler? FastForwardClicked;
    public event EventHandler<double>? SpeedChanged;
    public event EventHandler<double>? AudioDelayChanged;
    public event EventHandler<double>? SubtitleDelayChanged;
    public event EventHandler? FullscreenClicked;
    
    public Slider Progress => ProgressSlider;
    public Slider Volume => VolumeSlider;

    private int _audioDelayMs = 0;
    private int _subDelayMs = 0;

    public PlayerControls()
    {
        InitializeComponent();
        PlayPauseButton.Click += (s, e) => PlayPauseClicked?.Invoke(this, e);
        StopButton.Click += (s, e) => StopClicked?.Invoke(this, e);
        NextButton.Click += (s, e) => NextClicked?.Invoke(this, e);
        PreviousButton.Click += (s, e) => PreviousClicked?.Invoke(this, e);
        RewindButton.Click += (s, e) => RewindClicked?.Invoke(this, e);
        FastForwardButton.Click += (s, e) => FastForwardClicked?.Invoke(this, e);
        FullscreenButton.Click += (s, e) => FullscreenClicked?.Invoke(this, e);

        AudioDelayMinusButton.Click += (s, e) => AdjustAudioDelay(-50);
        AudioDelayPlusButton.Click += (s, e) => AdjustAudioDelay(50);
        SubDelayMinusButton.Click += (s, e) => AdjustSubDelay(-50);
        SubDelayPlusButton.Click += (s, e) => AdjustSubDelay(50);

        SpeedSelector.SelectionChanged += (s, e) =>
        {
            if (SpeedSelector.SelectedItem is ComboBoxItem item &&
                item.Tag is string tagStr &&
                double.TryParse(tagStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var speed))
            {
                SpeedChanged?.Invoke(this, speed);
            }
        };
    }

    private void AdjustAudioDelay(int delta)
    {
        _audioDelayMs += delta;
        AudioDelayText.Text = _audioDelayMs.ToString();
        AudioDelayChanged?.Invoke(this, _audioDelayMs / 1000.0);
    }

    private void AdjustSubDelay(int delta)
    {
        _subDelayMs += delta;
        SubDelayText.Text = _subDelayMs.ToString();
        SubtitleDelayChanged?.Invoke(this, _subDelayMs / 1000.0);
    }

    public void SetAudioDelay(int ms)
    {
        _audioDelayMs = ms;
        AudioDelayText.Text = _audioDelayMs.ToString();
    }

    public void SetSubtitleDelay(int ms)
    {
        _subDelayMs = ms;
        SubDelayText.Text = _subDelayMs.ToString();
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
