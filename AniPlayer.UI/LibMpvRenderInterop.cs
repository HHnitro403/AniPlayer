using System;
using System.Runtime.InteropServices;

namespace AniPlayer.UI
{
    // libmpv render API for proper embedded video rendering
    internal static class LibMpvRenderInterop
    {
        private const string LibraryName = "libmpv-2.dll";

        // Render context creation and management
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_render_context_create(
            out IntPtr res,
            IntPtr mpv,
            IntPtr @params);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_render_context_free(IntPtr ctx);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_render_context_render(IntPtr ctx, IntPtr @params);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_render_context_report_swap(IntPtr ctx);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong mpv_render_context_update(IntPtr ctx);

        public const ulong MPV_RENDER_UPDATE_FRAME = 1;

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_render_context_set_update_callback(
            IntPtr ctx,
            MpvRenderUpdateCallback callback,
            IntPtr callback_ctx);

        // Callback delegate
        public delegate void MpvRenderUpdateCallback(IntPtr callback_ctx);

        // Render parameter types
        public const int MPV_RENDER_PARAM_INVALID = 0;
        public const int MPV_RENDER_PARAM_API_TYPE = 1;
        public const int MPV_RENDER_PARAM_OPENGL_INIT_PARAMS = 2;
        public const int MPV_RENDER_PARAM_OPENGL_FBO = 3;
        public const int MPV_RENDER_PARAM_FLIP_Y = 4;
        public const int MPV_RENDER_PARAM_DEPTH = 5;
        public const int MPV_RENDER_PARAM_ICC_PROFILE = 6;
        public const int MPV_RENDER_PARAM_AMBIENT_LIGHT = 7;
        public const int MPV_RENDER_PARAM_X11_DISPLAY = 8;
        public const int MPV_RENDER_PARAM_WL_DISPLAY = 9;
        public const int MPV_RENDER_PARAM_ADVANCED_CONTROL = 10;
        public const int MPV_RENDER_PARAM_NEXT_FRAME_INFO = 11;
        public const int MPV_RENDER_PARAM_BLOCK_FOR_TARGET_TIME = 12;
        public const int MPV_RENDER_PARAM_SKIP_RENDERING = 13;
        public const int MPV_RENDER_PARAM_DRM_DISPLAY = 14;
        public const int MPV_RENDER_PARAM_DRM_DRAW_SURFACE_SIZE = 15;
        public const int MPV_RENDER_PARAM_DRM_DISPLAY_V2 = 16;
        public const int MPV_RENDER_PARAM_SW_SIZE = 17;
        public const int MPV_RENDER_PARAM_SW_FORMAT = 18;
        public const int MPV_RENDER_PARAM_SW_STRIDE = 19;
        public const int MPV_RENDER_PARAM_SW_POINTER = 20;

        // Render parameter structure
        [StructLayout(LayoutKind.Sequential)]
        public struct MpvRenderParam
        {
            public int type;
            public IntPtr data;
        }

        // OpenGL FBO structure
        [StructLayout(LayoutKind.Sequential)]
        public struct MpvOpenGLFBO
        {
            public int fbo;
            public int w;
            public int h;
            public int internal_format;
        }

        // OpenGL init params structure
        [StructLayout(LayoutKind.Sequential)]
        public struct MpvOpenGLInitParams
        {
            public IntPtr get_proc_address;
            public IntPtr get_proc_address_ctx;
        }

        // Software rendering structures
        [StructLayout(LayoutKind.Sequential)]
        public struct MpvRenderFrameInfo
        {
            public long flags;
            public long target_time;
        }
    }
}
