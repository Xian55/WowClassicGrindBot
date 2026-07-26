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
    // OS and architecture.
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

        // The Windows binaries are named per-architecture; the unix ones are not,
        // because a build only ever targets the host architecture.
        string fileName;
        if (OperatingSystem.IsWindows())
        {
            fileName = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "StormLib_x64.dll",
                Architecture.X86 => "StormLib_x86.dll",
                Architecture.Arm64 => "StormLib_arm64.dll",
                _ => throw new PlatformNotSupportedException(
                    $"StormLib: unsupported process architecture {RuntimeInformation.ProcessArchitecture}"),
            };
        }
        else if (OperatingSystem.IsMacOS())
        {
            fileName = "libstorm.dylib";
        }
        else if (OperatingSystem.IsLinux())
        {
            fileName = "libstorm.so";
        }
        else
        {
            throw new PlatformNotSupportedException(
                $"StormLib: unsupported platform {RuntimeInformation.OSDescription}");
        }

        return NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "MPQ", fileName));
    }

    /// <summary>
    /// StormLib types archive paths as <c>TCHAR</c>. On Windows we ship
    /// <c>STORM_UNICODE=ON</c> builds so that is UTF-16, but
    /// <c>StormPort.h</c>'s non-Windows branch does <c>typedef char TCHAR</c> -
    /// there is no wide API to build against - so a macOS/Linux build wants UTF-8.
    ///
    /// This matters because the mismatch is SILENT: a wide string handed to an
    /// ANSI <c>SFileOpenArchive</c> is not rejected, it simply fails to find the
    /// file, so every archive "does not exist" and the failure only surfaces much
    /// later as missing geometry. That is exactly how issue #803 presented on
    /// Windows ARM64 before the DLL was rebuilt with STORM_UNICODE=ON.
    ///
    /// <see cref="SFileOpenFileEx"/> needs no such split - names *inside* an
    /// archive are <c>const char*</c> on every platform.
    /// </summary>
    public static bool SFileOpenArchive(string szMpqName, uint dwPriority, OpenArchive dwFlags, out nint phMpq) =>
        OperatingSystem.IsWindows()
            ? SFileOpenArchiveW(szMpqName, dwPriority, dwFlags, out phMpq)
            : SFileOpenArchiveA(szMpqName, dwPriority, dwFlags, out phMpq);

    [LibraryImport(LibraryName, EntryPoint = "SFileOpenArchive")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SFileOpenArchiveW(
        [MarshalAs(UnmanagedType.LPWStr)] string szMpqName,
        uint dwPriority,
        OpenArchive dwFlags,
        out nint phMpq);

    [LibraryImport(LibraryName, EntryPoint = "SFileOpenArchive", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SFileOpenArchiveA(
        string szMpqName,
        uint dwPriority,
        OpenArchive dwFlags,
        out nint phMpq);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SFileCloseArchive(nint hMpq);

    /// <summary>
    /// Attaches a patch archive to an already-open base archive. Blizzard's
    /// `wow-update-*.MPQ` archives hold incremental PTCH deltas rather than whole
    /// files, so they are only readable through a base archive's patch chain -
    /// opening one standalone yields a PTCH blob, not the file.
    /// szPatchPathPrefix is passed as NULL (nint.Zero); StormLib then derives the
    /// prefix itself, which is what the base/locale update archives need.
    /// The patch path is <c>TCHAR</c> like <see cref="SFileOpenArchive"/>, hence
    /// the same wide/UTF-8 split; the prefix is <c>const char*</c> everywhere.
    /// </summary>
    public static bool SFileOpenPatchArchive(nint hMpq, string szPatchMpqName, nint szPatchPathPrefix, uint dwFlags) =>
        OperatingSystem.IsWindows()
            ? SFileOpenPatchArchiveW(hMpq, szPatchMpqName, szPatchPathPrefix, dwFlags)
            : SFileOpenPatchArchiveA(hMpq, szPatchMpqName, szPatchPathPrefix, dwFlags);

    [LibraryImport(LibraryName, EntryPoint = "SFileOpenPatchArchive")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SFileOpenPatchArchiveW(
        nint hMpq,
        [MarshalAs(UnmanagedType.LPWStr)] string szPatchMpqName,
        nint szPatchPathPrefix,
        uint dwFlags);

    [LibraryImport(LibraryName, EntryPoint = "SFileOpenPatchArchive", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SFileOpenPatchArchiveA(
        nint hMpq,
        string szPatchMpqName,
        nint szPatchPathPrefix,
        uint dwFlags);

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
