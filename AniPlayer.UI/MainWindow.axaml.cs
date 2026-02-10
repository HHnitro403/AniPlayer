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

        public MainWindow()
        {
            InitializeComponent();
            Closing += MainWindow_Closing;
            InitializeMpv();
        }

        private void InitializeMpv()
        {
            // Create mpv instance
            _mpvHandle = LibMpvInterop.mpv_create();
            if (_mpvHandle == IntPtr.Zero)
            {
                StatusText.Text = "Status: Error - Failed to create mpv instance";
                return;
            }

            // Initialize mpv
            int result = LibMpvInterop.mpv_initialize(_mpvHandle);
            if (result < 0)
            {
                StatusText.Text = $"Status: Error - Failed to initialize mpv (code: {result})";
                return;
            }
        }

        private async void OpenFileButton_Click(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

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
                await LoadVideoAsync(filePath);
            }
        }

        private async Task LoadVideoAsync(string filePath)
        {
            try
            {
                // Clean up previous file if any
                await CleanupCurrentFileAsync();

                // Acquire OS-level file lock with FileShare.None
                _lockStream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);

                _currentFile = Path.GetFileName(filePath);

                if (_mpvHandle == IntPtr.Zero)
                {
                    StatusText.Text = "Status: Error - MPV not initialized";
                    return;
                }

                // Load the file into mpv using command
                var cmd = new[] {
                    Marshal.StringToHGlobalAnsi("loadfile"),
                    Marshal.StringToHGlobalAnsi(filePath),
                    IntPtr.Zero
                };

                LibMpvInterop.mpv_command(_mpvHandle, cmd);

                // Free command pointers
                foreach (var ptr in cmd)
                {
                    if (ptr != IntPtr.Zero)
                        Marshal.FreeHGlobal(ptr);
                }

                // Update status
                StatusText.Text = "Status: Playing";
                LockText.Text = $"Lock: HELD — {_currentFile}";

                // Wait a bit for mpv to initialize the file
                await Task.Delay(1500);

                // Enumerate and display audio tracks
                await UpdateAudioTracksAsync();
            }
            catch (Exception ex)
            {
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
        }

        private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Clean up resources
            await CleanupCurrentFileAsync();

            // Dispose mpv context
            if (_mpvHandle != IntPtr.Zero)
            {
                LibMpvInterop.mpv_terminate_destroy(_mpvHandle);
                _mpvHandle = IntPtr.Zero;
            }
        }
    }
}