using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace StormDll;

internal static class StormLibNative
{
    public const string LibraryName = "StormLib.Native";

    [ModuleInitializer]
    internal static void Initialize()
    {
        NativeLibrary.SetDllImportResolver(typeof(StormLibNative).Assembly, Resolve);
    }

    private static nint Resolve(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal))
        {
            return 0;
        }

        if (OperatingSystem.IsWindows())
        {
            string fileName = Environment.Is64BitProcess ? "StormLib_x64.dll" : "StormLib_x86.dll";
            string candidate = Path.Combine(AppContext.BaseDirectory, "MPQ", fileName);
            if (NativeLibrary.TryLoad(candidate, out nint handle))
            {
                return handle;
            }
        }

        if (OperatingSystem.IsLinux())
        {
            string[] candidates =
            {
                Path.Combine(AppContext.BaseDirectory, "libstorm.so"),
                Path.Combine(AppContext.BaseDirectory, "libStormLib.so"),
                "/usr/local/lib/libstorm.so",
                "/usr/local/lib/libStormLib.so",
            };

            foreach (string candidate in candidates)
            {
                if (NativeLibrary.TryLoad(candidate, out nint handle))
                {
                    return handle;
                }
            }
        }

        return 0;
    }
}
