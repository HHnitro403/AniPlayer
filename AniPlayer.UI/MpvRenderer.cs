using System;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Threading;

namespace AniPlayer.UI
{
    /// <summary>
    /// Handles libmpv render API integration with OpenGL on a dedicated thread.
    /// This prevents VSync from blocking the UI thread.
    /// </summary>
    public class MpvRenderer : IDisposable
    {
        private IntPtr _mpvHandle;
        private IntPtr _renderContext;
        private IntPtr _windowHandle;
        private IntPtr _deviceContext;
        private IntPtr _glContext;
        
        // Volatile for thread safety without locks for simple reads/writes
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

        public MpvRenderer(IntPtr mpvHandle, IntPtr windowHandle)
        {
            Logger.Log($"=== MpvRenderer Constructor ===");
            _mpvHandle = mpvHandle;
            _windowHandle = windowHandle;
        }

        public bool Initialize(int width, int height, bool vsync = false)
        {
            Logger.Log($"=== MpvRenderer Initialize (Size: {width}x{height}, Vsync: {vsync}) ===");
            
            _width = width;
            _height = height;
            _isRunning = true;
            _initSignal.Reset();

            // Start the dedicated render thread
            _renderThread = new Thread(() => RenderLoop(vsync))
            {
                Name = "MpvGLRenderThread",
                IsBackground = true
            };
            _renderThread.Start();

            // Wait for initialization to complete on the thread
            _initSignal.Wait();
            
            if (_initSuccess)
            {
                Logger.Log("Renderer initialized successfully on background thread.");
            }
            else
            {
                Logger.LogError("Renderer failed to initialize on background thread.");
                Dispose(); // Cleanup if failed
            }

            return _initSuccess;
        }

