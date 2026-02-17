using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading; // [Changed] Replaces System.Timers
using Aniplayer.Core.Helpers;
using Aniplayer.Core.Interfaces;
using Aniplayer.Core.Models;

namespace AniPlayer.UI.Services;

public class PlayerService : Aniplayer.Core.Interfaces.IPlayerService, IDisposable
{
    private readonly ILibraryService _libraryService;
    private readonly IWatchProgressService _watchProgressService;

    private IntPtr _mpvHandle;
    private bool _mpvInitialized;

    // [Changed] Use DispatcherTimer for UI-safe updates
    private DispatcherTimer? _positionTimer;

    private IReadOnlyList<Episode> _playlist = Array.Empty<Episode>();
    private int _playlistIndex = -1;

    private bool _isTransitioning;
    private bool _isDisposed;

    public Episode? CurrentEpisode { get; private set; }
    public bool IsPlaying { get; private set; }
    public double Position { get; private set; }
    public double Duration { get; private set; }
    public IReadOnlyList<(int Id, string Name)> AudioTracks { get; private set; } = Array.Empty<(int, string)>();
    public int CurrentAudioTrackId { get; private set; }

    public event Action? StateChanged;
    public event Action? PositionChanged;
    public event Action<Episode>? EpisodeChanged;
    public event Action? TracksChanged;
    public event Action? PlaybackEnded;

    public PlayerService(ILibraryService libraryService, IWatchProgressService watchProgressService)
    {
        _libraryService = libraryService;
        _watchProgressService = watchProgressService;
    }

    public Task InitializeAsync(VideoHost videoHost)
    {
        if (_mpvInitialized) return Task.CompletedTask;

        Logger.Log("[PlayerService] Initializing MPV...");
        _mpvHandle = LibMpvInterop.mpv_create();
        if (_mpvHandle == IntPtr.Zero)
        {
            Logger.LogError("[PlayerService] mpv_create failed.");
            return Task.CompletedTask;
        }

        // Must be set before mpv_initialize
        SetOption("vo", "libmpv");

        var initResult = LibMpvInterop.mpv_initialize(_mpvHandle);
        if (initResult < 0)
        {
            Logger.LogError($"[PlayerService] mpv_initialize failed with error: {initResult}");
            return Task.CompletedTask;
        }

        videoHost.InitializeRenderer(_mpvHandle, false);

        _mpvInitialized = true;
        Logger.Log("[PlayerService] MPV Initialized successfully.");

        StartPositionTimer();
        return Task.CompletedTask;
    }

    public async Task LoadPlaylistAsync(IReadOnlyList<Episode> playlist, int startIndex)
    {
        if (!playlist.Any() || startIndex < 0 || startIndex >= playlist.Count)
        {
            Logger.LogError("[PlayerService] Invalid playlist or start index.");
            return;
        }

        await StopAsync(appShutdown: false);
        _playlist = playlist;
        await ChangeToEpisodeAsync(playlist[startIndex], isInitialLoad: true);
    }

    public void TogglePlayPause()
    {
        if (IsPlaying) Pause();
        else Play();
    }

    public void Play()
    {
        if (CurrentEpisode == null) return;
        SetOption("pause", "no");
    }

    public void Pause()
    {
        if (CurrentEpisode == null) return;
        SetOption("pause", "yes");
    }

    public async void PlayNext()
    {
        if (_isTransitioning || _playlistIndex >= _playlist.Count - 1)
        {
            if (!_isTransitioning) PlaybackEnded?.Invoke();
            return;
        }

        await SaveCurrentProgressAsync(markCompleted: true);

        var nextIndex = _playlistIndex + 1;
        Logger.Log($"[PlayerService] Playing next: index {nextIndex}");
        await ChangeToEpisodeAsync(_playlist[nextIndex]);
    }

    public void Seek(double position)
    {
        SetCommand("seek", position.ToString(CultureInfo.InvariantCulture), "absolute+keyframes");
    }

    public void SetAudioTrack(int trackId)
    {
        SetOption("aid", trackId.ToString());
        CurrentAudioTrackId = trackId;
        TracksChanged?.Invoke();

        if (CurrentEpisode != null)
        {
            var track = AudioTracks.FirstOrDefault(t => t.Id == trackId);
            _ = _libraryService.UpsertSeriesAudioPreferenceAsync(CurrentEpisode.SeriesId, "", track.Name, trackId);
        }
    }

