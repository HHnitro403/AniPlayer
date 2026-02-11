using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AniPlayer.UI
{
    public partial class MainWindow : Window
    {
        private IntPtr _mpvHandle;
        private FileStream? _lockStream;
        private string? _currentFile;
        private bool _mpvInitialized = false;

        public MainWindow()
        {
            Logger.Log("MainWindow constructor called");
            InitializeComponent();
            Closing += MainWindow_Closing;
            Opened += MainWindow_Opened;
            Logger.Log("MainWindow constructor completed");
        }

        private async void MainWindow_Opened(object? sender, EventArgs e)
        {
            Logger.Log("MainWindow_Opened event fired");
            StatusText.Text = $"Log: {Logger.GetLogFilePath()}";

            // Wait for window and video host to be fully rendered
            Logger.Log("Waiting 500ms for UI to render...");
            await Task.Delay(500);

            Logger.Log("Calling InitializeMpv...");
            InitializeMpv();
        }

        private void InitializeMpv()
        {
            Logger.Log("=== InitializeMpv START ===");

            try
            {
                // Wait for VideoHostControl to be initialized
                Logger.Log($"Checking VideoHostControl.NativeHandle...");
                if (VideoHostControl.NativeHandle == IntPtr.Zero)
                {
                    Logger.Log("VideoHostControl.NativeHandle is Zero, retrying in 500ms");
                    StatusText.Text = "Status: Waiting for video surface...";
                    // Retry after a delay
                    Task.Delay(500).ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.Post(InitializeMpv));
                    return;
                }

                // Create mpv instance
                Logger.Log("Creating mpv instance...");
                _mpvHandle = LibMpvInterop.mpv_create();
                Logger.Log($"mpv_create() returned handle: {_mpvHandle} (0x{_mpvHandle.ToString("X")})");

                if (_mpvHandle == IntPtr.Zero)
                {
                    Logger.LogError("Failed to create mpv instance - mpv_create returned NULL");
                    StatusText.Text = "Status: Error - Failed to create mpv instance";
                    return;
                }

                // Set video output to libmpv for render API (don't use wid)
                Logger.Log("Setting video output to libmpv for render API...");
                int voResult = LibMpvInterop.mpv_set_option_string(
                    _mpvHandle,
                    Encoding.UTF8.GetBytes("vo\0"),
                    Encoding.UTF8.GetBytes("libmpv\0"));
                Logger.Log($"mpv_set_option_string(vo=libmpv) returned: {voResult}");

                if (voResult != 0)
                {
                    Logger.LogError("Failed to set video output to libmpv");
                    StatusText.Text = "Status: Error - Failed to set video output";
                    return;
                }

                // Request log messages from MPV
                Logger.Log("Requesting MPV log messages (info level)...");
                LibMpvInterop.mpv_request_log_messages(_mpvHandle, Encoding.UTF8.GetBytes("info\0"));

                // Set some additional options for better embedded playback
                Logger.Log("Setting input-default-bindings option...");
                int bindingsResult = LibMpvInterop.mpv_set_option_string(
                    _mpvHandle,
                    Encoding.UTF8.GetBytes("input-default-bindings\0"),
                    Encoding.UTF8.GetBytes("yes\0"));
                Logger.Log($"mpv_set_option_string(input-default-bindings) returned: {bindingsResult}");

                Logger.Log("Setting input-vo-keyboard option...");
                int keyboardResult = LibMpvInterop.mpv_set_option_string(
                    _mpvHandle,
                    Encoding.UTF8.GetBytes("input-vo-keyboard\0"),
                    Encoding.UTF8.GetBytes("yes\0"));
                Logger.Log($"mpv_set_option_string(input-vo-keyboard) returned: {keyboardResult}");

                Logger.Log("Setting keep-open option...");
                int keepOpenResult = LibMpvInterop.mpv_set_option_string(
                    _mpvHandle,
                    Encoding.UTF8.GetBytes("keep-open\0"),
                    Encoding.UTF8.GetBytes("yes\0"));
                Logger.Log($"mpv_set_option_string(keep-open) returned: {keepOpenResult}");

                // Don't pause on load
                Logger.Log("Setting pause option to no...");
                int pauseResult = LibMpvInterop.mpv_set_option_string(
                    _mpvHandle,
                    Encoding.UTF8.GetBytes("pause\0"),
                    Encoding.UTF8.GetBytes("no\0"));
                Logger.Log($"mpv_set_option_string(pause) returned: {pauseResult}");

                // Initialize mpv
                Logger.Log("Calling mpv_initialize...");
                int result = LibMpvInterop.mpv_initialize(_mpvHandle);
                Logger.Log($"mpv_initialize() returned: {result}");

                if (result < 0)
                {
                    Logger.LogError($"Failed to initialize mpv - error code: {result}");
                    StatusText.Text = $"Status: Error - Failed to initialize mpv (code: {result})";
                    return;
                }

                // Now initialize the render context
                Logger.Log("Initializing MPV render context via VideoHost...");
                VideoHostControl.InitializeRenderer(_mpvHandle);

                if (VideoHostControl.Renderer == null || !VideoHostControl.Renderer.IsInitialized)
                {
                    Logger.LogError("Failed to initialize render context");
                    StatusText.Text = "Status: Error - Failed to initialize renderer";
                    return;
                }

                _mpvInitialized = true;
                StatusText.Text = "Status: Ready";
                Logger.Log("MPV initialized successfully with render API");
                Logger.Log("=== InitializeMpv END (SUCCESS) ===");
            }
            catch (Exception ex)
            {
                Logger.LogError("InitializeMpv exception", ex);
                StatusText.Text = $"Status: Error - {ex.Message}";
            }
        }

        private async void OpenFileButton_Click(object? sender, RoutedEventArgs e)
        {
            Logger.Log("=== OpenFileButton_Click START ===");

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null)
            {
                Logger.LogError("Failed to get TopLevel");
                return;
            }

            Logger.Log("Opening file picker...");
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Video File",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Video Files")
                    {
                        Patterns = new[] { "*.mkv", "*.mp4", "*.avi", "*.m4v", "*.mov" }
                    }
                }
            });

            if (files.Count > 0)
            {
                var filePath = files[0].Path.LocalPath;
                Logger.Log($"File selected: {filePath}");
                await LoadVideoAsync(filePath);
            }
            else
            {
                Logger.Log("No file selected");
            }

            Logger.Log("=== OpenFileButton_Click END ===");
        }

        private async Task LoadVideoAsync(string filePath)
        {
            Logger.Log("=== LoadVideoAsync START ===");
            Logger.Log($"File path: {filePath}");

            try
            {
                Logger.Log($"MPV initialized: {_mpvInitialized}, MPV handle: {_mpvHandle}");

                if (!_mpvInitialized || _mpvHandle == IntPtr.Zero)
                {
                    Logger.LogError("MPV not initialized");
                    StatusText.Text = "Status: Error - MPV not initialized";
                    return;
                }

                Logger.Log("Checking if file exists...");
                if (!File.Exists(filePath))
                {
                    Logger.LogError($"File does not exist: {filePath}");
                    StatusText.Text = "Status: Error - File not found";
                    return;
                }
                Logger.Log("File exists");

                // Clean up previous file if any
                Logger.Log("Cleaning up previous file...");
                await CleanupCurrentFileAsync();

                // Acquire OS-level file lock — FileShare.Read so mpv can still open the file
                Logger.Log("Acquiring file lock...");
                _lockStream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
                Logger.Log("File lock acquired");

                _currentFile = Path.GetFileName(filePath);

                // Load the file into mpv using command
                Logger.Log("Preparing mpv loadfile command...");
                var cmd = new[] {
                    Marshal.StringToHGlobalAnsi("loadfile"),
                    Marshal.StringToHGlobalAnsi(filePath),
                    IntPtr.Zero
                };
                Logger.Log($"Command pointers: loadfile={cmd[0]}, path={cmd[1]}");

                Logger.Log("Calling mpv_command(loadfile)...");
                int cmdResult = LibMpvInterop.mpv_command(_mpvHandle, cmd);
                Logger.Log($"mpv_command returned: {cmdResult}");

                // Free command pointers
                foreach (var ptr in cmd)
                {
                    if (ptr != IntPtr.Zero)
                        Marshal.FreeHGlobal(ptr);
                }

                // Explicitly set pause to false after loading
                Logger.Log("Setting pause property to no...");
                LibMpvInterop.mpv_set_property_string(
                    _mpvHandle,
                    Encoding.UTF8.GetBytes("pause\0"),
                    Encoding.UTF8.GetBytes("no\0"));

                // Trigger an initial render to kick off the render loop
                Logger.Log("Triggering initial render...");
                VideoHostControl.Renderer?.Render();

                // Update status
                StatusText.Text = "Status: Playing";
                LockText.Text = $"Lock: HELD — {_currentFile}";
                Logger.Log("Status updated to Playing");

                // Hide placeholder text when video is playing
                PlaceholderText.IsVisible = false;
                Logger.Log("Placeholder text hidden");

                // Wait a bit for mpv to initialize the file
                Logger.Log("Waiting 500ms for mpv to load file...");
                await Task.Delay(500);

                // Check playback status
                Logger.Log("Checking playback status...");
                var pausedPtr = LibMpvInterop.mpv_get_property_string(_mpvHandle, Encoding.UTF8.GetBytes("pause\0"));
                if (pausedPtr != IntPtr.Zero)
                {
                    var pausedStr = Marshal.PtrToStringAnsi(pausedPtr);
                    Logger.Log($"Pause status: {pausedStr}");
                    LibMpvInterop.mpv_free(pausedPtr);
                }

                var coreIdlePtr = LibMpvInterop.mpv_get_property_string(_mpvHandle, Encoding.UTF8.GetBytes("core-idle\0"));
                if (coreIdlePtr != IntPtr.Zero)
                {
                    var coreIdleStr = Marshal.PtrToStringAnsi(coreIdlePtr);
                    Logger.Log($"Core-idle status: {coreIdleStr}");
                    LibMpvInterop.mpv_free(coreIdlePtr);
                }

                Logger.Log("Waiting additional 1000ms...");
                await Task.Delay(1000);

                // Poll MPV events to see if there are errors
                Logger.Log("Polling MPV events for errors...");
                PollMpvEvents();

                // Check if file actually loaded
                Logger.Log("Checking if video track exists...");
                var vidPtr = LibMpvInterop.mpv_get_property_string(_mpvHandle, Encoding.UTF8.GetBytes("vid\0"));
                if (vidPtr != IntPtr.Zero)
                {
                    var vidStr = Marshal.PtrToStringAnsi(vidPtr);
                    Logger.Log($"Video track ID: {vidStr}");
                    LibMpvInterop.mpv_free(vidPtr);
                }
                else
                {
                    Logger.LogError("No video track found!");
                }

                var durationPtr = LibMpvInterop.mpv_get_property_string(_mpvHandle, Encoding.UTF8.GetBytes("duration\0"));
                if (durationPtr != IntPtr.Zero)
                {
                    var durationStr = Marshal.PtrToStringAnsi(durationPtr);
                    Logger.Log($"Video duration: {durationStr} seconds");
                    LibMpvInterop.mpv_free(durationPtr);
                }

                // Enumerate and display audio tracks
                Logger.Log("Updating audio tracks...");
                await UpdateAudioTracksAsync();

                Logger.Log("=== LoadVideoAsync END (SUCCESS) ===");
            }
            catch (Exception ex)
            {
                Logger.LogError("LoadVideoAsync exception", ex);
                StatusText.Text = $"Status: Error - {ex.Message}";
                await CleanupCurrentFileAsync();
            }
        }

        private async Task UpdateAudioTracksAsync()
        {
            try
            {
                if (_mpvHandle == IntPtr.Zero) return;

                // Clear existing buttons
                AudioTracksPanel.Children.Clear();

                // Get track list from mpv
                var trackListPtr = LibMpvInterop.mpv_get_property_string(_mpvHandle, Encoding.UTF8.GetBytes("track-list\0"));
                if (trackListPtr == IntPtr.Zero) return;

                var trackListJson = Marshal.PtrToStringAnsi(trackListPtr);
                LibMpvInterop.mpv_free(trackListPtr);

                if (string.IsNullOrEmpty(trackListJson)) return;

                // Parse the JSON track list
                var tracks = JsonDocument.Parse(trackListJson);
                var audioTracks = new List<(int id, string label)>();

                foreach (var track in tracks.RootElement.EnumerateArray())
                {
                    if (track.TryGetProperty("type", out var typeElement) &&
                        typeElement.GetString() == "audio")
                    {
                        var id = track.GetProperty("id").GetInt32();
                        var lang = track.TryGetProperty("lang", out var langElement)
                            ? langElement.GetString()
                            : null;
                        var title = track.TryGetProperty("title", out var titleElement)
                            ? titleElement.GetString()
                            : null;

                        var label = lang ?? title ?? $"Track {id}";
                        audioTracks.Add((id, label));
                    }
                }

                // Get current audio track ID
                var aidPtr = LibMpvInterop.mpv_get_property_string(_mpvHandle, Encoding.UTF8.GetBytes("aid\0"));
                long currentAid = 0;
                if (aidPtr != IntPtr.Zero)
                {
                    var aidStr = Marshal.PtrToStringAnsi(aidPtr);
                    LibMpvInterop.mpv_free(aidPtr);
                    if (!string.IsNullOrEmpty(aidStr) && long.TryParse(aidStr, out var aid))
                    {
                        currentAid = aid;
                    }
                }

                // Create buttons for each audio track
                foreach (var (id, label) in audioTracks)
                {
                    var button = new Button
                    {
                        Content = label,
                        Tag = id,
                        Padding = new Avalonia.Thickness(10, 5),
                        Margin = new Avalonia.Thickness(0, 0, 5, 0)
                    };

                    // Highlight current track
                    if (id == currentAid)
                    {
                        button.FontWeight = Avalonia.Media.FontWeight.Bold;
                    }

                    button.Click += (s, e) =>
                    {
                        if (button.Tag is int trackId)
                        {
                            SwitchAudioTrack(trackId);
                        }
                    };

                    AudioTracksPanel.Children.Add(button);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to enumerate audio tracks: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        private void SwitchAudioTrack(int trackId)
        {
            try
            {
                if (_mpvHandle == IntPtr.Zero) return;

                LibMpvInterop.mpv_set_property_string(
                    _mpvHandle,
                    Encoding.UTF8.GetBytes("aid\0"),
                    Encoding.UTF8.GetBytes($"{trackId}\0"));

                // Update button highlighting
                foreach (var child in AudioTracksPanel.Children)
                {
                    if (child is Button btn)
                    {
                        btn.FontWeight = (btn.Tag is int id && id == trackId)
                            ? Avalonia.Media.FontWeight.Bold
                            : Avalonia.Media.FontWeight.Normal;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to switch audio track: {ex.Message}");
            }
        }

        private async Task CleanupCurrentFileAsync()
        {
            // Dispose file lock
            if (_lockStream != null)
            {
                await _lockStream.DisposeAsync();
                _lockStream = null;
            }

            // Stop mpv playback
            if (_mpvHandle != IntPtr.Zero)
            {
                try
                {
                    var cmd = new[] {
                        Marshal.StringToHGlobalAnsi("stop"),
                        IntPtr.Zero
                    };
                    LibMpvInterop.mpv_command(_mpvHandle, cmd);

                    foreach (var ptr in cmd)
                    {
                        if (ptr != IntPtr.Zero)
                            Marshal.FreeHGlobal(ptr);
                    }
                }
                catch
                {
                    // Ignore errors during stop
                }
            }

            _currentFile = null;
            LockText.Text = "Lock: Not held";
            StatusText.Text = "Status: Stopped";
            AudioTracksPanel.Children.Clear();

            // Show placeholder text when stopped
            PlaceholderText.IsVisible = true;
        }

        private void PollMpvEvents()
        {
            try
            {
                // Poll up to 20 events with no wait
                for (int i = 0; i < 20; i++)
                {
                    IntPtr eventPtr = LibMpvInterop.mpv_wait_event(_mpvHandle, 0);
                    if (eventPtr == IntPtr.Zero)
                        break;

                    // Read event_id (first int in the structure)
                    int eventId = Marshal.ReadInt32(eventPtr);

                    // Event IDs from mpv documentation
                    // 0 = none, 1 = shutdown, 3 = log-message, 6 = start-file, 7 = end-file, etc.
                    if (eventId == 0) // MPV_EVENT_NONE
                        break;

                    string eventName = eventId switch
                    {
                        1 => "SHUTDOWN",
                        3 => "LOG_MESSAGE",
                        6 => "START_FILE",
                        7 => "END_FILE",
                        9 => "FILE_LOADED",
                        16 => "PLAYBACK_RESTART",
                        20 => "PROPERTY_CHANGE",
                        _ => $"EVENT_{eventId}"
                    };

                    Logger.Log($"MPV Event: {eventName} (id={eventId})");

                    // For END_FILE events, read the error/reason
                    if (eventId == 7) // END_FILE
                    {
                        try
                        {
                            // Structure: event_id (4), error (4), then reason (8), file_error (4)
                            int error = Marshal.ReadInt32(eventPtr, 4);
                            if (error != 0)
                            {
                                IntPtr errorStrPtr = LibMpvInterop.mpv_error_string(error);
                                string? errorStr = errorStrPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(errorStrPtr) : "unknown";
                                Logger.Log($"  END_FILE error: {error} ({errorStr})");
                            }

                            // Try to read reason (offset 8, int64)
                            long reason = Marshal.ReadInt64(eventPtr, 8);
                            string reasonStr = reason switch
                            {
                                0 => "EOF (normal end)",
                                2 => "STOP (stopped by command)",
                                3 => "QUIT (quit was requested)",
                                4 => "ERROR (playback error)",
                                5 => "REDIRECT",
                                _ => $"Unknown reason {reason}"
                            };
                            Logger.Log($"  END_FILE reason: {reasonStr}");
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"  Failed to parse END_FILE details: {ex.Message}");
                        }
                    }

                    // For log messages, try to read the message
                    if (eventId == 3)
                    {
                        // Log message structure has prefix, level, text fields after event_id and error
                        // This is approximate - proper parsing would need the full struct
                        try
                        {
                            // Skip event_id (4 bytes) + error (4 bytes) = 8 bytes
                            IntPtr prefixPtr = Marshal.ReadIntPtr(eventPtr, 8);
                            IntPtr levelPtr = Marshal.ReadIntPtr(eventPtr, 8 + IntPtr.Size);
                            IntPtr textPtr = Marshal.ReadIntPtr(eventPtr, 8 + IntPtr.Size * 2);

                            string? prefix = prefixPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(prefixPtr) : "";
                            string? level = levelPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(levelPtr) : "";
                            string? text = textPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(textPtr) : "";

                            // Only log if it contains useful info
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                Logger.Log($"  MPV Log [{level}] {prefix}: {text}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"  Failed to parse log message: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Error polling MPV events", ex);
            }
        }

        private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Clean up resources
            await CleanupCurrentFileAsync();

            // Render context must be freed before mpv_terminate_destroy
            VideoHostControl.Renderer?.Dispose();

            if (_mpvHandle != IntPtr.Zero)
            {
                LibMpvInterop.mpv_terminate_destroy(_mpvHandle);
                _mpvHandle = IntPtr.Zero;
            }
        }
    }
}