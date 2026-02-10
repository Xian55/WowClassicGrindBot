using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace StormDll;

internal sealed partial class StormDllx86
{
    [LibraryImport(StormLibNative.LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SFileOpenArchive(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string szMpqName,
        uint dwPriority,
        OpenArchive dwFlags,
        out nint phMpq);

    [LibraryImport(StormLibNative.LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SFileCloseArchive(nint hMpq);

    [LibraryImport(StormLibNative.LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SFileReadFile(
       nint fileHandle,
       Span<byte> buffer,
       [MarshalAs(UnmanagedType.I8)] Int64 toRead,
       out long read);

    [LibraryImport(StormLibNative.LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SFileCloseFile(
        nint fileHandle);

    [LibraryImport(StormLibNative.LibraryName)]
    public static partial uint SFileGetFileSize(
        nint fileHandle,
        out long fileSizeHigh);

    [LibraryImport(StormLibNative.LibraryName)]
    public static partial uint SFileSetFilePointer(
        nint fileHandle,
        long filePos,
        ref uint plFilePosHigh,
        SeekOrigin origin);

    [LibraryImport(StormLibNative.LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SFileOpenFileEx(
        nint archiveHandle,
        ReadOnlySpan<byte> fileName,
        OpenFile searchScope,
        out nint fileHandle);
}