    private async Task ChangeToEpisodeAsync(Episode newEpisode, bool isInitialLoad = false)
    {
        if (_isTransitioning) return;
        _isTransitioning = true;

        if (!isInitialLoad && CurrentEpisode != null)
        {
            Logger.Log($"[PlayerService] Saving final progress for old episode {CurrentEpisode.Id}.");
            await SaveCurrentProgressAsync(force: true);
        }

        Logger.Log($"[PlayerService] Changing to new episode {newEpisode.Id}.");
        CurrentEpisode = newEpisode;
        _playlistIndex = _playlist.ToList().FindIndex(e => e.Id == newEpisode.Id);

        await LoadFileIntoMpvAsync(newEpisode.FilePath);

        _isTransitioning = false;
        EpisodeChanged?.Invoke(newEpisode);
    }

    private async Task LoadFileIntoMpvAsync(string filePath)
    {
        if (!_mpvInitialized || !File.Exists(filePath))
        {
            Logger.LogError($"[PlayerService] Pre-flight check failed for '{filePath}'");
            return;
        }

        SetCommand("loadfile", filePath);

        // [Changed] Replaced "Magic Number" delay (1500ms) with smart polling.
        // We wait for the 'duration' property to become valid (> 0), indicating metadata is loaded.
        // This prevents race conditions where chapters/tracks were checked before the file was ready.

        var attempts = 0;
        const int maxAttempts = 100; // 10 seconds timeout (100 * 100ms)

        while (attempts < maxAttempts)
        {
            var duration = GetDoubleProperty("duration");
            if (duration > 0)
            {
                break; // File is ready!
            }
            await Task.Delay(100);
            attempts++;
        }

        if (attempts >= maxAttempts)
        {
            Logger.LogError($"[PlayerService] Timed out waiting for file metadata (duration). Proceeding with limited info.");
        }

        UpdatePlayerState(); // Update Duration/Position properties now that file is loaded
        await AnalyzeFileChapters();
        await UpdateAudioTracksAsync();

        var progress = await _watchProgressService.GetProgressByEpisodeIdAsync(CurrentEpisode!.Id);
        if (progress != null && !progress.IsCompleted && progress.PositionSeconds > 5)
        {
            Logger.Log($"[PlayerService] Resuming episode {CurrentEpisode.Id} from {progress.PositionSeconds}s");
            Seek(progress.PositionSeconds);
        }
    }

    // [Changed] Signature matches DispatcherTimer.Tick (EventHandler)
    private void OnPositionTimerTick(object? sender, EventArgs e)
    {
        if (CurrentEpisode == null) return;

        UpdatePlayerState();

        if (GetMpvPropertyString("eof-reached") == "yes")
        {
            PlayNext();
            return;
        }

        _ = SaveCurrentProgressAsync();
    }

    private void UpdatePlayerState()
    {
        IsPlaying = GetMpvPropertyString("pause") == "no";
        Position = GetDoubleProperty("time-pos");
        Duration = GetDoubleProperty("duration");

        // These events are now safe to consume by the UI because DispatcherTimer runs on the UI Thread
        StateChanged?.Invoke();
        if (Duration > 0) PositionChanged?.Invoke();
    }

    private async Task SaveCurrentProgressAsync(bool force = false, bool markCompleted = false)
    {
        if (CurrentEpisode == null || Duration <= 0) return;

        bool shouldMarkCompleted = markCompleted || Position >= Duration * 0.95 || (Duration > 30 && Duration - Position < 30);

        if (shouldMarkCompleted)
        {
            await _watchProgressService.MarkCompletedAsync(CurrentEpisode.Id);
        }
        else
        {
            await _watchProgressService.UpdateProgressAsync(CurrentEpisode.Id, (int)Position, (int)Duration, force);
        }
    }

    // [Changed] Replaced System.Timers initialization with DispatcherTimer
    private void StartPositionTimer()
    {
        _positionTimer?.Stop();

        _positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _positionTimer.Tick += OnPositionTimerTick;
        _positionTimer.Start();
    }

