using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace AniPlayer.UI
{
    internal static class PlatformHelper
    {
        // Windows P/Invoke
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        // X11 P/Invoke for Linux
        [DllImport("libX11.so.6", EntryPoint = "XOpenDisplay")]
        private static extern IntPtr X11OpenDisplay(IntPtr display);

        [DllImport("libX11.so.6", EntryPoint = "XCloseDisplay")]
        private static extern int X11CloseDisplay(IntPtr display);

        public static string GetCurrentPlatform()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return "windows";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return "linux";

            throw new PlatformNotSupportedException("Only Windows and Linux are supported");
        }

        public static IntPtr GetWindowHandle(Window window)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return GetWindowsHandle(window);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return GetLinuxHandle(window);
            }

            throw new PlatformNotSupportedException("Only Windows and Linux are supported");
        }

        private static IntPtr GetWindowsHandle(Window window)
        {
            // Get the native window handle from Avalonia
            var platformHandle = window.TryGetPlatformHandle();
            if (platformHandle != null)
            {
                return platformHandle.Handle;
            }

            throw new InvalidOperationException("Failed to get Windows window handle");
        }

        private static IntPtr GetLinuxHandle(Window window)
        {
            // Get the native window handle from Avalonia (X11 window ID)
            var platformHandle = window.TryGetPlatformHandle();
            if (platformHandle != null)
            {
                return platformHandle.Handle;
            }

            throw new InvalidOperationException("Failed to get X11 window handle");
        }
    }
}
