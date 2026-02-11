using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace AniPlayer.UI
{
    /// <summary>
    /// A control that provides a native window handle for embedding video playback
    /// </summary>
    public class VideoHost : NativeControlHost
    {
        private IntPtr _nativeHandle;
        private MpvRenderer? _renderer;

        public IntPtr NativeHandle => _nativeHandle;
        public MpvRenderer? Renderer => _renderer;

        public VideoHost()
        {
            Logger.Log("VideoHost constructor called");
        }

        public void InitializeRenderer(IntPtr mpvHandle)
        {
            Logger.Log($"InitializeRenderer called with MPV handle: {mpvHandle}");

            if (_nativeHandle == IntPtr.Zero)
            {
                Logger.LogError("Cannot initialize renderer - native handle is zero");
                return;
            }

            try
            {
                _renderer = new MpvRenderer(mpvHandle, _nativeHandle);

                // Get current size
                var size = Bounds.Size;
                int width = Math.Max((int)size.Width, 1);
                int height = Math.Max((int)size.Height, 1);

                Logger.Log($"Initializing renderer with size: {width}x{height}");
                bool success = _renderer.Initialize(width, height);

                if (!success)
                {
                    Logger.LogError("Renderer initialization failed");
                    _renderer?.Dispose();
                    _renderer = null;
                    return;
                }

                // Subscribe to render events
                _renderer.RenderNeeded += OnRenderNeeded;

                Logger.Log("Renderer initialized successfully");
            }
            catch (Exception ex)
            {
                Logger.LogError("InitializeRenderer exception", ex);
                _renderer?.Dispose();
                _renderer = null;
            }
        }

        private void OnRenderNeeded()
        {
            try
            {
                Logger.Log("OnRenderNeeded - calling Render()");
                _renderer?.Render();
                Logger.Log("Render() completed");
            }
            catch (Exception ex)
            {
                Logger.LogError("OnRenderNeeded exception", ex);
            }
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var result = base.ArrangeOverride(finalSize);

            // Resize the native child window to match the control size
            if (_nativeHandle != IntPtr.Zero)
            {
                Logger.Log($"ArrangeOverride: Resizing child window to {finalSize.Width}x{finalSize.Height}");

                if (OperatingSystem.IsWindows())
                {
                    ResizeWindowsControl(_nativeHandle, (int)finalSize.Width, (int)finalSize.Height);
                }
                // Linux X11 windows resize automatically with parent

                // Notify renderer of size change
                if (_renderer != null)
                {
                    int width = Math.Max((int)finalSize.Width, 1);
                    int height = Math.Max((int)finalSize.Height, 1);
                    _renderer.Resize(width, height);
                }
            }

            return result;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);

            // Clean up renderer
            if (_renderer != null)
            {
                _renderer.RenderNeeded -= OnRenderNeeded;
                _renderer.Dispose();
                _renderer = null;
            }
        }

        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            Logger.Log("=== CreateNativeControlCore START ===");
            Logger.Log($"Parent handle: {parent.Handle} (0x{parent.Handle.ToString("X")})");
            Logger.Log($"Parent handle descriptor: {parent.HandleDescriptor}");

            // Create a child window for the video rendering
            if (OperatingSystem.IsWindows())
            {
                Logger.Log("Platform: Windows - creating Windows control");
                _nativeHandle = CreateWindowsControl(parent);
            }
            else if (OperatingSystem.IsLinux())
            {
                Logger.Log("Platform: Linux - creating X11 control");
                _nativeHandle = CreateLinuxControl(parent);
            }
            else
            {
                Logger.LogError("Unsupported platform - only Windows and Linux are supported");
                throw new PlatformNotSupportedException("Only Windows and Linux are supported");
            }

            Logger.Log($"Created native control with handle: {_nativeHandle} (0x{_nativeHandle.ToString("X")})");
            Logger.Log("=== CreateNativeControlCore END ===");

            return new PlatformHandle(_nativeHandle, "MPV-Video-Host");
        }

        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            Logger.Log($"DestroyNativeControlCore called for handle: {control.Handle}");

            if (OperatingSystem.IsWindows())
            {
                DestroyWindowsControl(control.Handle);
            }
            else if (OperatingSystem.IsLinux())
            {
                DestroyLinuxControl(control.Handle);
            }

            _nativeHandle = IntPtr.Zero;
            Logger.Log("Native control destroyed");
        }

        #region Windows Implementation

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            uint dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x, int y,
            int nWidth, int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool UpdateWindow(IntPtr hWnd);

        private const uint WS_CHILD = 0x40000000;
        private const uint WS_VISIBLE = 0x10000000;
        private const int SW_SHOW = 5;

        private IntPtr CreateWindowsControl(IPlatformHandle parent)
        {
            Logger.Log("CreateWindowsControl: Creating child window...");
            Logger.Log($"  Parent HWND: {parent.Handle}");
            Logger.Log($"  Style: WS_CHILD | WS_VISIBLE (0x{(WS_CHILD | WS_VISIBLE).ToString("X")})");

            // Create a child window for video rendering
            var handle = CreateWindowEx(
                0,
                "Static",
                "",
                WS_CHILD | WS_VISIBLE,
                0, 0,
                800, 600,
                parent.Handle,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            Logger.Log($"CreateWindowEx returned: {handle} (0x{handle.ToString("X")})");

            if (handle == IntPtr.Zero)
            {
                var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                Logger.LogError($"Failed to create Windows child window - GetLastError: {error}");
                throw new Exception($"Failed to create Windows child window for video (error: {error})");
            }

            // Ensure the window is shown and updated
            Logger.Log("Showing and updating window...");
            ShowWindow(handle, SW_SHOW);
            UpdateWindow(handle);

            Logger.Log("Windows child window created successfully");
            return handle;
        }

        private void DestroyWindowsControl(IntPtr handle)
        {
            if (handle != IntPtr.Zero)
            {
                Logger.Log($"Destroying Windows control: {handle}");
                DestroyWindow(handle);
            }
        }

        private void ResizeWindowsControl(IntPtr handle, int width, int height)
        {
            if (handle != IntPtr.Zero)
            {
                bool result = MoveWindow(handle, 0, 0, width, height, true);
                if (!result)
                {
                    var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                    Logger.LogError($"Failed to resize window - GetLastError: {error}");
                }
            }
        }

        #endregion

        #region Linux Implementation

        [System.Runtime.InteropServices.DllImport("libX11.so.6")]
        private static extern IntPtr XCreateSimpleWindow(
            IntPtr display,
            IntPtr parent,
            int x, int y,
            uint width, uint height,
            uint border_width,
            ulong border,
            ulong background);

        [System.Runtime.InteropServices.DllImport("libX11.so.6")]
        private static extern int XMapWindow(IntPtr display, IntPtr window);

        [System.Runtime.InteropServices.DllImport("libX11.so.6")]
        private static extern int XDestroyWindow(IntPtr display, IntPtr window);

        [System.Runtime.InteropServices.DllImport("libX11.so.6")]
        private static extern IntPtr XOpenDisplay(IntPtr display);

        [System.Runtime.InteropServices.DllImport("libX11.so.6")]
        private static extern int XFlush(IntPtr display);

        private IntPtr _x11Display;

        private IntPtr CreateLinuxControl(IPlatformHandle parent)
        {
            Logger.Log("CreateLinuxControl: Opening X11 display...");

            // Open X11 display
            _x11Display = XOpenDisplay(IntPtr.Zero);
            Logger.Log($"XOpenDisplay returned: {_x11Display}");

            if (_x11Display == IntPtr.Zero)
            {
                Logger.LogError("Failed to open X11 display");
                throw new Exception("Failed to open X11 display");
            }

            Logger.Log($"Creating X11 child window for parent: {parent.Handle}");

            // Create a child window for video rendering
            var window = XCreateSimpleWindow(
                _x11Display,
                parent.Handle,
                0, 0,
                800, 600,
                0,
                0,
                0);

            Logger.Log($"XCreateSimpleWindow returned: {window}");

            if (window == IntPtr.Zero)
            {
                Logger.LogError("Failed to create X11 window");
                throw new Exception("Failed to create X11 window for video");
            }

            Logger.Log("Mapping X11 window...");
            XMapWindow(_x11Display, window);
            XFlush(_x11Display);
            Logger.Log("X11 window created and mapped successfully");

            return window;
        }

        private void DestroyLinuxControl(IntPtr handle)
        {
            if (handle != IntPtr.Zero && _x11Display != IntPtr.Zero)
            {
                Logger.Log($"Destroying X11 control: {handle}");
                XDestroyWindow(_x11Display, handle);
            }
        }

        #endregion
    }
}
