using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Aniplayer.Core.Interfaces;
using Aniplayer.Core.Models;
using Aniplayer.Core.Helpers;

namespace AniPlayer.UI;

public partial class PlayerPage : UserControl
{
    private IntPtr _mpvHandle;
    private FileStream? _lockStream;
    private bool _mpvInitialized;
    private TaskCompletionSource? _mpvReady;
    private DispatcherTimer? _positionTimer;
    private bool _isUserSeeking;
    private bool _isTransitioning;
    private DispatcherTimer? _controlsHideTimer;
    private bool _mouseOverControls;
    private bool _isFullscreen;
    private bool _cursorInitialized;
    private int _lastCursorX, _lastCursorY;
    private Dictionary<int, (string? lang, string? title)> _audioTrackInfo = new();
    private Dictionary<int, (string? lang, string? title)> _subtitleTrackInfo = new();
    private bool _vsyncEnabled;

    // State management
    private IWatchProgressService? _watchProgressService;
    private ILibraryService? _libraryService;
    private IReadOnlyList<Episode> _playlist = Array.Empty<Episode>();
    private int _playlistIndex = -1;
    private Episode? _currentEpisode;

    // Seek debouncing
    private long _lastSeekTicks;
    private volatile bool _seekInFlight;
    private bool _tickInProgress;

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    public event Action? PlaybackStopped;
    public event Action? FullscreenToggleRequested;

    public PlayerPage()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;

        ProgressSlider.AddHandler(PointerPressedEvent, OnSliderPointerPressed, RoutingStrategies.Tunnel);
        ProgressSlider.AddHandler(PointerReleasedEvent, OnSliderPointerReleased, RoutingStrategies.Tunnel);

