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

namespace AniPlayer.UI;

public partial class PlayerPage : UserControl
{
    private IntPtr _mpvHandle;
    private FileStream? _lockStream;
    private string? _currentFile;
    private bool _mpvInitialized;
    private TaskCompletionSource? _mpvReady;
    private DispatcherTimer? _positionTimer;
    private bool _isUserSeeking;
    private string[] _playlist = Array.Empty<string>();
    private int[] _playlistEpisodeIds = Array.Empty<int>();
    private int _playlistIndex;
    private bool _isTransitioning;
    private DispatcherTimer? _controlsHideTimer;
    private bool _mouseOverControls;
    private bool _isFullscreen;
    private int _lastCursorX, _lastCursorY;
    private int _currentSeriesId;
    private ILibraryService? _libraryService;
    private Dictionary<int, (string? lang, string? title)> _audioTrackInfo = new();
    private bool _vsyncEnabled;

    // Watch progress tracking
    private IWatchProgressService? _watchProgressService;
    private int _currentEpisodeId;
    private int _progressSaveCounter;
    private int _lastSavedPositionSeconds = -1;

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

        // Tunnel handlers so we catch pointer events before the slider's internal handling
        ProgressSlider.AddHandler(PointerPressedEvent, OnSliderPointerPressed, RoutingStrategies.Tunnel);
        ProgressSlider.AddHandler(PointerReleasedEvent, OnSliderPointerReleased, RoutingStrategies.Tunnel);

