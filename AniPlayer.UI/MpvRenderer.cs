using System;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Threading;

namespace AniPlayer.UI
{
    /// <summary>
    /// Handles libmpv render API integration with OpenGL
    /// </summary>
    public class MpvRenderer : IDisposable
    {
        private IntPtr _mpvHandle;
        private IntPtr _renderContext;
        private IntPtr _windowHandle;
        private IntPtr _deviceContext;
        private IntPtr _glContext;
        private int _width;
        private int _height;
        private bool _disposed;
        private LibMpvRenderInterop.MpvRenderUpdateCallback? _updateCallback;
        private GCHandle _callbackHandle;

        public event Action? RenderNeeded;

        public bool IsInitialized => _renderContext != IntPtr.Zero;

        public MpvRenderer(IntPtr mpvHandle, IntPtr windowHandle)
        {
            Logger.Log($"=== MpvRenderer Constructor START ===");
            _mpvHandle = mpvHandle;
            _windowHandle = windowHandle;
            Logger.Log($"MPV handle: {mpvHandle}, Window handle: {windowHandle}");
        }

        public bool Initialize(int width, int height)
        {
            Logger.Log($"=== MpvRenderer Initialize START (size: {width}x{height}) ===");

            try
            {
                _width = width;
                _height = height;

                // Get device context for the window
                Logger.Log("Getting device context...");
                _deviceContext = OpenGLInterop.GetDC(_windowHandle);
                if (_deviceContext == IntPtr.Zero)
                {
                    Logger.LogError("Failed to get device context");
                    return false;
                }
                Logger.Log($"Device context: {_deviceContext}");

                // Set pixel format
                Logger.Log("Setting pixel format...");
                var pfd = OpenGLInterop.GetDefaultPixelFormatDescriptor();
                int pixelFormat = OpenGLInterop.ChoosePixelFormat(_deviceContext, ref pfd);
                Logger.Log($"Chosen pixel format: {pixelFormat}");

                if (pixelFormat == 0 || !OpenGLInterop.SetPixelFormat(_deviceContext, pixelFormat, ref pfd))
                {
                    Logger.LogError("Failed to set pixel format");
                    return false;
                }

                // Create OpenGL context
                Logger.Log("Creating OpenGL context...");
                _glContext = OpenGLInterop.wglCreateContext(_deviceContext);
                if (_glContext == IntPtr.Zero)
                {
                    Logger.LogError("Failed to create OpenGL context");
                    return false;
                }
                Logger.Log($"OpenGL context: {_glContext}");

                // Make context current
                if (!OpenGLInterop.wglMakeCurrent(_deviceContext, _glContext))
                {
                    Logger.LogError("Failed to make OpenGL context current");
                    return false;
                }

                Logger.Log("OpenGL context is current");

                // Log OpenGL version info
                try
                {
                    IntPtr versionPtr = OpenGLInterop.glGetString(OpenGLInterop.GL_VERSION);
                    IntPtr vendorPtr = OpenGLInterop.glGetString(OpenGLInterop.GL_VENDOR);
                    IntPtr rendererPtr = OpenGLInterop.glGetString(OpenGLInterop.GL_RENDERER);

                    string? version = versionPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(versionPtr) : "unknown";
                    string? vendor = vendorPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(vendorPtr) : "unknown";
                    string? renderer = rendererPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(rendererPtr) : "unknown";

                    Logger.Log($"OpenGL Version: {version}");
                    Logger.Log($"OpenGL Vendor: {vendor}");
                    Logger.Log($"OpenGL Renderer: {renderer}");
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to get OpenGL info: {ex.Message}");
                }

                // Create libmpv render context with OpenGL
                Logger.Log("Creating libmpv render context...");
                if (!CreateMpvRenderContext())
                {
                    Logger.LogError("Failed to create MPV render context");
                    return false;
                }

                Logger.Log("=== MpvRenderer Initialize END (SUCCESS) ===");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError("MpvRenderer Initialize exception", ex);
                return false;
            }
        }