        RootGrid.PointerMoved += OnPlayerPointerMoved;
    }

    private async void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _watchProgressService ??= App.Services.GetRequiredService<IWatchProgressService>();
        _libraryService ??= App.Services.GetRequiredService<ILibraryService>();

        if (!_mpvInitialized)
        {
            _mpvReady = new TaskCompletionSource();
            Logger.Log("PlayerPage attached — waiting for render, then initializing MPV");
            await Task.Delay(500);
            InitializeMpv();
        }
        else if (_mpvHandle != IntPtr.Zero)
        {
            _mpvReady = new TaskCompletionSource();
            Logger.Log("PlayerPage re-attached — re-initializing renderer for new native handle");
            await Task.Delay(500);
            if (VideoHostControl.NativeHandle != IntPtr.Zero)
            {
                VideoHostControl.InitializeRenderer(_mpvHandle, _vsyncEnabled);
                Logger.Log($"Renderer re-initialized: {VideoHostControl.Renderer?.IsInitialized ?? false}");
            }
            else
            {
                Logger.Log("WARNING: NativeHandle still zero after delay, renderer not re-initialized");
            }
            _mpvReady.TrySetResult();
        }
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        Logger.Log("[PlayerPage] Detached from visual tree, force-saving progress.");
        _ = SaveCurrentProgressAsync(force: true);
    }

    private async void InitializeMpv()
    {
        Logger.Log("=== InitializeMpv START ===");
        try
        {
            if (VideoHostControl.NativeHandle == IntPtr.Zero)
            {
                Logger.Log("VideoHostControl.NativeHandle is Zero, retrying in 500ms");
                StatusText.Text = "Waiting for video surface...";
                await Task.Delay(500);
                Dispatcher.UIThread.Post(InitializeMpv);
                return;
            }

            var settings = App.Services.GetService<ISettingsService>();
            if (settings != null)
                _vsyncEnabled = await settings.GetBoolAsync("vsync", false);

            Logger.Log("Creating mpv instance...");
            _mpvHandle = LibMpvInterop.mpv_create();
            Logger.Log($"mpv_create() returned handle: 0x{_mpvHandle.ToString("X")}");

            if (_mpvHandle == IntPtr.Zero)
            {
                Logger.LogError("Failed to create mpv instance");
                StatusText.Text = "Error — mpv create failed";
                return;
            }

            SetOption("vo", "libmpv");
            LibMpvInterop.mpv_request_log_messages(_mpvHandle, Encoding.UTF8.GetBytes("info\0"));

            SetOption("input-default-bindings", "yes");
            SetOption("input-vo-keyboard", "yes");
            SetOption("keep-open", "yes");
            SetOption("pause", "no");

            int initResult = LibMpvInterop.mpv_initialize(_mpvHandle);
            Logger.Log($"mpv_initialize: {initResult}");
            if (initResult < 0) { StatusText.Text = $"Error — init ({initResult})"; return; }

            VideoHostControl.InitializeRenderer(_mpvHandle, _vsyncEnabled);
            if (VideoHostControl.Renderer == null || !VideoHostControl.Renderer.IsInitialized)
            {
                StatusText.Text = "Error — renderer";
                return;
            }

            _mpvInitialized = true;
            StatusText.Text = "Ready";
            Logger.Log("=== InitializeMpv END (SUCCESS) ===");
            _mpvReady?.TrySetResult();
        }
        catch (Exception ex)
        {
            Logger.LogError("InitializeMpv exception", ex);
            StatusText.Text = $"Error — {ex.Message}";
            _mpvReady?.TrySetResult();
        }
    }

    private void SetOption(string name, string value)
    {
        if (_mpvHandle == IntPtr.Zero) return;
        int r = LibMpvInterop.mpv_set_option_string(
            _mpvHandle,
            Encoding.UTF8.GetBytes(name + "\0"),
            Encoding.UTF8.GetBytes(value + "\0"));
        Logger.Log($"option {name}={value}: {r}");
    }

    public async Task LoadPlaylistAsync(IReadOnlyList<Episode> playlist, int startIndex)
    {
        Logger.Log($"=== LoadPlaylistAsync: {playlist.Count} episodes, starting at index {startIndex} ===");

        if (!playlist.Any() || startIndex < 0 || startIndex >= playlist.Count)
        {
            Logger.LogError("Invalid playlist or start index provided.");
            return;
        }

        await CleanupCurrentFileAsync();
        _playlist = playlist;

        await ChangeToEpisodeAsync(playlist[startIndex], isInitialLoad: true);
    }

    public void PausePlayback()
    {
        if (_mpvHandle == IntPtr.Zero || !_mpvInitialized || _currentEpisode == null) return;
        SetOption("pause", "yes");
        PlayPauseButton.Content = "▶ Play";
        StatusText.Text = "Paused";
    }

    private void PlayPauseButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_mpvHandle == IntPtr.Zero || !_mpvInitialized) return;

        var paused = GetMpvPropertyString("pause") == "yes";
        SetOption("pause", paused ? "no" : "yes");

        PlayPauseButton.Content = paused ? "⏸ Pause" : "▶ Play";
        StatusText.Text = paused ? "Playing" : "Paused";
    }

    private async void StopButton_Click(object? sender, RoutedEventArgs e)
    {
        await CleanupCurrentFileAsync();
        PlaybackStopped?.Invoke();
    }

    private async void NextButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEpisode == null) return;
        await SaveCurrentProgressAsync(markCompleted: true);
        await PlayNextInPlaylistAsync();
    }

    private void FullscreenButton_Click(object? sender, RoutedEventArgs e) => FullscreenToggleRequested?.Invoke();

    public bool HandleKeyDown(Key key)
    {
        if (_mpvHandle == IntPtr.Zero || !_mpvInitialized || _currentEpisode == null)
            return false;

        switch (key)
        {
            case Key.Space: PlayPauseButton_Click(null, null!); return true;
            case Key.Left: SeekRelative(-5); return true;
            case Key.Right: SeekRelative(5); return true;
            case Key.Up: AdjustVolume(10); return true;
            case Key.Down: AdjustVolume(-5); return true;
            case Key.A: CycleAudioTrack(); return true;
            case Key.S: CycleSubtitleTrack(); return true;
            case Key.F: FullscreenToggleRequested?.Invoke(); return true;
            case Key.N: NextButton_Click(null, null!); return true;
            case Key.M: ToggleMute(); return true;
            default: return false;
        }
    }

    private void SeekRelative(double seconds)
    {
        if (_mpvHandle == IntPtr.Zero || _seekInFlight) return;

        var now = Environment.TickCount64;
        if (now - _lastSeekTicks < 250) return;
        _lastSeekTicks = now;
        _seekInFlight = true;

        var handle = _mpvHandle;
        Task.Run(() =>
        {
            try
            {
                var cmd = new[]
                {
                    Marshal.StringToHGlobalAnsi("seek"),
                    Marshal.StringToHGlobalAnsi(seconds.ToString("F1", CultureInfo.InvariantCulture)),
                    Marshal.StringToHGlobalAnsi("relative+keyframes"),
                    IntPtr.Zero
                };
                LibMpvInterop.mpv_command(handle, cmd);
                foreach (var ptr in cmd) if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);
            }
            finally { _seekInFlight = false; }
        });
    }

    private void AdjustVolume(int delta)
    {
        if (_mpvHandle == IntPtr.Zero) return;
        try
        {
            var volStr = GetMpvPropertyString("volume");
            if (!double.TryParse(volStr, out var vol)) vol = 100;
            var newVol = Math.Clamp(vol + delta, 0, 150);
            SetOption("volume", $"{newVol:F0}");
        }
        catch (Exception ex) { Logger.Log($"AdjustVolume failed: {ex.Message}"); }
    }

    private async Task ChangeToEpisodeAsync(Episode newEpisode, bool isInitialLoad = false)
    {
        if (_isTransitioning) return;
        _isTransitioning = true;

        if (!isInitialLoad && _currentEpisode != null && _currentEpisode.Id != newEpisode.Id)
        {
            Logger.Log($"[State] Saving progress for old episode {_currentEpisode.Id} before changing.");
            await SaveCurrentProgressAsync(force: true);
        }

        Logger.Log($"[State] Changing to new episode {newEpisode.Id} ('{newEpisode.FilePath}')");
        _currentEpisode = newEpisode;
        _playlistIndex = _playlist.ToList().FindIndex(e => e.Id == newEpisode.Id);

        await LoadFileIntoMpvAsync(newEpisode.FilePath);

        _isTransitioning = false;
        NextButton.IsEnabled = _playlistIndex < _playlist.Count - 1;
    }

    private async Task LoadFileIntoMpvAsync(string filePath)
    {
        Logger.Log($"=== LoadFileIntoMpvAsync: {filePath} ===");

        if (_mpvReady != null && !_mpvReady.Task.IsCompleted)
        {
            Logger.Log("Waiting for MPV/renderer initialization to complete...");
            await _mpvReady.Task;
        }

        if (!_mpvInitialized || _mpvHandle == IntPtr.Zero || !File.Exists(filePath))
        {
            Logger.LogError($"Pre-flight check failed for '{filePath}'");
            StatusText.Text = "Error — pre-flight check failed";
            return;
        }

        if (_lockStream != null)
        {
            await _lockStream.DisposeAsync();
            _lockStream = null;
        }

        var cmd = new[]
        {
            Marshal.StringToHGlobalAnsi("loadfile"),
            Marshal.StringToHGlobalAnsi(filePath),
            IntPtr.Zero
        };
        LibMpvInterop.mpv_command(_mpvHandle, cmd);
        foreach (var ptr in cmd) if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);

        _lockStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.Asynchronous);

        SetOption("pause", "no");

        // Load external subtitle if one is set for this episode
        if (_currentEpisode != null && !string.IsNullOrEmpty(_currentEpisode.ExternalSubtitlePath) && File.Exists(_currentEpisode.ExternalSubtitlePath))
        {
            Logger.Log($"Loading external subtitle: {_currentEpisode.ExternalSubtitlePath}");
            SetOption("sub-file", _currentEpisode.ExternalSubtitlePath);
        }

        VideoHostControl.Renderer?.Render();

        PlaceholderText.IsVisible = false;
        NowPlayingText.Text = Path.GetFileName(filePath);
        StatusText.Text = "Playing";
        PlayPauseButton.Content = "⏸ Pause";

        StartPositionTimer();

        if (_currentEpisode != null && _watchProgressService != null)
        {
            try
            {
                var progress = await _watchProgressService.GetProgressByEpisodeIdAsync(_currentEpisode.Id);
                if (progress != null && !progress.IsCompleted && progress.PositionSeconds > 5)
                {
                    Logger.Log($"Resuming episode {_currentEpisode.Id} from {progress.PositionSeconds}s");
                    await Task.Delay(300);
                    SeekAbsolute(progress.PositionSeconds);
                }
            }
            catch (Exception ex) { Logger.Log($"Failed to load saved progress: {ex.Message}"); }
        }

        await Task.Delay(500);
        PollMpvEvents();
        await Task.Delay(1000);
        await AnalyzeFileChapters();
        await UpdateAudioTracksAsync();
        await UpdateSubtitleTracksAsync();
        Logger.Log("=== LoadFileIntoMpvAsync END ===");
    }

    private async Task PlayNextInPlaylistAsync()
    {
        if (_isTransitioning || _playlistIndex >= _playlist.Count - 1) return;

        var nextIndex = _playlistIndex + 1;
        Logger.Log($"Playing next: index {nextIndex}/{_playlist.Count - 1}");

        await ChangeToEpisodeAsync(_playlist[nextIndex]);
    }

    private async Task AnalyzeFileChapters()
    {
        if (_currentEpisode == null || _mpvHandle == IntPtr.Zero) return;

        try
        {
            var chapterListJson = GetMpvPropertyString("chapter-list");
            if (string.IsNullOrEmpty(chapterListJson)) return;

            var rawChapters = new List<Chapters.ChapterInfo>();
            var chapters = JsonDocument.Parse(chapterListJson);

            foreach (var chapter in chapters.RootElement.EnumerateArray())
            {
                var title = chapter.TryGetProperty("title", out var t) ? t.GetString() : "Chapter";
                var time = chapter.TryGetProperty("time", out var ts) ? ts.GetDouble() : 0;
                rawChapters.Add(new Chapters.ChapterInfo(title ?? "", time));
            }

            var durationStr = GetMpvPropertyString("duration");
            if (double.TryParse(durationStr, out var duration) && duration > 0)
            {
                Chapters.Detect(_currentEpisode, rawChapters, duration);
                Logger.Log($"Chapter detection ran. HasIntro: {_currentEpisode.HasIntro}, HasOutro: {_currentEpisode.HasOutro}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("AnalyzeFileChapters failed", ex);
        }
    }

    private async Task UpdateAudioTracksAsync()
    {
        try
        {
            if (_mpvHandle == IntPtr.Zero || _currentEpisode == null || _libraryService == null) return;
            AudioTracksPanel.Children.Clear();
            _audioTrackInfo.Clear();

            var trackListJson = GetMpvPropertyString("track-list");
            if (string.IsNullOrEmpty(trackListJson)) return;

            var tracks = JsonDocument.Parse(trackListJson);
            var audioTracks = new List<(int id, string label, string? lang, string? title)>();

            foreach (var track in tracks.RootElement.EnumerateArray())
            {
                if (track.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "audio")
                {
                    var id = track.GetProperty("id").GetInt32();
                    var lang = track.TryGetProperty("lang", out var l) ? l.GetString() : null;
                    var title = track.TryGetProperty("title", out var t) ? t.GetString() : null;
                    var label = !string.IsNullOrEmpty(lang) && !string.IsNullOrEmpty(title) ? $"{lang} — {title}"
                        : title ?? lang ?? $"Track {id}";

                    audioTracks.Add((id, label, lang, title));
                    _audioTrackInfo[id] = (lang, title);
                }
            }

            var pref = await _libraryService.GetSeriesTrackPreferenceAsync(_currentEpisode.SeriesId);
            if (pref != null)
            {
                var match = MatchPreferredAudioTrack(audioTracks, pref.PreferredAudioLanguage, pref.PreferredAudioTitle, pref.PreferredAudioTrackId);
                if (match.id > 0)
                {
                    Logger.Log($"Auto-selecting preferred audio track {match.id}");
                    SetOption("aid", $"{match.id}");
                }
            }

            var aidStr = GetMpvPropertyString("aid");
            if (!long.TryParse(aidStr, out var currentAid)) currentAid = 0;

            foreach (var (id, label, _, _) in audioTracks)
            {
                var btn = new Button
                {
                    Content = label,
                    Tag = id,
                    Padding = new Thickness(8, 4),
                    Margin = new Thickness(0, 0, 4, 0),
                    FontSize = 11,
                    Focusable = false,
                    Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2A2A3E")),
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#D0D0E0")),
                    CornerRadius = new CornerRadius(4)
                };
                if (id == currentAid) btn.FontWeight = Avalonia.Media.FontWeight.Bold;
                btn.Click += (_, _) => { if (btn.Tag is int tid) SwitchAudioTrack(tid); };
                AudioTracksPanel.Children.Add(btn);
            }
        }
        catch (Exception ex) { Logger.LogError("UpdateAudioTracks", ex); }
    }

    private static (int id, string? lang, string? title) MatchPreferredAudioTrack(
        List<(int id, string label, string? lang, string? title)> tracks,
        string? prefLang, string? prefTitle, int? prefTrackId = null)
    {
        if (prefTrackId.HasValue && prefTrackId.Value > 0)
        {
            var m = tracks.FirstOrDefault(t => t.id == prefTrackId.Value);
            if (m.id > 0) return (m.id, m.lang, m.title);
        }
        if (!string.IsNullOrEmpty(prefTitle))
        {
            var m = tracks.FirstOrDefault(t => string.Equals(t.title, prefTitle, StringComparison.OrdinalIgnoreCase));
            if (m.title != null) return (m.id, m.lang, m.title);
        }
        if (!string.IsNullOrEmpty(prefLang))
        {
            var m = tracks.FirstOrDefault(t => string.Equals(t.lang, prefLang, StringComparison.OrdinalIgnoreCase));
            if (m.lang != null) return (m.id, m.lang, m.title);
        }
        return default;
    }

    private void SwitchAudioTrack(int trackId)
    {
        if (_mpvHandle == IntPtr.Zero || _currentEpisode == null || _libraryService == null) return;
        SetOption("aid", $"{trackId}");

        foreach (var child in AudioTracksPanel.Children)
        {
            if (child is Button btn)
                btn.FontWeight = (btn.Tag is int id && id == trackId)
                    ? Avalonia.Media.FontWeight.Bold
                    : Avalonia.Media.FontWeight.Normal;
        }

        if (_audioTrackInfo.TryGetValue(trackId, out var info))
        {
            var lang = info.lang ?? "";
            _ = _libraryService.UpsertSeriesAudioPreferenceAsync(_currentEpisode.SeriesId, lang, info.title, trackId);
        }
    }

    private void CycleAudioTrack()
    {
        if (_mpvHandle == IntPtr.Zero || _audioTrackInfo.Count == 0) return;

        var aidStr = GetMpvPropertyString("aid");
        if (!int.TryParse(aidStr, out var currentAid)) currentAid = 0;

        var trackIds = _audioTrackInfo.Keys.OrderBy(k => k).ToList();
        if (trackIds.Count == 0) return;

        var currentIndex = trackIds.IndexOf(currentAid);
        var nextIndex = (currentIndex + 1) % trackIds.Count;
        SwitchAudioTrack(trackIds[nextIndex]);

        // Show brief status of the selected track
        if (_audioTrackInfo.TryGetValue(trackIds[nextIndex], out var info))
        {
            var label = !string.IsNullOrEmpty(info.lang) && !string.IsNullOrEmpty(info.title)
                ? $"{info.lang} — {info.title}"
                : info.title ?? info.lang ?? $"Track {trackIds[nextIndex]}";
            StatusText.Text = $"Audio: {label}";
        }
    }

    private void ToggleMute()
    {
        if (_mpvHandle == IntPtr.Zero) return;
        var muted = GetMpvPropertyString("mute") == "yes";
        SetOption("mute", muted ? "no" : "yes");
        StatusText.Text = muted ? "Unmuted" : "Muted";
    }

    private async Task UpdateSubtitleTracksAsync()
    {
        try
        {
            if (_mpvHandle == IntPtr.Zero || _currentEpisode == null || _libraryService == null) return;
            SubtitleTracksPanel.Children.Clear();
            _subtitleTrackInfo.Clear();

            var trackListJson = GetMpvPropertyString("track-list");
            if (string.IsNullOrEmpty(trackListJson)) return;

            var tracks = JsonDocument.Parse(trackListJson);
            var subtitleTracks = new List<(int id, string label, string? lang, string? title)>();

            // Add "None" option
            subtitleTracks.Add((0, "None", null, null));
            _subtitleTrackInfo[0] = (null, null);

            foreach (var track in tracks.RootElement.EnumerateArray())
            {
                if (track.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "sub")
                {
                    var id = track.GetProperty("id").GetInt32();
                    var lang = track.TryGetProperty("lang", out var l) ? l.GetString() : null;
                    var title = track.TryGetProperty("title", out var t) ? t.GetString() : null;
                    var label = !string.IsNullOrEmpty(lang) && !string.IsNullOrEmpty(title) ? $"{lang} — {title}"
                        : title ?? lang ?? $"Track {id}";

                    subtitleTracks.Add((id, label, lang, title));
                    _subtitleTrackInfo[id] = (lang, title);
                }
            }

            var pref = await _libraryService.GetSeriesTrackPreferenceAsync(_currentEpisode.SeriesId);
            if (pref != null && !string.IsNullOrEmpty(pref.PreferredSubtitleLanguage))
            {
                var match = MatchPreferredSubtitleTrack(subtitleTracks, pref.PreferredSubtitleLanguage, pref.PreferredSubtitleName);
                if (match.id > 0)
                {
                    Logger.Log($"Auto-selecting preferred subtitle track {match.id}");
                    SetOption("sid", $"{match.id}");
                }
            }

            var sidStr = GetMpvPropertyString("sid");
            var currentSid = 0;
            if (!string.IsNullOrEmpty(sidStr) && sidStr != "no")
            {
                if (long.TryParse(sidStr, out var temp))
                    currentSid = (int)temp;
            }

            foreach (var (id, label, _, _) in subtitleTracks)
            {
                var btn = new Button
                {
                    Content = label,
                    Tag = id,
                    Padding = new Thickness(8, 4),
                    Margin = new Thickness(0, 0, 4, 0),
                    FontSize = 11,
                    Focusable = false,
                    Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2A2A3E")),
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#D0D0E0")),
                    CornerRadius = new CornerRadius(4)
                };
                if (id == currentSid) btn.FontWeight = Avalonia.Media.FontWeight.Bold;
                btn.Click += (_, _) => { if (btn.Tag is int tid) SwitchSubtitleTrack(tid); };
                SubtitleTracksPanel.Children.Add(btn);
            }
        }
        catch (Exception ex) { Logger.LogError("UpdateSubtitleTracks", ex); }
    }

    private static (int id, string? lang, string? title) MatchPreferredSubtitleTrack(
        List<(int id, string label, string? lang, string? title)> tracks,
        string? prefLang, string? prefTitle)
    {
        if (!string.IsNullOrEmpty(prefTitle))
        {
            var m = tracks.FirstOrDefault(t => string.Equals(t.title, prefTitle, StringComparison.OrdinalIgnoreCase));
            if (m.title != null) return (m.id, m.lang, m.title);
        }
        if (!string.IsNullOrEmpty(prefLang))
        {
            var m = tracks.FirstOrDefault(t => string.Equals(t.lang, prefLang, StringComparison.OrdinalIgnoreCase));
            if (m.lang != null) return (m.id, m.lang, m.title);
        }
        return default;
    }

    private void SwitchSubtitleTrack(int trackId)
    {
        if (_mpvHandle == IntPtr.Zero || _currentEpisode == null || _libraryService == null) return;

        if (trackId == 0)
        {
            SetOption("sid", "no");
        }
        else
        {
            SetOption("sid", $"{trackId}");
        }

        foreach (var child in SubtitleTracksPanel.Children)
        {
            if (child is Button btn)
                btn.FontWeight = (btn.Tag is int id && id == trackId)
                    ? Avalonia.Media.FontWeight.Bold
                    : Avalonia.Media.FontWeight.Normal;
        }

        if (trackId > 0 && _subtitleTrackInfo.TryGetValue(trackId, out var info))
        {
            var lang = info.lang ?? "";
            _ = _libraryService.UpsertSeriesSubtitlePreferenceAsync(_currentEpisode.SeriesId, lang, info.title);
        }
    }

    private void CycleSubtitleTrack()
    {
        if (_mpvHandle == IntPtr.Zero || _subtitleTrackInfo.Count == 0) return;

        var sidStr = GetMpvPropertyString("sid");
        var currentSid = 0;
        if (!string.IsNullOrEmpty(sidStr) && sidStr != "no")
            int.TryParse(sidStr, out currentSid);

        var trackIds = _subtitleTrackInfo.Keys.OrderBy(k => k).ToList();
        if (trackIds.Count == 0) return;

        var currentIndex = trackIds.IndexOf(currentSid);
        var nextIndex = (currentIndex + 1) % trackIds.Count;
        var nextId = trackIds[nextIndex];
        SwitchSubtitleTrack(nextId);

        // Show brief status
        if (nextId == 0)
        {
            StatusText.Text = "Subtitles: Off";
        }
        else if (_subtitleTrackInfo.TryGetValue(nextId, out var info))
        {
            var label = !string.IsNullOrEmpty(info.lang) && !string.IsNullOrEmpty(info.title)
                ? $"{info.lang} — {info.title}"
                : info.title ?? info.lang ?? $"Track {nextId}";
            StatusText.Text = $"Subtitle: {label}";
        }
    }

    private void StartPositionTimer()
    {
        _positionTimer?.Stop();
        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _positionTimer.Tick += OnPositionTimerTick;
        _positionTimer.Start();
    }

    private void StopPositionTimer()
    {
        _positionTimer?.Stop();
        _positionTimer = null;
    }

    private async void OnPositionTimerTick(object? sender, EventArgs e)
    {
        if (_mpvHandle == IntPtr.Zero || !_mpvInitialized || _tickInProgress) return;
        _tickInProgress = true;

        try
        {
            PollMpvEvents();
            UpdateSkipOverlay(); // Check for intro/outro

            // Detect mouse movement via OS cursor position polling.
            // This is necessary because NativeControlHost's HWND swallows Avalonia pointer events.
            if (OperatingSystem.IsWindows() && _isFullscreen)
            {
                if (GetCursorPos(out var cursorPos))
                {
                    if (!_cursorInitialized)
                    {
                        // Snapshot initial position without triggering movement
                        _lastCursorX = cursorPos.X;
                        _lastCursorY = cursorPos.Y;
                        _cursorInitialized = true;
                    }
                    else if (cursorPos.X != _lastCursorX || cursorPos.Y != _lastCursorY)
                    {
                        _lastCursorX = cursorPos.X;
                        _lastCursorY = cursorPos.Y;
                        ShowControls();
                        ResetControlsHideTimer();
                    }
                }
            }

            var (eofReached, posStr, durStr) = await Task.Run(() => (
                GetMpvPropertyString("eof-reached"),
                GetMpvPropertyString("time-pos"),
                GetMpvPropertyString("duration")
            ));

            if (_mpvHandle == IntPtr.Zero) return;

            if (eofReached == "yes" && !_isTransitioning && _playlistIndex < _playlist.Count - 1)
            {
                Logger.Log("EOF reached — auto-playing next episode");
                await PlayNextInPlaylistAsync();
                return;
            }

            if (!_isUserSeeking &&
             double.TryParse(posStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var pos) &&
             double.TryParse(durStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var dur) &&
             dur > 0)
            {
                ProgressSlider.Maximum = dur;
                ProgressSlider.Value = pos;
                TimeCurrentText.Text = FormatTime(pos);
                TimeTotalText.Text = FormatTime(dur);

                await SaveCurrentProgressAsync(force: false);
            }
        }
        finally
        {
            _tickInProgress = false;
        }
    }

    private void UpdateSkipOverlay()
    {
        if (_currentEpisode == null) return;

        var now = ProgressSlider.Value;

        if (_currentEpisode.HasIntro && now >= _currentEpisode.IntroStart && now < _currentEpisode.IntroEnd)
        {
            if (!SkipOverlayButton.IsVisible)
            {
                SkipButtonText.Text = "Skip Intro";
                SkipOverlayButton.Tag = _currentEpisode.IntroEnd;
                SkipOverlayButton.IsVisible = true;
            }
        }
        else if (_currentEpisode.HasOutro && now >= _currentEpisode.OutroStart && _playlistIndex < _playlist.Count - 1)
        {
            if (!SkipOverlayButton.IsVisible || SkipButtonText.Text as string != "Next Episode")
            {
                SkipButtonText.Text = "Next Episode";
                SkipOverlayButton.Tag = -1.0;
                SkipOverlayButton.IsVisible = true;
            }
        }
        else
        {
            if (SkipOverlayButton.IsVisible) SkipOverlayButton.IsVisible = false;
        }
    }

    private void OnSkipButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not double target) return;

        if (target == -1.0)
        {
            NextButton_Click(sender, e);
        }
        else
        {
            SeekAbsolute(target);
        }
        btn.IsVisible = false;
    }

    private async Task SaveCurrentProgressAsync(bool force = false, bool markCompleted = false)
    {
        if (_currentEpisode == null || _watchProgressService == null || _mpvHandle == IntPtr.Zero)
            return;

        try
        {
            var posStr = GetMpvPropertyString("time-pos");
            var durStr = GetMpvPropertyString("duration");
            if (double.TryParse(posStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var pos) &&
            double.TryParse(durStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var dur) &&
            dur > 0)
            {
                var posSec = (int)pos;
                var durSec = (int)dur;

                bool shouldMarkCompleted = markCompleted || pos >= dur * 0.9 || (dur > 30 && dur - pos < 30);

                if (shouldMarkCompleted)
                {
                    Logger.Log($"Marking episode {_currentEpisode.Id} as completed ({pos:F0}/{dur:F0}s)");
                    await _watchProgressService.MarkCompletedAsync(_currentEpisode.Id);
                }
                else
                {
                    await _watchProgressService.UpdateProgressAsync(_currentEpisode.Id, posSec, durSec, force);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to save progress: {ex.Message}");
        }
    }

    private void OnSliderPointerPressed(object? sender, PointerPressedEventArgs e) => _isUserSeeking = true;

    private void OnSliderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isUserSeeking)
        {
            _isUserSeeking = false;
            SeekAbsolute(ProgressSlider.Value);
        }
    }

    private void SeekAbsolute(double seconds)
    {
        if (_mpvHandle == IntPtr.Zero || _seekInFlight) return;
        _seekInFlight = true;

        var handle = _mpvHandle;
        Task.Run(() =>
        {
            try
            {
                var cmd = new[]
                {
                    Marshal.StringToHGlobalAnsi("seek"),
                    Marshal.StringToHGlobalAnsi(seconds.ToString("F1", CultureInfo.InvariantCulture)),
                    Marshal.StringToHGlobalAnsi("absolute+keyframes"),
                    IntPtr.Zero
                };
                LibMpvInterop.mpv_command(handle, cmd);
                foreach (var ptr in cmd) if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);
            }
            finally { _seekInFlight = false; }
        });
    }

    private string? GetMpvPropertyString(string name)
    {
        if (_mpvHandle == IntPtr.Zero) return null;
        try
        {
            var ptr = LibMpvInterop.mpv_get_property_string(_mpvHandle, Encoding.UTF8.GetBytes(name + "\0"));
            if (ptr == IntPtr.Zero) return null;
            var val = Marshal.PtrToStringAnsi(ptr);
            LibMpvInterop.mpv_free(ptr);
            return val;
        }
        catch { return null; }
    }

    private static string FormatTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(seconds, 0));
        return ts.Hours > 0 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
    }

    public void SetFullscreen(bool fullscreen)
    {
        _isFullscreen = fullscreen;
        if (fullscreen)
        {
            // Controls stay in Row 1 (never move into Row 0 HWND region)
            // Grid layout (RowDefinitions="*,Auto") naturally overlays Row 1 at bottom
            _cursorInitialized = false;   // Reset cursor tracking for fresh state
            ShowControls();
            ResetControlsHideTimer();
        }
        else
        {
            // Windowed mode: controls always visible
            StopControlsHideTimer();
            _cursorInitialized = false;
            ShowControls();
        }
    }

    private void OnPlayerPointerMoved(object? sender, PointerEventArgs e)
    {
        ShowControls();
        ResetControlsHideTimer();
    }

    private void ShowControls()
    {
        if (!ControlsBar.IsVisible)
        {
            ControlsBar.IsVisible = true;
            Cursor = Cursor.Default;
        }
    }

    private void HideControls()
    {
        if (!_isFullscreen || _mouseOverControls || _currentEpisode == null) return;
        ControlsBar.IsVisible = false;
        Cursor = new Cursor(StandardCursorType.None);
    }

    private void ResetControlsHideTimer()
    {
        _controlsHideTimer?.Stop();
        // Only auto-hide controls in fullscreen mode; in windowed mode they stay visible
        // Note: _currentEpisode check is in HideControls(), not here, so timer can arm even before playback
        if (!_isFullscreen) return;
        _controlsHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _controlsHideTimer.Tick += (_, _) => { _controlsHideTimer?.Stop(); HideControls(); };
        _controlsHideTimer.Start();
    }

    private void OnControlsBarPointerEntered(object? sender, PointerEventArgs e)
        => _mouseOverControls = true;

    private void OnControlsBarPointerExited(object? sender, PointerEventArgs e)
    {
        _mouseOverControls = false;
        if (_isFullscreen) ResetControlsHideTimer();
    }

    private void StopControlsHideTimer()
    {
        _controlsHideTimer?.Stop();
        _controlsHideTimer = null;
        ShowControls();
    }

    private async Task CleanupCurrentFileAsync()
    {
        await SaveCurrentProgressAsync(force: true);

        StopPositionTimer();
        StopControlsHideTimer();

        if (_lockStream != null)
        {
            await _lockStream.DisposeAsync();
            _lockStream = null;
        }

        if (_mpvHandle != IntPtr.Zero)
            try { SetOption("command", "stop"); } catch { }

        _currentEpisode = null;
        NowPlayingText.Text = "No file loaded";
        StatusText.Text = "Stopped";
        AudioTracksPanel.Children.Clear();
        SubtitleTracksPanel.Children.Clear();
        PlaceholderText.IsVisible = true;
        PlayPauseButton.Content = "▶ Play";
        ProgressSlider.Value = 0;
        ProgressSlider.Maximum = 100;
        TimeCurrentText.Text = "0:00";
        TimeTotalText.Text = "0:00";
    }

    private void PollMpvEvents()
    {
        try
        {
            for (int i = 0; i < 20; i++)
            {
                IntPtr eventPtr = LibMpvInterop.mpv_wait_event(_mpvHandle, 0);
                if (eventPtr == IntPtr.Zero || Marshal.ReadInt32(eventPtr) == 0) break;
            }
        }
        catch (Exception ex) { Logger.LogError("PollMpvEvents", ex); }
    }

    public async Task ShutdownAsync()
    {
        await CleanupCurrentFileAsync();
        VideoHostControl.Renderer?.Dispose();
        if (_mpvHandle != IntPtr.Zero)
        {
            LibMpvInterop.mpv_terminate_destroy(_mpvHandle);
            _mpvHandle = IntPtr.Zero;
        }
    }
}
