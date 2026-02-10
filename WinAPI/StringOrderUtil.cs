using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security;

namespace WinAPI;

[SuppressUnmanagedCodeSecurity]
internal static partial class SafeNativeMethods
{
    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int StrCmpLogicalW(string psz1, string psz2);
}

public sealed class NaturalStringComparer : IComparer<string>
{
    public int Compare(string x, string y)
    {
        // shlwapi.dll exists only on Windows; fall back to managed compare elsewhere
        if (!OperatingSystem.IsWindows())
        {
            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }

        return SafeNativeMethods.StrCmpLogicalW(x, y);
    }
}
