using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: DisableRuntimeMarshalling]

namespace StormDll;

internal sealed partial class StormDllx64
{
    [LibraryImport("MPQ\\StormLib_x64.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SFileOpenArchive(
        [MarshalAs(UnmanagedType.LPWStr)] string szMpqName,
        uint dwPriority,
        OpenArchive dwFlags,
        out nint phMpq);

    [LibraryImport("MPQ\\StormLib_x64.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SFileCloseArchive(nint hMpq);

    [LibraryImport("MPQ\\StormLib_x64.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SFileReadFile(
       nint fileHandle,
       Span<byte> buffer,
       [MarshalAs(UnmanagedType.I8)] long toRead,
       out long read);

    [LibraryImport("MPQ\\StormLib_x64.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SFileCloseFile(
        nint fileHandle);

    [LibraryImport("MPQ\\StormLib_x64.dll")]
    public static partial uint SFileGetFileSize(
        nint fileHandle,
        out long fileSizeHigh);

    [LibraryImport("MPQ\\StormLib_x64.dll")]
    public static partial uint SFileSetFilePointer(
        nint fileHandle,
        long filePos,
        ref uint plFilePosHigh,
        SeekOrigin origin);

    [LibraryImport("MPQ\\StormLib_x64.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SFileOpenFileEx(
        nint archiveHandle,
        ReadOnlySpan<byte> fileName,
        OpenFile searchScope,
        out nint fileHandle);
}