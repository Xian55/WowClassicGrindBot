using System;
using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace StormDll;

internal sealed class Archive
{
    public const uint SFILE_INVALID_SIZE = 0xFFFFFFFF;

    private readonly IntPtr handle;

    private readonly FrozenSet<string> fileList;

    public Archive(string file, out bool open, uint prio, OpenArchive flags)
    {
        open = StormDll.SFileOpenArchive(file, prio, flags, out handle);

        if (!open)
            return;

        using MpqFileStream mpq = GetStream("(listfile)".AsSpan());
        int length = (int)mpq.Length;

        HashSet<string> fileList = new(StringComparer.InvariantCultureIgnoreCase);

        if (length <= MpqFileStream.MaxStackLimit)
        {
            Span<byte> stackBytes = stackalloc byte[length];
            mpq.Read(stackBytes);
            ParseFileLines(stackBytes, fileList);
        }
        else
        {
            var pooler = ArrayPool<byte>.Shared;
            byte[] array = pooler.Rent(length);
            try
            {
                Span<byte> spanBytes = array.AsSpan(0, length);
                mpq.Read(spanBytes);
                ParseFileLines(spanBytes, fileList);
            }
            finally
            {
                pooler.Return(array);
            }
        }

        if (fileList.Count == 0)
            throw new InvalidOperationException($"{nameof(fileList)} contains no elements!");

        this.fileList = fileList.ToFrozenSet(StringComparer.InvariantCultureIgnoreCase);
    }

    public static void ParseFileLines(ReadOnlySpan<byte> data, HashSet<string> fileList)
    {
        string content = Encoding.UTF8.GetString(data);
        ReadOnlySpan<char> span = content.AsSpan();

        int start = 0;
        while (start < span.Length)
        {
            int end = span[start..].IndexOf('\n');
            if (end == -1)
            {
                end = span.Length - start;
            }

            ReadOnlySpan<char> lineSpan = span.Slice(start, end).TrimEnd('\r');

            fileList.Add(lineSpan.ToString());

            start += end + 1;
        }

        if (fileList.Count == 0)
            throw new InvalidOperationException("File contains no lines.");
    }


    public bool IsOpen()
    {
        return handle != IntPtr.Zero;
    }

    public bool HasFile(string name) => fileList.Contains(name);

    public bool HasFile(ReadOnlySpan<char> name)
    {
        var lookup = fileList.GetAlternateLookup<ReadOnlySpan<char>>();
        return lookup.Contains(name);
    }

    public bool SFileCloseArchive()
    {
        return StormDll.SFileCloseArchive(handle);
    }

    [Obsolete("Use GetStream instead.")]
    public MpqFileStream GetStream(string fileName)
    {
        return !SFileOpenFileEx(handle, fileName, OpenFile.SFILE_OPEN_FROM_MPQ, out IntPtr fileHandle)
            ? throw new IOException("SFileOpenFileEx failed")
            : new MpqFileStream(fileHandle);
    }

    public MpqFileStream GetStream(ReadOnlySpan<char> fileName)
    {
        return !SFileOpenFileEx(handle, fileName, OpenFile.SFILE_OPEN_FROM_MPQ, out IntPtr fileHandle)
            ? throw new IOException("SFileOpenFileEx failed")
            : new MpqFileStream(fileHandle);
    }

    public static bool SFileReadFile(IntPtr fileHandle, Span<byte> buffer, long toRead, out long read)
    {
        return StormDll.SFileReadFile(fileHandle, buffer, toRead, out read);
    }

    public static bool SFileCloseFile(IntPtr fileHandle)
    {
        return StormDll.SFileCloseFile(fileHandle);
    }

    public static long SFileGetFileSize(IntPtr fileHandle, out long fileSizeHigh)
    {
        return StormDll.SFileGetFileSize(fileHandle, out fileSizeHigh);
    }

    public static uint SFileSetFilePointer(IntPtr fileHandle,
        long filePos,
        ref uint plFilePosHigh,
        SeekOrigin origin)
    {
        return StormDll.SFileSetFilePointer(fileHandle, filePos, ref plFilePosHigh, origin);
    }

    public static bool SFileOpenFileEx(
        IntPtr archiveHandle,
        ReadOnlySpan<char> fileName,
        OpenFile searchScope,
        out IntPtr fileHandle)
    {
        // Convert the fileName of Uft16 to Utf8 with null terminated string
        Span<byte> utf8Bytes = stackalloc byte[Encoding.UTF8.GetByteCount(fileName) + 1];
        Encoding.UTF8.GetBytes(fileName, utf8Bytes);
        utf8Bytes[^1] = 0;

        return StormDll.SFileOpenFileEx(archiveHandle, utf8Bytes, searchScope, out fileHandle);
    }
}