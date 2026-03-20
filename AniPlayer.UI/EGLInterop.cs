using System;
using System.Runtime.InteropServices;

namespace AniPlayer.UI
{
    /// <summary>
    /// EGL P/Invoke — the Linux equivalent of the WGL calls in OpenGLInterop.cs.
    /// libEGL.so.1 is part of Mesa or any GPU driver stack.
    /// </summary>
    internal static class EGLInterop
    {
        private const string EGL = "libEGL.so.1";
        private const string X11 = "libX11.so.6";
        private const string GL  = "libGL.so.1";

        // ── X11 Display ───────────────────────────────────────────────────────
        [DllImport(X11)]
        public static extern IntPtr XOpenDisplay(IntPtr display_name);

        [DllImport(X11)]
        public static extern int XCloseDisplay(IntPtr display);

        // ── EGL core ──────────────────────────────────────────────────────────
        [DllImport(EGL)]
        public static extern IntPtr eglGetDisplay(IntPtr native_display);

        [DllImport(EGL)]
        public static extern bool eglInitialize(IntPtr display, out int major, out int minor);

        [DllImport(EGL)]
        public static extern bool eglChooseConfig(
            IntPtr display, int[] attrib_list,
            IntPtr[] configs, int config_size, out int num_config);

        [DllImport(EGL)]
        public static extern IntPtr eglCreateWindowSurface(
            IntPtr display, IntPtr config,
            IntPtr native_window, int[]? attrib_list);

        [DllImport(EGL)]
        public static extern IntPtr eglCreateContext(
            IntPtr display, IntPtr config,
            IntPtr share_context, int[]? attrib_list);

        [DllImport(EGL)]
        public static extern bool eglMakeCurrent(
            IntPtr display, IntPtr draw, IntPtr read, IntPtr ctx);

        [DllImport(EGL)]
        public static extern bool eglSwapBuffers(IntPtr display, IntPtr surface);

        [DllImport(EGL)]
        public static extern bool eglSwapInterval(IntPtr display, int interval);

        [DllImport(EGL)]
        public static extern bool eglDestroyContext(IntPtr display, IntPtr ctx);

        [DllImport(EGL)]
        public static extern bool eglDestroySurface(IntPtr display, IntPtr surface);

        [DllImport(EGL)]
        public static extern bool eglTerminate(IntPtr display);

        [DllImport(EGL)]
        public static extern IntPtr eglGetProcAddress(string procname);

        [DllImport(EGL)]
        public static extern bool eglBindAPI(uint api);

        [DllImport(EGL)]
        public static extern int eglGetError();

        // ── EGL constants ─────────────────────────────────────────────────────
        public const uint EGL_OPENGL_API      = 0x30A2;
        public const int  EGL_NONE            = 0x3038;
        public const int  EGL_SURFACE_TYPE    = 0x3033;
        public const int  EGL_WINDOW_BIT      = 0x0004;
        public const int  EGL_RENDERABLE_TYPE = 0x3040;
        public const int  EGL_OPENGL_BIT      = 0x0008;
        public const int  EGL_RED_SIZE        = 0x3024;
        public const int  EGL_GREEN_SIZE      = 0x3023;
        public const int  EGL_BLUE_SIZE       = 0x3022;
        public const int  EGL_ALPHA_SIZE      = 0x3021;
        public const int  EGL_DEPTH_SIZE      = 0x3025;
        public const int  EGL_STENCIL_SIZE    = 0x3026;

        public static readonly IntPtr EGL_NO_CONTEXT = IntPtr.Zero;
        public static readonly IntPtr EGL_NO_SURFACE = IntPtr.Zero;
        public static readonly IntPtr EGL_NO_DISPLAY = IntPtr.Zero;

        // ── OpenGL (same calls as on Windows, different library) ──────────────
        [DllImport(GL)]
        public static extern void glViewport(int x, int y, int width, int height);

        [DllImport(GL)]
        public static extern void glClearColor(float r, float g, float b, float a);

        [DllImport(GL)]
        public static extern void glClear(uint mask);

        public const uint GL_COLOR_BUFFER_BIT = 0x00004000;
    }
}