        // Auto-hide controls: mouse move anywhere shows them, 5s idle hides them
        RootGrid.PointerMoved += OnPlayerPointerMoved;
        ControlsBar.PointerEntered += (_, _) => _mouseOverControls = true;
        ControlsBar.PointerExited += (_, _) => { _mouseOverControls = false; ResetControlsHideTimer(); };
    }

    private async void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (!_mpvInitialized)
        {
            _mpvReady = new TaskCompletionSource();
            Logger.Log("PlayerPage attached — waiting for render, then initializing MPV");
            await Task.Delay(500);
            InitializeMpv();
        }
        else if (_mpvHandle != IntPtr.Zero)
        {
            // MPV already created but renderer was disposed when we navigated away.
            // Re-attach the renderer to the new native control.
            // Reset _mpvReady so LoadFileAsync waits for the renderer before sending loadfile.
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

    // ── MPV init (moved from MainWindow) ─────────────────────

    private async void InitializeMpv()
    {
        Logger.Log("=== InitializeMpv START ===");
        try
        {
            if (VideoHostControl.NativeHandle == IntPtr.Zero)
            {
                Logger.Log("VideoHostControl.NativeHandle is Zero, retrying in 500ms");
                StatusText.Text = "Waiting for video surface...";
                Task.Delay(500).ContinueWith(_ =>
                    Avalonia.Threading.Dispatcher.UIThread.Post(InitializeMpv));
                return;
            }

            // Read vsync setting (default off)
            try
            {
                var settings = App.Services.GetService(typeof(ISettingsService)) as ISettingsService;
                if (settings != null)
                    _vsyncEnabled = await settings.GetBoolAsync("vsync", false);
            }
            catch { /* settings unavailable — default to off */ }

            Logger.Log("Creating mpv instance...");
            _mpvHandle = LibMpvInterop.mpv_create();
            Logger.Log($"mpv_create() returned handle: 0x{_mpvHandle.ToString("X")}");

            if (_mpvHandle == IntPtr.Zero)
            {
                Logger.LogError("Failed to create mpv instance");
                StatusText.Text = "Error — mpv create failed";
                return;
            }

            // vo = libmpv (render-API mode)
            int voResult = LibMpvInterop.mpv_set_option_string(
                _mpvHandle,
                Encoding.UTF8.GetBytes("vo\0"),
                Encoding.UTF8.GetBytes("libmpv\0"));
            Logger.Log($"vo=libmpv: {voResult}");
            if (voResult != 0) { StatusText.Text = "Error — vo"; return; }

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
        int r = LibMpvInterop.mpv_set_option_string(
            _mpvHandle,
            Encoding.UTF8.GetBytes(name + "\0"),
            Encoding.UTF8.GetBytes(value + "\0"));
        Logger.Log($"option {name}={value}: {r}");
    }

    // ── Public API (called by MainWindow navigation) ─────────

    public async Task LoadFileAsync(string filePath)
    {
        Logger.Log($"=== LoadFileAsync: {filePath} ===");

        // Wait for MPV/renderer init if it's still in progress (first init or re-attach)
        if (_mpvReady != null && !_mpvReady.Task.IsCompleted)
        {
            Logger.Log("Waiting for MPV/renderer initialization to complete...");
            await _mpvReady.Task;
        }

        if (!_mpvInitialized || _mpvHandle == IntPtr.Zero)
        {
            Logger.LogError("MPV not initialized");
            StatusText.Text = "Error — MPV not ready";
            return;
        }

        if (!File.Exists(filePath))
        {
            Logger.LogError($"File not found: {filePath}");
            StatusText.Text = "Error — file not found";
            return;
        }

        await CleanupCurrentFileAsync();
        _currentFile = Path.GetFileName(filePath);

        // loadfile command
        var cmd = new[]
        {
            Marshal.StringToHGlobalAnsi("loadfile"),
            Marshal.StringToHGlobalAnsi(filePath),
            IntPtr.Zero
        };
        int cmdResult = LibMpvInterop.mpv_command(_mpvHandle, cmd);
        Logger.Log($"mpv_command(loadfile): {cmdResult}");

        // Acquire file lock after mpv opens it
        _lockStream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read,
            FileShare.Read, bufferSize: 1, FileOptions.Asynchronous);

        // Free command pointers
        foreach (var ptr in cmd)
            if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);

        // Unpause
        LibMpvInterop.mpv_set_property_string(
            _mpvHandle,
            Encoding.UTF8.GetBytes("pause\0"),
            Encoding.UTF8.GetBytes("no\0"));

        VideoHostControl.Renderer?.Render();

        PlaceholderText.IsVisible = false;
        NowPlayingText.Text = _currentFile;
        StatusText.Text = "Playing";
        PlayPauseButton.Content = "Pause";

        StartPositionTimer();

        // Resume from saved position if we have progress for this episode
        if (_currentEpisodeId > 0 && _watchProgressService != null)
        {
            try
            {
                var progress = await _watchProgressService.GetProgressByEpisodeIdAsync(_currentEpisodeId);
                if (progress != null && !progress.IsCompleted && progress.PositionSeconds > 5)
                {
                    Logger.Log($"Resuming episode {_currentEpisodeId} from {progress.PositionSeconds}s");
                    await Task.Delay(300); // let mpv load the file first
                    SeekAbsolute(progress.PositionSeconds);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to load saved progress: {ex.Message}");
            }
        }

        await Task.Delay(500);
        PollMpvEvents();

        await Task.Delay(1000);
        await UpdateAudioTracksAsync();

        Logger.Log("=== LoadFileAsync END ===");
    }

    // ── Transport controls ───────────────────────────────────

    public void PausePlayback()
    {
        if (_mpvHandle == IntPtr.Zero || !_mpvInitialized || _currentFile == null) return;
        LibMpvInterop.mpv_set_property_string(
            _mpvHandle,
            Encoding.UTF8.GetBytes("pause\0"),
            Encoding.UTF8.GetBytes("yes\0"));
        PlayPauseButton.Content = "Play";
        StatusText.Text = "Paused";
    }

    private void PlayPauseButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_mpvHandle == IntPtr.Zero || !_mpvInitialized) return;

        var pausedPtr = LibMpvInterop.mpv_get_property_string(
            _mpvHandle, Encoding.UTF8.GetBytes("pause\0"));
        if (pausedPtr == IntPtr.Zero) return;

        var paused = Marshal.PtrToStringAnsi(pausedPtr);
        LibMpvInterop.mpv_free(pausedPtr);

        bool isPaused = paused == "yes";
        LibMpvInterop.mpv_set_property_string(
            _mpvHandle,
            Encoding.UTF8.GetBytes("pause\0"),
            Encoding.UTF8.GetBytes(isPaused ? "no\0" : "yes\0"));

        PlayPauseButton.Content = isPaused ? "Pause" : "Play";
        StatusText.Text = isPaused ? "Playing" : "Paused";
    }

    private async void StopButton_Click(object? sender, RoutedEventArgs e)
    {
        await CleanupCurrentFileAsync();
        PlaybackStopped?.Invoke();
    }

    private void NextButton_Click(object? sender, RoutedEventArgs e) => _ = PlayNextInPlaylistAsync();

    private void FullscreenButton_Click(object? sender, RoutedEventArgs e) => FullscreenToggleRequested?.Invoke();

    // ── Keyboard shortcuts ─────────────────────────────────

    /// <summary>
    /// Called by MainWindow.OnMainWindowKeyDown when the player page is active.
    /// Returns true if the key was handled.
    /// </summary>
    public bool HandleKeyDown(Key key)
    {
        if (_mpvHandle == IntPtr.Zero || !_mpvInitialized || _currentFile == null)
            return false;

        switch (key)
        {
            case Key.Space:
                PlayPauseButton_Click(null, null!);
                return true;

            case Key.Left:
                SeekRelative(-5);
                return true;

            case Key.Right:
                SeekRelative(5);
                return true;

            case Key.Up:
                AdjustVolume(10);
                return true;

            case Key.Down:
                AdjustVolume(-5);
                return true;

            default:
                return false;
        }
    }

    private void SeekRelative(double seconds)
    {
        if (_mpvHandle == IntPtr.Zero) return;
        var cmd = new[]
        {
            Marshal.StringToHGlobalAnsi("seek"),
            Marshal.StringToHGlobalAnsi(seconds.ToString("F1", CultureInfo.InvariantCulture)),
            Marshal.StringToHGlobalAnsi("relative"),
            IntPtr.Zero
        };
        LibMpvInterop.mpv_command(_mpvHandle, cmd);
        foreach (var ptr in cmd)
            if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);
    }

    private void AdjustVolume(int delta)
    {
        if (_mpvHandle == IntPtr.Zero) return;
        var volStr = GetMpvPropertyString("volume");
        if (!double.TryParse(volStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var vol))
            vol = 100;
        var newVol = Math.Clamp(vol + delta, 0, 150);
        LibMpvInterop.mpv_set_property_string(
            _mpvHandle,
            Encoding.UTF8.GetBytes("volume\0"),
            Encoding.UTF8.GetBytes($"{newVol:F0}\0"));
        Logger.Log($"Volume: {vol:F0} → {newVol:F0}");
    }

    // ── Playlist / auto-next ────────────────────────────────

    public void SetSeriesContext(int seriesId, ILibraryService libraryService)
    {
        _currentSeriesId = seriesId;
        _libraryService = libraryService;
    }

    public void SetEpisodeContext(int episodeId)
    {
        _currentEpisodeId = episodeId;
        _progressSaveCounter = 0;
        _lastSavedPositionSeconds = -1;

        // Resolve watch progress service from DI lazily
        _watchProgressService ??= App.Services.GetService(typeof(IWatchProgressService)) as IWatchProgressService;
    }

    public void SetPlaylist(string[] filePaths, int startIndex, int[]? episodeIds = null)
    {
        _playlist = filePaths;
        _playlistIndex = startIndex;
        _playlistEpisodeIds = episodeIds ?? Array.Empty<int>();
        NextButton.IsEnabled = startIndex < filePaths.Length - 1;
        Logger.Log($"Playlist set: {filePaths.Length} files, starting at index {startIndex}");
    }

    private async Task PlayNextInPlaylistAsync()
    {
        if (_isTransitioning) return;
        if (_playlistIndex >= _playlist.Length - 1)
        {
            Logger.Log("Playlist ended — no more episodes");
            return;
        }

        _isTransitioning = true;

        // Save/mark completed for the episode we're leaving
        await SaveFinalProgressAsync();

        _playlistIndex++;
        NextButton.IsEnabled = _playlistIndex < _playlist.Length - 1;
        Logger.Log($"Playing next: index {_playlistIndex}/{_playlist.Length - 1}");

        // Update episode context for the new episode
        if (_playlistIndex < _playlistEpisodeIds.Length)
            SetEpisodeContext(_playlistEpisodeIds[_playlistIndex]);

        await TransitionToFileAsync(_playlist[_playlistIndex]);
        _isTransitioning = false;
    }

    /// <summary>
    /// Seamless file transition — sends loadfile directly without stopping,
    /// so mpv transitions from one episode to the next without a visible gap.
    /// </summary>
    private async Task TransitionToFileAsync(string filePath)
    {
        Logger.Log($"=== TransitionToFileAsync: {filePath} ===");

        if (!File.Exists(filePath))
        {
            Logger.LogError($"Next file not found: {filePath}");
            return;
        }

        // Release previous file lock
        if (_lockStream != null)
        {
            await _lockStream.DisposeAsync();
            _lockStream = null;
        }

        _currentFile = Path.GetFileName(filePath);

        // Send loadfile directly — mpv transitions seamlessly without stopping
        var cmd = new[]
        {
            Marshal.StringToHGlobalAnsi("loadfile"),
            Marshal.StringToHGlobalAnsi(filePath),
            IntPtr.Zero
        };
        int cmdResult = LibMpvInterop.mpv_command(_mpvHandle, cmd);
        Logger.Log($"mpv_command(loadfile transition): {cmdResult}");

        foreach (var ptr in cmd)
            if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);

        // Unpause in case it was paused at EOF by keep-open
        LibMpvInterop.mpv_set_property_string(
            _mpvHandle,
            Encoding.UTF8.GetBytes("pause\0"),
            Encoding.UTF8.GetBytes("no\0"));

        // New file lock
        _lockStream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read,
            FileShare.Read, bufferSize: 1, FileOptions.Asynchronous);

        NowPlayingText.Text = _currentFile;
        StatusText.Text = "Playing";
        PlayPauseButton.Content = "Pause";

        // Update audio tracks after the new file loads
        await Task.Delay(1500);
        await UpdateAudioTracksAsync();

        Logger.Log("=== TransitionToFileAsync END ===");
    }

    // ── Audio tracks (moved from MainWindow) ─────────────────

    private async Task UpdateAudioTracksAsync()
    {
        try
        {
            if (_mpvHandle == IntPtr.Zero) return;
            AudioTracksPanel.Children.Clear();
            _audioTrackInfo.Clear();

            var trackListPtr = LibMpvInterop.mpv_get_property_string(
                _mpvHandle, Encoding.UTF8.GetBytes("track-list\0"));
            if (trackListPtr == IntPtr.Zero) return;

            var trackListJson = Marshal.PtrToStringAnsi(trackListPtr);
            LibMpvInterop.mpv_free(trackListPtr);
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

                    // Build a descriptive label combining lang + title when available
                    string label;
                    if (!string.IsNullOrEmpty(lang) && !string.IsNullOrEmpty(title)
                        && !string.Equals(lang, title, StringComparison.OrdinalIgnoreCase))
                        label = $"{lang} — {title}";
                    else if (!string.IsNullOrEmpty(title))
                        label = title;
                    else if (!string.IsNullOrEmpty(lang))
                        label = lang;
                    else
                        label = $"Track {id}";

                    Logger.Log($"Audio track {id}: lang={lang}, title={title} → \"{label}\"");
                    audioTracks.Add((id, label, lang, title));
                    _audioTrackInfo[id] = (lang, title);
                }
            }

            // Check for a saved audio preference for this series
            if (_currentSeriesId > 0 && _libraryService != null)
            {
                var pref = await _libraryService.GetSeriesTrackPreferenceAsync(_currentSeriesId);
                if (pref != null)
                {
                    Logger.Log($"Series {_currentSeriesId} preferred audio: trackId={pref.PreferredAudioTrackId}, lang={pref.PreferredAudioLanguage}, title={pref.PreferredAudioTitle}");
                    var match = MatchPreferredAudioTrack(audioTracks, pref.PreferredAudioLanguage, pref.PreferredAudioTitle, pref.PreferredAudioTrackId);

                    if (match.id > 0)
                    {
                        Logger.Log($"Auto-selecting preferred audio track {match.id} (title={match.title}, lang={match.lang})");
                        LibMpvInterop.mpv_set_property_string(
                            _mpvHandle,
                            Encoding.UTF8.GetBytes("aid\0"),
                            Encoding.UTF8.GetBytes($"{match.id}\0"));
                    }
                    else
                    {
                        Logger.Log($"Preferred audio not available in this file, using default");
                    }
                }
            }

            // Re-read current aid after potential auto-switch
            var aidPtr = LibMpvInterop.mpv_get_property_string(
                _mpvHandle, Encoding.UTF8.GetBytes("aid\0"));
            long currentAid = 0;
            if (aidPtr != IntPtr.Zero)
            {
                var aidStr = Marshal.PtrToStringAnsi(aidPtr);
                LibMpvInterop.mpv_free(aidPtr);
                if (long.TryParse(aidStr, out var aid)) currentAid = aid;
            }

            foreach (var (id, label, _, _) in audioTracks)
            {
                var btn = new Button
                {
                    Content = label,
                    Tag = id,
                    Padding = new Thickness(8, 4),
                    Margin = new Thickness(0, 0, 4, 0),
                    FontSize = 11
                };
                if (id == currentAid) btn.FontWeight = Avalonia.Media.FontWeight.Bold;
                btn.Click += (_, _) => { if (btn.Tag is int tid) SwitchAudioTrack(tid); };
                AudioTracksPanel.Children.Add(btn);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("UpdateAudioTracks", ex);
        }
    }

    /// <summary>
    /// Multi-pass matching for audio track preference across inconsistently-labeled files.
    /// Pass 0: track ID match (most reliable — same position in same encode)
    /// Pass 1: exact title match
    /// Pass 2: exact lang match
    /// Pass 3: normalized language match (jpn=ja=japanese, eng=en=english, etc.)
    /// Pass 4: keyword match in title (e.g. saved "Japanese" matches title "Japanese 2.0 Stereo")
    /// </summary>
    private static (int id, string? lang, string? title) MatchPreferredAudioTrack(
        List<(int id, string label, string? lang, string? title)> tracks,
        string? prefLang, string? prefTitle, int? prefTrackId = null)
    {
        // Pass 0: track ID (mpv track number — most reliable for same-encode series
        // where someone mislabeled both tracks as the same language)
        if (prefTrackId.HasValue && prefTrackId.Value > 0)
        {
            var m = tracks.FirstOrDefault(t => t.id == prefTrackId.Value);
            if (m.id > 0) return (m.id, m.lang, m.title);
        }

        // Pass 1: exact title
        if (!string.IsNullOrEmpty(prefTitle))
        {
            var m = tracks.FirstOrDefault(t =>
                string.Equals(t.title, prefTitle, StringComparison.OrdinalIgnoreCase));
            if (m.title != null) return (m.id, m.lang, m.title);
        }

        // Pass 2: exact lang
        if (!string.IsNullOrEmpty(prefLang))
        {
            var m = tracks.FirstOrDefault(t =>
                string.Equals(t.lang, prefLang, StringComparison.OrdinalIgnoreCase));
            if (m.lang != null) return (m.id, m.lang, m.title);
        }

        // Pass 3: normalized language (jpn/ja/japanese all → "ja", eng/en/english → "en", etc.)
        var normPrefLang = NormalizeLanguage(prefLang);
        var normPrefTitle = NormalizeLanguage(prefTitle);
        var normKey = normPrefLang ?? normPrefTitle;
        if (normKey != null)
        {
            var m = tracks.FirstOrDefault(t =>
                NormalizeLanguage(t.lang) == normKey || NormalizeLanguage(t.title) == normKey);
            if (m.id > 0) return (m.id, m.lang, m.title);
        }

        // Pass 4: keyword / contains match on title
        if (!string.IsNullOrEmpty(prefTitle))
        {
            var m = tracks.FirstOrDefault(t =>
                t.title != null && t.title.Contains(prefTitle, StringComparison.OrdinalIgnoreCase));
            if (m.title != null) return (m.id, m.lang, m.title);

            // Reverse: saved title is substring of track title or vice versa
            m = tracks.FirstOrDefault(t =>
                t.title != null && prefTitle.Contains(t.title, StringComparison.OrdinalIgnoreCase));
            if (m.title != null) return (m.id, m.lang, m.title);
        }

        return default;
    }

    private static readonly Dictionary<string, string> LanguageAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // Japanese
        ["jpn"] = "ja", ["ja"] = "ja", ["japanese"] = "ja", ["日本語"] = "ja", ["jp"] = "ja",
        // English
        ["eng"] = "en", ["en"] = "en", ["english"] = "en",
        // Chinese
        ["zho"] = "zh", ["zh"] = "zh", ["chi"] = "zh", ["chinese"] = "zh",
        ["cmn"] = "zh", ["mandarin"] = "zh",
        // Korean
        ["kor"] = "ko", ["ko"] = "ko", ["korean"] = "ko",
        // Spanish
        ["spa"] = "es", ["es"] = "es", ["spanish"] = "es", ["español"] = "es",
        // French
        ["fre"] = "fr", ["fra"] = "fr", ["fr"] = "fr", ["french"] = "fr", ["français"] = "fr",
        // German
        ["ger"] = "de", ["deu"] = "de", ["de"] = "de", ["german"] = "de", ["deutsch"] = "de",
        // Portuguese
        ["por"] = "pt", ["pt"] = "pt", ["portuguese"] = "pt",
        // Italian
        ["ita"] = "it", ["it"] = "it", ["italian"] = "it",
        // Russian
        ["rus"] = "ru", ["ru"] = "ru", ["russian"] = "ru",
        // Arabic
        ["ara"] = "ar", ["ar"] = "ar", ["arabic"] = "ar",
        // Hindi
        ["hin"] = "hi", ["hi"] = "hi", ["hindi"] = "hi",
        // Thai
        ["tha"] = "th", ["th"] = "th", ["thai"] = "th",
        // Vietnamese
        ["vie"] = "vi", ["vi"] = "vi", ["vietnamese"] = "vi",
        // Indonesian / Malay
        ["ind"] = "id", ["id"] = "id", ["indonesian"] = "id",
        ["msa"] = "ms", ["ms"] = "ms", ["malay"] = "ms",
        // Latin American Spanish
        ["lat"] = "es-la", ["latino"] = "es-la", ["latin spanish"] = "es-la",
        // Brazilian Portuguese
        ["pt-br"] = "pt-br", ["brazilian"] = "pt-br",
        // Undefined / undetermined
        ["und"] = "und", ["undetermined"] = "und",
    };

    private static string? NormalizeLanguage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return LanguageAliases.TryGetValue(trimmed, out var norm) ? norm : null;
    }

    private void SwitchAudioTrack(int trackId)
    {
        if (_mpvHandle == IntPtr.Zero) return;
        LibMpvInterop.mpv_set_property_string(
            _mpvHandle,
            Encoding.UTF8.GetBytes("aid\0"),
            Encoding.UTF8.GetBytes($"{trackId}\0"));

        foreach (var child in AudioTracksPanel.Children)
        {
            if (child is Button btn)
                btn.FontWeight = (btn.Tag is int id && id == trackId)
                    ? Avalonia.Media.FontWeight.Bold
                    : Avalonia.Media.FontWeight.Normal;
        }

        // Persist the language + title + track ID choice for this series
        if (_currentSeriesId > 0 && _libraryService != null
            && _audioTrackInfo.TryGetValue(trackId, out var info))
        {
            var lang = info.lang ?? "";
            Logger.Log($"Saving audio preference for series {_currentSeriesId}: trackId={trackId}, lang={lang}, title={info.title}");
            _ = _libraryService.UpsertSeriesAudioPreferenceAsync(_currentSeriesId, lang, info.title, trackId);
        }
    }

    // ── Seek bar ─────────────────────────────────────────────

    private void StartPositionTimer()
    {
        _positionTimer?.Stop();
        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _positionTimer.Tick += OnPositionTimerTick;
        _positionTimer.Start();
    }

    private void StopPositionTimer()
    {
        _positionTimer?.Stop();
        _positionTimer = null;
    }

    private void OnPositionTimerTick(object? sender, EventArgs e)
    {
        if (_mpvHandle == IntPtr.Zero || !_mpvInitialized) return;

        // In fullscreen, poll cursor position to detect mouse movement over the
        // native video HWND (which eats Avalonia PointerMoved events).
        if (_isFullscreen && GetCursorPos(out var cursorPos))
        {
            if (cursorPos.X != _lastCursorX || cursorPos.Y != _lastCursorY)
            {
                _lastCursorX = cursorPos.X;
                _lastCursorY = cursorPos.Y;
                ShowControls();
                ResetControlsHideTimer();
            }
        }

        // Check if playback reached EOF (keep-open pauses at end)
        var eofReached = GetMpvPropertyString("eof-reached");
        if (eofReached == "yes" && !_isTransitioning && _playlistIndex < _playlist.Length - 1)
        {
            Logger.Log("EOF reached — auto-playing next episode");
            _ = PlayNextInPlaylistAsync();
            return;
        }

        if (_isUserSeeking) return;

        var posStr = GetMpvPropertyString("time-pos");
        var durStr = GetMpvPropertyString("duration");

        if (double.TryParse(posStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var pos)
            && double.TryParse(durStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var dur)
            && dur > 0)
        {
            ProgressSlider.Maximum = dur;
            ProgressSlider.Value = pos;
            TimeCurrentText.Text = FormatTime(pos);
            TimeTotalText.Text = FormatTime(dur);

            // Save progress every ~5 seconds (20 ticks × 250ms)
            _progressSaveCounter++;
            if (_progressSaveCounter >= 20 && _currentEpisodeId > 0 && _watchProgressService != null)
            {
                _progressSaveCounter = 0;
                var posSec = (int)pos;
                var durSec = (int)dur;
                if (posSec != _lastSavedPositionSeconds)
                {
                    _lastSavedPositionSeconds = posSec;
                    _ = _watchProgressService.SaveProgressAsync(_currentEpisodeId, posSec, durSec);
                }
            }
        }
    }

    private void OnSliderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _isUserSeeking = true;
    }

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
        if (_mpvHandle == IntPtr.Zero) return;
        var cmd = new[]
        {
            Marshal.StringToHGlobalAnsi("seek"),
            Marshal.StringToHGlobalAnsi(seconds.ToString("F1", CultureInfo.InvariantCulture)),
            Marshal.StringToHGlobalAnsi("absolute"),
            IntPtr.Zero
        };
        int result = LibMpvInterop.mpv_command(_mpvHandle, cmd);
        Logger.Log($"Seek to {seconds:F1}s: {result}");
        foreach (var ptr in cmd)
            if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);
    }

    private string? GetMpvPropertyString(string name)
    {
        var ptr = LibMpvInterop.mpv_get_property_string(_mpvHandle, Encoding.UTF8.GetBytes(name + "\0"));
        if (ptr == IntPtr.Zero) return null;
        var val = Marshal.PtrToStringAnsi(ptr);
        LibMpvInterop.mpv_free(ptr);
        return val;
    }

    private static string FormatTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(seconds, 0));
        return ts.Hours > 0 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
    }

    // ── Controls auto-hide (fullscreen only) ───────────────

    public void SetFullscreen(bool fullscreen)
    {
        _isFullscreen = fullscreen;
        if (_isFullscreen)
        {
            // Entering fullscreen — start the hide countdown
            ResetControlsHideTimer();
        }
        else
        {
            // Leaving fullscreen — stop hiding, ensure controls visible
            StopControlsHideTimer();
        }
    }

    private void OnPlayerPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isFullscreen) return;
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
        if (!_isFullscreen || _mouseOverControls || _currentFile == null) return;
        ControlsBar.IsVisible = false;
        Cursor = new Cursor(StandardCursorType.None);
    }

    private void ResetControlsHideTimer()
    {
        _controlsHideTimer?.Stop();
        if (!_isFullscreen) return;
        _controlsHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _controlsHideTimer.Tick += (_, _) =>
        {
            _controlsHideTimer?.Stop();
            HideControls();
        };
        _controlsHideTimer.Start();
    }

    private void StopControlsHideTimer()
    {
        _controlsHideTimer?.Stop();
        _controlsHideTimer = null;
        ShowControls();
    }

    // ── Watch progress persistence ─────────────────────────

    private async Task SaveFinalProgressAsync()
    {
        if (_currentEpisodeId <= 0 || _watchProgressService == null || _mpvHandle == IntPtr.Zero)
            return;

        try
        {
            var posStr = GetMpvPropertyString("time-pos");
            var durStr = GetMpvPropertyString("duration");
            if (double.TryParse(posStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var pos)
                && double.TryParse(durStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var dur)
                && dur > 0)
            {
                var posSec = (int)pos;
                var durSec = (int)dur;

                // Mark completed if within last 5% or last 120 seconds
                if (pos >= dur * 0.95 || dur - pos < 120)
                {
                    Logger.Log($"Marking episode {_currentEpisodeId} as completed ({pos:F0}/{dur:F0}s)");
                    await _watchProgressService.MarkCompletedAsync(_currentEpisodeId);
                }
                else
                {
                    Logger.Log($"Saving final progress for episode {_currentEpisodeId}: {posSec}/{durSec}s");
                    await _watchProgressService.SaveProgressAsync(_currentEpisodeId, posSec, durSec);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to save final progress: {ex.Message}");
        }
    }

    // ── Cleanup ──────────────────────────────────────────────

    private async Task CleanupCurrentFileAsync()
    {
        // Save final watch position before stopping
        await SaveFinalProgressAsync();

        StopPositionTimer();
        StopControlsHideTimer();

        if (_lockStream != null)
        {
            await _lockStream.DisposeAsync();
            _lockStream = null;
        }

        if (_mpvHandle != IntPtr.Zero)
        {
            try
            {
                var cmd = new[] { Marshal.StringToHGlobalAnsi("stop"), IntPtr.Zero };
                LibMpvInterop.mpv_command(_mpvHandle, cmd);
                foreach (var ptr in cmd)
                    if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);
            }
            catch { }
        }

        _currentFile = null;
        NowPlayingText.Text = "No file loaded";
        StatusText.Text = "Stopped";
        AudioTracksPanel.Children.Clear();
        PlaceholderText.IsVisible = true;
        PlayPauseButton.Content = "Play";
        ProgressSlider.Value = 0;
        ProgressSlider.Maximum = 100;
        TimeCurrentText.Text = "0:00";
        TimeTotalText.Text = "0:00";
    }

    // ── Event polling ────────────────────────────────────────

    private void PollMpvEvents()
    {
        try
        {
            for (int i = 0; i < 20; i++)
            {
                IntPtr eventPtr = LibMpvInterop.mpv_wait_event(_mpvHandle, 0);
                if (eventPtr == IntPtr.Zero) break;
                int eventId = Marshal.ReadInt32(eventPtr);
                if (eventId == 0) break;

                string eventName = eventId switch
                {
                    1 => "SHUTDOWN", 3 => "LOG_MESSAGE", 6 => "START_FILE",
                    7 => "END_FILE", 9 => "FILE_LOADED", 16 => "PLAYBACK_RESTART",
                    20 => "PROPERTY_CHANGE", _ => $"EVENT_{eventId}"
                };
                Logger.Log($"MPV Event: {eventName} (id={eventId})");

                if (eventId == 7) // END_FILE
                {
                    try
                    {
                        int error = Marshal.ReadInt32(eventPtr, 4);
                        if (error != 0)
                        {
                            IntPtr errPtr = LibMpvInterop.mpv_error_string(error);
                            string? errStr = errPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(errPtr) : "unknown";
                            Logger.Log($"  END_FILE error: {error} ({errStr})");
                        }
                        long reason = Marshal.ReadInt64(eventPtr, 8);
                        string reasonStr = reason switch
                        {
                            0 => "EOF", 2 => "STOP", 3 => "QUIT",
                            4 => "ERROR", 5 => "REDIRECT", _ => $"reason={reason}"
                        };
                        Logger.Log($"  END_FILE reason: {reasonStr}");
                    }
                    catch (Exception ex) { Logger.Log($"  parse fail: {ex.Message}"); }
                }

                if (eventId == 3) // LOG_MESSAGE
                {
                    try
                    {
                        IntPtr prefixPtr = Marshal.ReadIntPtr(eventPtr, 8);
                        IntPtr levelPtr = Marshal.ReadIntPtr(eventPtr, 8 + IntPtr.Size);
                        IntPtr textPtr = Marshal.ReadIntPtr(eventPtr, 8 + IntPtr.Size * 2);
                        string? prefix = prefixPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(prefixPtr) : "";
                        string? level = levelPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(levelPtr) : "";
                        string? text = textPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(textPtr) : "";
                        if (!string.IsNullOrWhiteSpace(text))
                            Logger.Log($"  MPV [{level}] {prefix}: {text}");
                    }
                    catch (Exception ex) { Logger.Log($"  log parse fail: {ex.Message}"); }
                }
            }
        }
        catch (Exception ex) { Logger.LogError("PollMpvEvents", ex); }
    }

    // ── Public cleanup for MainWindow.Closing ────────────────

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