        private void RenderLoop(bool vsync)
        {
            try
            {
                // 1. Setup OpenGL on this thread
                _deviceContext = OpenGLInterop.GetDC(_windowHandle);
                if (_deviceContext == IntPtr.Zero) throw new Exception("Failed to get DC");

                var pfd = OpenGLInterop.GetDefaultPixelFormatDescriptor();
                int pixelFormat = OpenGLInterop.ChoosePixelFormat(_deviceContext, ref pfd);
                if (pixelFormat == 0 || !OpenGLInterop.SetPixelFormat(_deviceContext, pixelFormat, ref pfd))
                    throw new Exception("Failed to set pixel format");

                _glContext = OpenGLInterop.wglCreateContext(_deviceContext);
                if (_glContext == IntPtr.Zero) throw new Exception("Failed to create GL context");

                if (!OpenGLInterop.wglMakeCurrent(_deviceContext, _glContext))
                    throw new Exception("Failed to make GL context current");

                // 2. Setup VSync
                SetupVsync(vsync);

                // 3. Create MPV Render Context
                if (!CreateMpvRenderContext())
                    throw new Exception("Failed to create MPV render context");

                // Signal success to the main thread
                _initSuccess = true;
                _initSignal.Set();

                // 4. Render Loop
                while (_isRunning)
                {
                    // Wait for signal from MPV (or timeout to check _isRunning occasionally)
                    _renderSignal.WaitOne(1000); 

                    if (!_isRunning) break;

                    ProcessRender();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("RenderLoop Crashed", ex);
                _initSuccess = false;
                _initSignal.Set(); // Unblock main thread if we crash during init
            }
            finally
            {
                CleanupGL();
            }
        }

        private void SetupVsync(bool enabled)
        {
            try
            {
                IntPtr swapIntervalPtr = OpenGLInterop.wglGetProcAddress("wglSwapIntervalEXT");
                if (swapIntervalPtr != IntPtr.Zero)
                {
                    var wglSwapInterval = Marshal.GetDelegateForFunctionPointer<WglSwapIntervalEXT>(swapIntervalPtr);
                    wglSwapInterval(enabled ? 1 : 0);
                    Logger.Log($"Vsync set to: {enabled}");
                }
            }
            catch (Exception ex) { Logger.Log($"Vsync setup failed: {ex.Message}"); }
        }

        private void ProcessRender()
        {
            if (_renderContext == IntPtr.Zero) return;

            // Check if MPV actually needs a redraw
            ulong flags = LibMpvRenderInterop.mpv_render_context_update(_renderContext);
            if ((flags & LibMpvRenderInterop.MPV_RENDER_UPDATE_FRAME) != 0)
            {
                // Prepare FBO
                OpenGLInterop.glViewport(0, 0, _width, _height);
                OpenGLInterop.glClearColor(0.0f, 0.0f, 0.0f, 1.0f);
                OpenGLInterop.glClear(OpenGLInterop.GL_COLOR_BUFFER_BIT);

                var fbo = new LibMpvRenderInterop.MpvOpenGLFBO { fbo = 0, w = _width, h = _height, internal_format = 0 };
                IntPtr fboPtr = Marshal.AllocHGlobal(Marshal.SizeOf(fbo));
                Marshal.StructureToPtr(fbo, fboPtr, false);

                int flipY = 1;
                IntPtr flipYPtr = Marshal.AllocHGlobal(sizeof(int));
                Marshal.WriteInt32(flipYPtr, flipY);

                var renderParams = new[]
                {
                    new LibMpvRenderInterop.MpvRenderParam { type = LibMpvRenderInterop.MPV_RENDER_PARAM_OPENGL_FBO, data = fboPtr },
                    new LibMpvRenderInterop.MpvRenderParam { type = LibMpvRenderInterop.MPV_RENDER_PARAM_FLIP_Y, data = flipYPtr },
                    new LibMpvRenderInterop.MpvRenderParam { type = LibMpvRenderInterop.MPV_RENDER_PARAM_INVALID, data = IntPtr.Zero }
                };

                // Marshal array
                int paramSize = Marshal.SizeOf<LibMpvRenderInterop.MpvRenderParam>();
                IntPtr paramsPtr = Marshal.AllocHGlobal(paramSize * renderParams.Length);
                for (int i = 0; i < renderParams.Length; i++)
                    Marshal.StructureToPtr(renderParams[i], IntPtr.Add(paramsPtr, i * paramSize), false);

                // Render
                LibMpvRenderInterop.mpv_render_context_render(_renderContext, paramsPtr);

                // Cleanup Marshal
                Marshal.FreeHGlobal(paramsPtr);
                Marshal.FreeHGlobal(fboPtr);
                Marshal.FreeHGlobal(flipYPtr);

                // Swap Buffers (This blocks if VSync is ON, but now it blocks the Background Thread, not UI!)
                OpenGLInterop.SwapBuffers(_deviceContext);
                LibMpvRenderInterop.mpv_render_context_report_swap(_renderContext);
            }
        }

        private void OnMpvRenderUpdate(IntPtr ctx)
        {
            // Signal the render loop to wake up
            _renderSignal.Set();
        }

        private bool CreateMpvRenderContext()
        {
            // Same context creation logic as before, but running on the thread
            GetProcAddressDelegate getProcAddress = GetProcAddressImpl;
            IntPtr getProcAddressPtr = Marshal.GetFunctionPointerForDelegate(getProcAddress);
            _callbackHandle = GCHandle.Alloc(getProcAddress);

            var initParams = new LibMpvRenderInterop.MpvOpenGLInitParams { get_proc_address = getProcAddressPtr };
            IntPtr initParamsPtr = Marshal.AllocHGlobal(Marshal.SizeOf(initParams));
            Marshal.StructureToPtr(initParams, initParamsPtr, false);

            IntPtr apiTypePtr = Marshal.StringToHGlobalAnsi("opengl");

            var renderParams = new[]
            {
                new LibMpvRenderInterop.MpvRenderParam { type = LibMpvRenderInterop.MPV_RENDER_PARAM_API_TYPE, data = apiTypePtr },
                new LibMpvRenderInterop.MpvRenderParam { type = LibMpvRenderInterop.MPV_RENDER_PARAM_OPENGL_INIT_PARAMS, data = initParamsPtr },
                new LibMpvRenderInterop.MpvRenderParam { type = LibMpvRenderInterop.MPV_RENDER_PARAM_INVALID, data = IntPtr.Zero }
            };

            int paramSize = Marshal.SizeOf<LibMpvRenderInterop.MpvRenderParam>();
            IntPtr paramsPtr = Marshal.AllocHGlobal(paramSize * renderParams.Length);
            for (int i = 0; i < renderParams.Length; i++)
                Marshal.StructureToPtr(renderParams[i], IntPtr.Add(paramsPtr, i * paramSize), false);

            int result = LibMpvRenderInterop.mpv_render_context_create(out _renderContext, _mpvHandle, paramsPtr);

            Marshal.FreeHGlobal(paramsPtr);
            Marshal.FreeHGlobal(initParamsPtr);
            Marshal.FreeHGlobal(apiTypePtr);

            if (result < 0) return false;

            _updateCallback = OnMpvRenderUpdate;
            LibMpvRenderInterop.mpv_render_context_set_update_callback(_renderContext, _updateCallback, IntPtr.Zero);
            return true;
        }

        public void Render() 
        { 
            // Manual Render call is no longer needed/used by UI thread
            // The thread handles it via _renderSignal
            _renderSignal.Set(); 
        }

        public void Resize(int width, int height)
        {
            _width = width;
            _height = height;
            _renderSignal.Set(); // Wake up thread to update viewport next frame
        }

        private void CleanupGL()
        {
            if (_renderContext != IntPtr.Zero)
            {
                LibMpvRenderInterop.mpv_render_context_free(_renderContext);
                _renderContext = IntPtr.Zero;
            }
            if (_glContext != IntPtr.Zero)
            {
                OpenGLInterop.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
                OpenGLInterop.wglDeleteContext(_glContext);
                _glContext = IntPtr.Zero;
            }
            if (_deviceContext != IntPtr.Zero && _windowHandle != IntPtr.Zero)
            {
                OpenGLInterop.ReleaseDC(_windowHandle, _deviceContext);
                _deviceContext = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            if (!_isRunning) return;
            
            _isRunning = false;
            _renderSignal.Set(); // Wake up thread so it can exit
            
            if (_renderThread != null && _renderThread.IsAlive)
            {
                _renderThread.Join(1000); // Wait for thread to finish cleanup
            }

            if (_callbackHandle.IsAllocated) _callbackHandle.Free();
            _renderSignal.Dispose();
            _initSignal.Dispose();
        }

        // Delegates and Imports helpers (Keep existing GetProcAddressImpl, etc.)
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate bool WglSwapIntervalEXT(int interval);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr GetProcAddressDelegate(IntPtr ctx, [MarshalAs(UnmanagedType.LPStr)] string name);

        private IntPtr GetProcAddressImpl(IntPtr ctx, string name)
        {
            IntPtr proc = OpenGLInterop.wglGetProcAddress(name);
            if (proc == IntPtr.Zero || proc == new IntPtr(1) || proc == new IntPtr(2) || proc == new IntPtr(3) || proc == new IntPtr(-1))
            {
                IntPtr opengl32 = GetModuleHandle("opengl32.dll");
                if (opengl32 != IntPtr.Zero) proc = GetProcAddress(opengl32, name);
            }
            return proc;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}