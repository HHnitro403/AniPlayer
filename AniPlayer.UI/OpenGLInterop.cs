using System;
using System.Runtime.InteropServices;

namespace AniPlayer.UI
{
    // Windows OpenGL (WGL) interop for creating OpenGL context
    internal static class OpenGLInterop
    {
        // User32 functions for device context
        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        // GDI32 functions for pixel format
        [DllImport("gdi32.dll")]
        public static extern int ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR ppfd);

        [DllImport("gdi32.dll")]
        public static extern bool SetPixelFormat(IntPtr hdc, int format, ref PIXELFORMATDESCRIPTOR ppfd);

        [DllImport("gdi32.dll")]
        public static extern bool SwapBuffers(IntPtr hdc);

        // OpenGL32 functions
        [DllImport("opengl32.dll")]
        public static extern IntPtr wglCreateContext(IntPtr hdc);

        [DllImport("opengl32.dll")]
        public static extern bool wglDeleteContext(IntPtr hglrc);

        [DllImport("opengl32.dll")]
        public static extern bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);

        [DllImport("opengl32.dll")]
        public static extern IntPtr wglGetProcAddress(string name);

        [DllImport("opengl32.dll")]
        public static extern IntPtr wglGetCurrentContext();

        [DllImport("opengl32.dll", EntryPoint = "glGetString")]
        public static extern IntPtr glGetString(uint name);

        public const uint GL_VERSION = 0x1F02;
        public const uint GL_VENDOR = 0x1F00;
        public const uint GL_RENDERER = 0x1F01;

        // OpenGL functions we need
        [DllImport("opengl32.dll")]
        public static extern void glClear(uint mask);

        [DllImport("opengl32.dll")]
        public static extern void glClearColor(float red, float green, float blue, float alpha);

        [DllImport("opengl32.dll")]
        public static extern void glViewport(int x, int y, int width, int height);

        [DllImport("opengl32.dll")]
        public static extern void glFlush();

        // OpenGL constants
        public const uint GL_COLOR_BUFFER_BIT = 0x00004000;
        public const uint GL_DEPTH_BUFFER_BIT = 0x00000100;

        // Pixel format descriptor
        [StructLayout(LayoutKind.Sequential)]
        public struct PIXELFORMATDESCRIPTOR
        {
            public ushort nSize;
            public ushort nVersion;
            public uint dwFlags;
            public byte iPixelType;
            public byte cColorBits;
            public byte cRedBits;
            public byte cRedShift;
            public byte cGreenBits;
            public byte cGreenShift;
            public byte cBlueBits;
            public byte cBlueShift;
            public byte cAlphaBits;
            public byte cAlphaShift;
            public byte cAccumBits;
            public byte cAccumRedBits;
            public byte cAccumGreenBits;
            public byte cAccumBlueBits;
            public byte cAccumAlphaBits;
            public byte cDepthBits;
            public byte cStencilBits;
            public byte cAuxBuffers;
            public byte iLayerType;
            public byte bReserved;
            public uint dwLayerMask;
            public uint dwVisibleMask;
            public uint dwDamageMask;
        }

        // Pixel format flags
        public const uint PFD_DRAW_TO_WINDOW = 0x00000004;
        public const uint PFD_SUPPORT_OPENGL = 0x00000020;
        public const uint PFD_DOUBLEBUFFER = 0x00000001;
        public const byte PFD_TYPE_RGBA = 0;
        public const byte PFD_MAIN_PLANE = 0;

        public static PIXELFORMATDESCRIPTOR GetDefaultPixelFormatDescriptor()
        {
            return new PIXELFORMATDESCRIPTOR
            {
                nSize = (ushort)Marshal.SizeOf(typeof(PIXELFORMATDESCRIPTOR)),
                nVersion = 1,
                dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER,
                iPixelType = PFD_TYPE_RGBA,
                cColorBits = 32,
                cDepthBits = 24,
                cStencilBits = 8,
                iLayerType = PFD_MAIN_PLANE
            };
        }
    }
}
