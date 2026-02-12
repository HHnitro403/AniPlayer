using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

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

    public event Action? PlaybackStopped;

    public PlayerPage()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;

        // Tunnel handlers so we catch pointer events before the slider's internal handling
        ProgressSlider.AddHandler(PointerPressedEvent, OnSliderPointerPressed, RoutingStrategies.Tunnel);
        ProgressSlider.AddHandler(PointerReleasedEvent, OnSliderPointerReleased, RoutingStrategies.Tunnel);
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
                VideoHostControl.InitializeRenderer(_mpvHandle);
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

    private void InitializeMpv()
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

            VideoHostControl.InitializeRenderer(_mpvHandle);
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

        await Task.Delay(500);
        PollMpvEvents();

        await Task.Delay(1000);
        await UpdateAudioTracksAsync();

        Logger.Log("=== LoadFileAsync END ===");
    }

    // ── Transport controls ───────────────────────────────────

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

    // ── Audio tracks (moved from MainWindow) ─────────────────

    private async Task UpdateAudioTracksAsync()
    {
        try
        {
            if (_mpvHandle == IntPtr.Zero) return;
            AudioTracksPanel.Children.Clear();

            var trackListPtr = LibMpvInterop.mpv_get_property_string(
                _mpvHandle, Encoding.UTF8.GetBytes("track-list\0"));
            if (trackListPtr == IntPtr.Zero) return;

            var trackListJson = Marshal.PtrToStringAnsi(trackListPtr);
            LibMpvInterop.mpv_free(trackListPtr);
            if (string.IsNullOrEmpty(trackListJson)) return;

            var tracks = JsonDocument.Parse(trackListJson);
            var audioTracks = new List<(int id, string label)>();

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
                    audioTracks.Add((id, label));
                }
            }

            var aidPtr = LibMpvInterop.mpv_get_property_string(
                _mpvHandle, Encoding.UTF8.GetBytes("aid\0"));
            long currentAid = 0;
            if (aidPtr != IntPtr.Zero)
            {
                var aidStr = Marshal.PtrToStringAnsi(aidPtr);
                LibMpvInterop.mpv_free(aidPtr);
                if (long.TryParse(aidStr, out var aid)) currentAid = aid;
            }

            foreach (var (id, label) in audioTracks)
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
        await Task.CompletedTask;
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
        if (_mpvHandle == IntPtr.Zero || !_mpvInitialized || _isUserSeeking) return;

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

    // ── Cleanup ──────────────────────────────────────────────

    private async Task CleanupCurrentFileAsync()
    {
        StopPositionTimer();

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
