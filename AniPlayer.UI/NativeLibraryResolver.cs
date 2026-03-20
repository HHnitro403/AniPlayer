using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AniPlayer.UI
{
    internal static class NativeLibraryResolver
    {
        public static void Register()
        {
            NativeLibrary.SetDllImportResolver(typeof(LibMpvInterop).Assembly, Resolve);
        }

        private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (OperatingSystem.IsLinux())
            {
                // libmpv: DllImport uses the Windows name, map it to the Linux SO
                if (libraryName == "libmpv-2.dll")
                {
                    if (NativeLibrary.TryLoad("libmpv.so.2", assembly, searchPath, out IntPtr h)) return h;
                    if (NativeLibrary.TryLoad("libmpv.so",   assembly, searchPath, out h))       return h;
                }
            }

            // All other libraries: use default resolution
            return IntPtr.Zero;
        }
    }
}