    private async Task AnalyzeFileChapters()
    {
        if (CurrentEpisode == null) return;
        var chapterListJson = GetMpvPropertyString("chapter-list");
        if (string.IsNullOrEmpty(chapterListJson)) return;

        var rawChapters = new List<Chapters.ChapterInfo>();
        try
        {
            using var chaptersDoc = JsonDocument.Parse(chapterListJson);
            foreach (var chapter in chaptersDoc.RootElement.EnumerateArray())
            {
                var title = chapter.TryGetProperty("title", out var t) ? t.GetString() : "Chapter";
                var time = chapter.TryGetProperty("time", out var ts) ? ts.GetDouble() : 0;
                rawChapters.Add(new Chapters.ChapterInfo(title ?? "", time));
            }
            if (Duration > 0)
            {
                Chapters.Detect(CurrentEpisode, rawChapters, Duration);
            }
        }
        catch (Exception ex) { Logger.LogError("AnalyzeFileChapters failed", ex); }
    }

    private async Task UpdateAudioTracksAsync()
    {
        if (CurrentEpisode == null) return;
        var trackListJson = GetMpvPropertyString("track-list");
        if (string.IsNullOrEmpty(trackListJson)) return;

        var audioTracks = new List<(int, string)>();
        try
        {
            using var tracks = JsonDocument.Parse(trackListJson);
            foreach (var track in tracks.RootElement.EnumerateArray())
            {
                if (track.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "audio")
                {
                    var id = track.GetProperty("id").GetInt32();
                    var lang = track.TryGetProperty("lang", out var l) ? l.GetString() : null;
                    var title = track.TryGetProperty("title", out var t) ? t.GetString() : null;
                    var label = !string.IsNullOrEmpty(lang) && !string.IsNullOrEmpty(title) ? $"{lang} — {title}"
                        : title ?? lang ?? $"Track {id}";
                    audioTracks.Add((id, label));
                }
            }
        }
        catch (Exception ex) { Logger.LogError("UpdateAudioTracks failed", ex); }

        AudioTracks = audioTracks;

        var pref = await _libraryService.GetSeriesTrackPreferenceAsync(CurrentEpisode.SeriesId);
        if (pref?.PreferredAudioTrackId != null)
        {
            SetAudioTrack(pref.PreferredAudioTrackId.Value);
        }

        CurrentAudioTrackId = (int)GetDoubleProperty("aid");
        TracksChanged?.Invoke();
    }

    private async Task StopAsync(bool appShutdown = false)
    {
        if (CurrentEpisode != null)
        {
            await SaveCurrentProgressAsync(force: true);
        }
        if (!appShutdown)
        {
            SetCommand("stop");
            CurrentEpisode = null;
            IsPlaying = false;
            StateChanged?.Invoke();
        }
    }

    public async Task ShutdownAsync()
    {
        await StopAsync(true);
        Dispose();
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        // [Changed] Stop the DispatcherTimer
        _positionTimer?.Stop();

        if (_mpvHandle != IntPtr.Zero)
        {
            LibMpvInterop.mpv_terminate_destroy(_mpvHandle);
            _mpvHandle = IntPtr.Zero;
        }
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }

    private void SetOption(string name, string value)
    {
        if (_mpvHandle == IntPtr.Zero) return;
        LibMpvInterop.mpv_set_option_string(_mpvHandle, Encoding.UTF8.GetBytes(name + "\0"), Encoding.UTF8.GetBytes(value + "\0"));
    }

    private void SetCommand(string name, string? value = null, string? value2 = null)
    {
        if (_mpvHandle == IntPtr.Zero) return;
        var args = new List<string?> { name, value, value2 }.Where(s => s != null).ToList();
        var cmd = args.Select(s => Marshal.StringToHGlobalAnsi(s!)).Concat(new[] { IntPtr.Zero }).ToArray();

        try { LibMpvInterop.mpv_command(_mpvHandle, cmd); }
        finally { foreach (var ptr in cmd) if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr); }
    }

    private string? GetMpvPropertyString(string name)
    {
        if (_mpvHandle == IntPtr.Zero) return null;
        var ptr = LibMpvInterop.mpv_get_property_string(_mpvHandle, Encoding.UTF8.GetBytes(name + "\0"));
        if (ptr == IntPtr.Zero) return null;
        var val = Marshal.PtrToStringAnsi(ptr);
        LibMpvInterop.mpv_free(ptr);
        return val;
    }

    private double GetDoubleProperty(string name)
    {
        var valueStr = GetMpvPropertyString(name);
        if (double.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            return value;
        }
        return 0;
    }
}