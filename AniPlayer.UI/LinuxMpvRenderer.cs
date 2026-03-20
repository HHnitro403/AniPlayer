using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace AniPlayer.UI
{
    /// <summary>
    /// Linux equivalent of MpvRenderer.cs.
    /// Replaces WGL context creation with EGL; the mpv render API usage is identical.
    /// </summary>
    public class LinuxMpvRenderer : IMpvRenderer
    {
        private readonly IntPtr _mpvHandle;
        private readonly IntPtr _x11Window;

        // EGL handles
        private IntPtr _x11Display;
        private IntPtr _eglDisplay;
        private IntPtr _eglSurface;
        private IntPtr _eglContext;
        private IntPtr _eglConfig;

        // mpv render context
        private IntPtr _renderContext;

        private volatile int _width;
        private volatile int _height;
        private volatile bool _isRunning;

        private Thread? _renderThread;
        private readonly AutoResetEvent _renderSignal = new(false);
        private readonly ManualResetEventSlim _initSignal = new(false);
        private bool _initSuccess;

        private LibMpvRenderInterop.MpvRenderUpdateCallback? _updateCallback;
        private GCHandle _callbackHandle;

        public bool IsInitialized => _renderContext != IntPtr.Zero;

        public LinuxMpvRenderer(IntPtr mpvHandle, IntPtr x11Window)
        {
            _mpvHandle  = mpvHandle;
            _x11Window  = x11Window;
        }

        public bool Initialize(int width, int height, bool vsync = false)
        {
            Logger.Log($"=== LinuxMpvRenderer Initialize ({width}x{height}, vsync={vsync}) ===");

            _width     = width;
            _height    = height;
            _isRunning = true;
            _initSignal.Reset();

            _renderThread = new Thread(() => RenderLoop(vsync))
            {
                Name         = "MpvEGLRenderThread",
                IsBackground = true
            };
            _renderThread.Start();

            _initSignal.Wait();

            if (!_initSuccess)
            {
                Logger.LogError("LinuxMpvRenderer failed to initialize on background thread.");
                Dispose();
            }
            else
            {
                Logger.Log("LinuxMpvRenderer initialized successfully on background thread.");
            }

            return _initSuccess;
        }

        // ── Render thread ─────────────────────────────────────────────────────

        private void RenderLoop(bool vsync)
        {
            try
            {
                // 1. Open X11 display connection
                _x11Display = EGLInterop.XOpenDisplay(IntPtr.Zero);
                if (_x11Display == IntPtr.Zero)
                    throw new Exception("XOpenDisplay failed — is DISPLAY set?");

                Logger.Log($"EGL: XOpenDisplay OK ({_x11Display})");

                // 2. Get EGL display from the X11 display
                _eglDisplay = EGLInterop.eglGetDisplay(_x11Display);
                if (_eglDisplay == EGLInterop.EGL_NO_DISPLAY)
                    throw new Exception("eglGetDisplay failed");

                // 3. Initialize EGL
                if (!EGLInterop.eglInitialize(_eglDisplay, out int major, out int minor))
                    throw new Exception($"eglInitialize failed (error 0x{EGLInterop.eglGetError():X})");

                Logger.Log($"EGL version {major}.{minor}");

                // 4. Bind full OpenGL API (not OpenGL ES)
                if (!EGLInterop.eglBindAPI(EGLInterop.EGL_OPENGL_API))
                    throw new Exception("eglBindAPI(EGL_OPENGL_API) failed — Mesa libGL required");

                // 5. Choose config — equivalent of ChoosePixelFormat
                var attribs = new int[]
                {
                    EGLInterop.EGL_SURFACE_TYPE,    EGLInterop.EGL_WINDOW_BIT,
                    EGLInterop.EGL_RENDERABLE_TYPE, EGLInterop.EGL_OPENGL_BIT,
                    EGLInterop.EGL_RED_SIZE,        8,
                    EGLInterop.EGL_GREEN_SIZE,      8,
                    EGLInterop.EGL_BLUE_SIZE,       8,
                    EGLInterop.EGL_ALPHA_SIZE,      8,
                    EGLInterop.EGL_DEPTH_SIZE,      24,
                    EGLInterop.EGL_NONE
                };

                var configs = new IntPtr[1];
                if (!EGLInterop.eglChooseConfig(_eglDisplay, attribs, configs, 1, out int numConfigs) || numConfigs == 0)
                    throw new Exception($"eglChooseConfig found no matching configs (error 0x{EGLInterop.eglGetError():X})");

                _eglConfig = configs[0];
                Logger.Log($"EGL config chosen: {_eglConfig}");

                // 6. Create window surface from the X11 window
                _eglSurface = EGLInterop.eglCreateWindowSurface(_eglDisplay, _eglConfig, _x11Window, null);
                if (_eglSurface == EGLInterop.EGL_NO_SURFACE)
                    throw new Exception($"eglCreateWindowSurface failed (error 0x{EGLInterop.eglGetError():X})");

                Logger.Log("EGL window surface created");

                // 7. Create context — equivalent of wglCreateContext
                _eglContext = EGLInterop.eglCreateContext(_eglDisplay, _eglConfig, EGLInterop.EGL_NO_CONTEXT, null);
                if (_eglContext == EGLInterop.EGL_NO_CONTEXT)
                    throw new Exception($"eglCreateContext failed (error 0x{EGLInterop.eglGetError():X})");

                Logger.Log("EGL context created");

                // 8. Make current on this thread — equivalent of wglMakeCurrent
                if (!EGLInterop.eglMakeCurrent(_eglDisplay, _eglSurface, _eglSurface, _eglContext))
                    throw new Exception($"eglMakeCurrent failed (error 0x{EGLInterop.eglGetError():X})");

                Logger.Log("EGL context made current on render thread");

                // 9. VSync — equivalent of wglSwapIntervalEXT
                EGLInterop.eglSwapInterval(_eglDisplay, vsync ? 1 : 0);
                Logger.Log($"EGL vsync set to {vsync}");

                // 10. Create mpv render context — identical to Windows path
                if (!CreateMpvRenderContext())
                    throw new Exception("mpv_render_context_create failed");

                _initSuccess = true;
                _initSignal.Set();

                // 11. Render loop — identical to Windows path
                while (_isRunning)
                {
                    _renderSignal.WaitOne(1000);

                    if (!_isRunning) break;

                    ProcessRender();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("LinuxMpvRenderer RenderLoop crashed", ex);
                _initSuccess = false;
                _initSignal.Set();
            }
            finally
            {
                CleanupEGL();
            }
        }

        private void ProcessRender()
        {
            if (_renderContext == IntPtr.Zero) return;

            ulong flags = LibMpvRenderInterop.mpv_render_context_update(_renderContext);
            if ((flags & LibMpvRenderInterop.MPV_RENDER_UPDATE_FRAME) == 0) return;

            EGLInterop.glViewport(0, 0, _width, _height);
            EGLInterop.glClearColor(0f, 0f, 0f, 1f);
            EGLInterop.glClear(EGLInterop.GL_COLOR_BUFFER_BIT);

            var fbo = new LibMpvRenderInterop.MpvOpenGLFBO
            {
                fbo             = 0,
                w               = _width,
                h               = _height,
                internal_format = 0
            };

            IntPtr fboPtr = Marshal.AllocHGlobal(Marshal.SizeOf(fbo));
            Marshal.StructureToPtr(fbo, fboPtr, false);

            int flipY = 1;
            IntPtr flipYPtr = Marshal.AllocHGlobal(sizeof(int));
            Marshal.WriteInt32(flipYPtr, flipY);

            var renderParams = new[]
            {
                new LibMpvRenderInterop.MpvRenderParam { type = LibMpvRenderInterop.MPV_RENDER_PARAM_OPENGL_FBO, data = fboPtr    },
                new LibMpvRenderInterop.MpvRenderParam { type = LibMpvRenderInterop.MPV_RENDER_PARAM_FLIP_Y,     data = flipYPtr  },
                new LibMpvRenderInterop.MpvRenderParam { type = LibMpvRenderInterop.MPV_RENDER_PARAM_INVALID,    data = IntPtr.Zero }
            };

            int paramSize = Marshal.SizeOf<LibMpvRenderInterop.MpvRenderParam>();
            IntPtr paramsPtr = Marshal.AllocHGlobal(paramSize * renderParams.Length);
            for (int i = 0; i < renderParams.Length; i++)
                Marshal.StructureToPtr(renderParams[i], IntPtr.Add(paramsPtr, i * paramSize), false);

            LibMpvRenderInterop.mpv_render_context_render(_renderContext, paramsPtr);

            Marshal.FreeHGlobal(paramsPtr);
            Marshal.FreeHGlobal(fboPtr);
            Marshal.FreeHGlobal(flipYPtr);

            // Linux: eglSwapBuffers instead of Win32 SwapBuffers(hdc)
            EGLInterop.eglSwapBuffers(_eglDisplay, _eglSurface);
            LibMpvRenderInterop.mpv_render_context_report_swap(_renderContext);
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr GetProcAddressDelegate(IntPtr ctx, [MarshalAs(UnmanagedType.LPStr)] string name);

        private bool CreateMpvRenderContext()
        {
            // GetProcAddress: eglGetProcAddress instead of wglGetProcAddress
            GetProcAddressDelegate getProcAddress = (ctx, name) => EGLInterop.eglGetProcAddress(name);
            IntPtr getProcAddressPtr = Marshal.GetFunctionPointerForDelegate(getProcAddress);
            _callbackHandle = GCHandle.Alloc(getProcAddress);

            var initParams = new LibMpvRenderInterop.MpvOpenGLInitParams
            {
                get_proc_address     = getProcAddressPtr,
                get_proc_address_ctx = IntPtr.Zero
            };

            IntPtr initParamsPtr = Marshal.AllocHGlobal(Marshal.SizeOf(initParams));
            Marshal.StructureToPtr(initParams, initParamsPtr, false);

            IntPtr apiTypePtr = Marshal.StringToHGlobalAnsi("opengl");

            // Linux needs MPV_RENDER_PARAM_X11_DISPLAY so mpv can use hwdec
            IntPtr x11DisplayPtr = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(x11DisplayPtr, _x11Display);

            var renderParams = new[]
            {
                new LibMpvRenderInterop.MpvRenderParam { type = LibMpvRenderInterop.MPV_RENDER_PARAM_API_TYPE,          data = apiTypePtr    },
                new LibMpvRenderInterop.MpvRenderParam { type = LibMpvRenderInterop.MPV_RENDER_PARAM_OPENGL_INIT_PARAMS, data = initParamsPtr },
                new LibMpvRenderInterop.MpvRenderParam { type = LibMpvRenderInterop.MPV_RENDER_PARAM_X11_DISPLAY,        data = x11DisplayPtr },
                new LibMpvRenderInterop.MpvRenderParam { type = LibMpvRenderInterop.MPV_RENDER_PARAM_INVALID,            data = IntPtr.Zero   }
            };

            int paramSize = Marshal.SizeOf<LibMpvRenderInterop.MpvRenderParam>();
            IntPtr paramsPtr = Marshal.AllocHGlobal(paramSize * renderParams.Length);
            for (int i = 0; i < renderParams.Length; i++)
                Marshal.StructureToPtr(renderParams[i], IntPtr.Add(paramsPtr, i * paramSize), false);

            int result = LibMpvRenderInterop.mpv_render_context_create(out _renderContext, _mpvHandle, paramsPtr);

            Marshal.FreeHGlobal(paramsPtr);
            Marshal.FreeHGlobal(initParamsPtr);
            Marshal.FreeHGlobal(apiTypePtr);
            Marshal.FreeHGlobal(x11DisplayPtr);

            if (result < 0)
            {
                Logger.LogError($"mpv_render_context_create returned {result}");
                return false;
            }

            _updateCallback = _ => _renderSignal.Set();
            LibMpvRenderInterop.mpv_render_context_set_update_callback(_renderContext, _updateCallback, IntPtr.Zero);

            Logger.Log("mpv render context created (EGL path)");
            return true;
        }

        // ── Cleanup ───────────────────────────────────────────────────────────

        private void CleanupEGL()
        {
            if (_renderContext != IntPtr.Zero)
            {
                LibMpvRenderInterop.mpv_render_context_free(_renderContext);
                _renderContext = IntPtr.Zero;
            }

            if (_eglDisplay != EGLInterop.EGL_NO_DISPLAY)
            {
                EGLInterop.eglMakeCurrent(_eglDisplay, EGLInterop.EGL_NO_SURFACE, EGLInterop.EGL_NO_SURFACE, EGLInterop.EGL_NO_CONTEXT);

                if (_eglContext != EGLInterop.EGL_NO_CONTEXT)
                {
                    EGLInterop.eglDestroyContext(_eglDisplay, _eglContext);
                    _eglContext = EGLInterop.EGL_NO_CONTEXT;
                }

                if (_eglSurface != EGLInterop.EGL_NO_SURFACE)
                {
                    EGLInterop.eglDestroySurface(_eglDisplay, _eglSurface);
                    _eglSurface = EGLInterop.EGL_NO_SURFACE;
                }

                EGLInterop.eglTerminate(_eglDisplay);
                _eglDisplay = EGLInterop.EGL_NO_DISPLAY;
            }

            if (_x11Display != IntPtr.Zero)
            {
                EGLInterop.XCloseDisplay(_x11Display);
                _x11Display = IntPtr.Zero;
            }
        }

        public void Render()              => _renderSignal.Set();
        public void Resize(int w, int h)  { _width = w; _height = h; _renderSignal.Set(); }

        public void Dispose()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _renderSignal.Set();

            if (_renderThread?.IsAlive == true)
            {
                System.Threading.Tasks.Task.Run(() =>
                {
                    if (_renderThread.Join(2000))
                        Logger.Log("EGL render thread exited cleanly");
                    else
                        Logger.LogError("EGL render thread did not exit within 2 seconds");
                });
            }

            if (_callbackHandle.IsAllocated) _callbackHandle.Free();
            _renderSignal.Dispose();
            _initSignal.Dispose();
        }
    }
}
