using System;
using System.Runtime.InteropServices;

namespace AniPlayer.UI
{
    // Simple P/Invoke wrapper for libmpv - POC only
    internal static class LibMpvInterop
    {
        private const string LibraryName = "libmpv-2.dll";

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_create();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_initialize(IntPtr mpv);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_terminate_destroy(IntPtr mpv);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_command(IntPtr mpv, IntPtr[] args);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_property_string(IntPtr mpv, byte[] name, byte[] value);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_get_property_string(IntPtr mpv, byte[] name);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_free(IntPtr data);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_option_string(IntPtr mpv, byte[] name, byte[] value);
    }
}