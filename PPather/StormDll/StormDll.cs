using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: DisableRuntimeMarshalling]

namespace StormDll;

internal static partial class StormDll
{
    private const string LibraryName = "StormLib";

    // Runs once before the first StormLib P/Invoke below (all are static members
    // of this class), so the resolver decides which native binary to load per
    // architecture: x64, x86 or arm64.
    static StormDll()
    {
        NativeLibrary.SetDllImportResolver(typeof(StormDll).Assembly, Resolve);
    }

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibraryName)
        {
            return nint.Zero;
        }

        string fileName = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "StormLib_x64.dll",
            Architecture.X86 => "StormLib_x86.dll",
            Architecture.Arm64 => "StormLib_arm64.dll",
            _ => throw new PlatformNotSupportedException(
                $"StormLib: unsupported process architecture {RuntimeInformation.ProcessArchitecture}"),
        };

        return NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "MPQ", fileName));
    }

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SFileOpenArchive(
        [MarshalAs(UnmanagedType.LPWStr)] string szMpqName,
        uint dwPriority,
        OpenArchive dwFlags,
        out nint phMpq);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SFileCloseArchive(nint hMpq);

    [LibraryImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SFileReadFile(
       nint fileHandle,
       Span<byte> buffer,
       [MarshalAs(UnmanagedType.I8)] long toRead,
       out long read);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SFileCloseFile(
        nint fileHandle);

    [LibraryImport(LibraryName)]
    public static partial uint SFileGetFileSize(
        nint fileHandle,
        out long fileSizeHigh);

    [LibraryImport(LibraryName)]
    public static partial uint SFileSetFilePointer(
        nint fileHandle,
        long filePos,
        ref uint plFilePosHigh,
        SeekOrigin origin);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SFileOpenFileEx(
        nint archiveHandle,
        ReadOnlySpan<byte> fileName,
        OpenFile searchScope,
        out nint fileHandle);
}