        private bool CreateMpvRenderContext()
        {
            try
            {
                // Get proc address function pointer
                GetProcAddressDelegate getProcAddress = GetProcAddressImpl;
                IntPtr getProcAddressPtr = Marshal.GetFunctionPointerForDelegate(getProcAddress);

                // Keep delegate alive
                _callbackHandle = GCHandle.Alloc(getProcAddress);

                // Create OpenGL init params
                var initParams = new LibMpvRenderInterop.MpvOpenGLInitParams
                {
                    get_proc_address = getProcAddressPtr,
                    get_proc_address_ctx = IntPtr.Zero
                };

                IntPtr initParamsPtr = Marshal.AllocHGlobal(Marshal.SizeOf(initParams));
                Marshal.StructureToPtr(initParams, initParamsPtr, false);

                // API type string
                IntPtr apiTypePtr = Marshal.StringToHGlobalAnsi("opengl");

                // Create render params as contiguous array in memory
                var renderParams = new LibMpvRenderInterop.MpvRenderParam[3];
                renderParams[0] = new LibMpvRenderInterop.MpvRenderParam
                {
                    type = LibMpvRenderInterop.MPV_RENDER_PARAM_API_TYPE,
                    data = apiTypePtr
                };
                renderParams[1] = new LibMpvRenderInterop.MpvRenderParam
                {
                    type = LibMpvRenderInterop.MPV_RENDER_PARAM_OPENGL_INIT_PARAMS,
                    data = initParamsPtr
                };
                renderParams[2] = new LibMpvRenderInterop.MpvRenderParam
                {
                    type = LibMpvRenderInterop.MPV_RENDER_PARAM_INVALID,
                    data = IntPtr.Zero
                };

                // Allocate contiguous memory for the array
                int paramSize = Marshal.SizeOf<LibMpvRenderInterop.MpvRenderParam>();
                IntPtr paramsPtr = Marshal.AllocHGlobal(paramSize * renderParams.Length);

                // Copy each struct to contiguous memory
                for (int i = 0; i < renderParams.Length; i++)
                {
                    IntPtr offset = IntPtr.Add(paramsPtr, i * paramSize);
                    Marshal.StructureToPtr(renderParams[i], offset, false);
                }

                Logger.Log("Parameter array created in contiguous memory");
                Logger.Log($"  Param 0: type={renderParams[0].type}, data={renderParams[0].data}");
                Logger.Log($"  Param 1: type={renderParams[1].type}, data={renderParams[1].data}");
                Logger.Log($"  Param 2: type={renderParams[2].type}, data={renderParams[2].data}");

                // Create render context
                Logger.Log("Calling mpv_render_context_create...");
                int result = LibMpvRenderInterop.mpv_render_context_create(
                    out _renderContext,
                    _mpvHandle,
                    paramsPtr);

                Logger.Log($"mpv_render_context_create returned: {result}");

                // Cleanup
                Marshal.FreeHGlobal(paramsPtr);
                Marshal.FreeHGlobal(initParamsPtr);
                Marshal.FreeHGlobal(apiTypePtr);

                if (result < 0)
                {
                    Logger.LogError($"mpv_render_context_create failed with error: {result}");
                    return false;
                }

                Logger.Log($"Render context created: {_renderContext}");

                // Set update callback
                _updateCallback = OnMpvRenderUpdate;
                LibMpvRenderInterop.mpv_render_context_set_update_callback(
                    _renderContext,
                    _updateCallback,
                    IntPtr.Zero);

                Logger.Log("Render update callback set");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError("CreateMpvRenderContext exception", ex);
                return false;
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr GetProcAddressDelegate(IntPtr ctx, [MarshalAs(UnmanagedType.LPStr)] string name);

        private IntPtr GetProcAddressImpl(IntPtr ctx, string name)
        {
            // wglGetProcAddress only returns extension function pointers, not core functions
            // For core functions, we need to use GetProcAddress on opengl32.dll
            IntPtr proc = OpenGLInterop.wglGetProcAddress(name);

            if (proc == IntPtr.Zero || proc == new IntPtr(1) || proc == new IntPtr(2) || proc == new IntPtr(3) || proc == new IntPtr(-1))
            {
                // Try to load from opengl32.dll for core functions
                IntPtr opengl32 = GetModuleHandle("opengl32.dll");
                if (opengl32 != IntPtr.Zero)
                {
                    proc = GetProcAddress(opengl32, name);
                }
            }

            return proc;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private void OnMpvRenderUpdate(IntPtr ctx)
        {
            // This is called from MPV thread when it wants to render
            // Marshal to UI thread
            try
            {
                Logger.Log("OnMpvRenderUpdate called - requesting render");
                Dispatcher.UIThread.Post(() =>
                {
                    Logger.Log("Dispatched render request to UI thread");
                    RenderNeeded?.Invoke();
                });
            }
            catch (Exception ex)
            {
                Logger.LogError("OnMpvRenderUpdate exception", ex);
            }
        }

        public void Render()
        {
            Logger.Log($"Render() called - context: {_renderContext}, glContext: {_glContext}");

            if (_renderContext == IntPtr.Zero || _glContext == IntPtr.Zero)
            {
                Logger.LogError("Render() called but contexts are null");
                return;
            }

            try
            {
                Logger.Log("Starting render...");
                // Make context current
                OpenGLInterop.wglMakeCurrent(_deviceContext, _glContext);

                // Clear to black
                OpenGLInterop.glClearColor(0.0f, 0.0f, 0.0f, 1.0f);
                OpenGLInterop.glClear(OpenGLInterop.GL_COLOR_BUFFER_BIT | OpenGLInterop.GL_DEPTH_BUFFER_BIT);

                // Set viewport
                OpenGLInterop.glViewport(0, 0, _width, _height);

                // Create FBO params (render to default framebuffer = 0)
                var fbo = new LibMpvRenderInterop.MpvOpenGLFBO
                {
                    fbo = 0,
                    w = _width,
                    h = _height,
                    internal_format = 0 // Use default
                };

                IntPtr fboPtr = Marshal.AllocHGlobal(Marshal.SizeOf(fbo));
                Marshal.StructureToPtr(fbo, fboPtr, false);

                int flipY = 1; // Flip Y coordinate
                IntPtr flipYPtr = Marshal.AllocHGlobal(sizeof(int));
                Marshal.WriteInt32(flipYPtr, flipY);

                // Create render params
                var renderParams = new[]
                {
                    new LibMpvRenderInterop.MpvRenderParam
                    {
                        type = LibMpvRenderInterop.MPV_RENDER_PARAM_OPENGL_FBO,
                        data = fboPtr
                    },
                    new LibMpvRenderInterop.MpvRenderParam
                    {
                        type = LibMpvRenderInterop.MPV_RENDER_PARAM_FLIP_Y,
                        data = flipYPtr
                    },
                    new LibMpvRenderInterop.MpvRenderParam
                    {
                        type = LibMpvRenderInterop.MPV_RENDER_PARAM_INVALID,
                        data = IntPtr.Zero
                    }
                };

                // Allocate contiguous memory for the render params array
                int renderParamSize = Marshal.SizeOf<LibMpvRenderInterop.MpvRenderParam>();
                IntPtr renderParamsPtr = Marshal.AllocHGlobal(renderParamSize * renderParams.Length);

                // Copy each struct to contiguous memory
                for (int i = 0; i < renderParams.Length; i++)
                {
                    IntPtr offset = IntPtr.Add(renderParamsPtr, i * renderParamSize);
                    Marshal.StructureToPtr(renderParams[i], offset, false);
                }

                // Render
                Logger.Log("Calling mpv_render_context_render...");
                LibMpvRenderInterop.mpv_render_context_render(_renderContext, renderParamsPtr);
                Logger.Log("mpv_render_context_render completed");

                // Cleanup
                Marshal.FreeHGlobal(renderParamsPtr);
                Marshal.FreeHGlobal(fboPtr);
                Marshal.FreeHGlobal(flipYPtr);

                // Swap buffers
                Logger.Log("Swapping buffers...");
                OpenGLInterop.SwapBuffers(_deviceContext);

                // Report swap to mpv
                Logger.Log("Reporting swap to MPV...");
                LibMpvRenderInterop.mpv_render_context_report_swap(_renderContext);
                Logger.Log("Render cycle complete");
            }
            catch (Exception ex)
            {
                Logger.LogError("Render exception", ex);
            }
        }

        public void Resize(int width, int height)
        {
            _width = width;
            _height = height;
            Logger.Log($"MpvRenderer resized to {width}x{height}");
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            Logger.Log("Disposing MpvRenderer...");

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

            if (_callbackHandle.IsAllocated)
            {
                _callbackHandle.Free();
            }

            _disposed = true;
            Logger.Log("MpvRenderer disposed");
        }
    }
}
