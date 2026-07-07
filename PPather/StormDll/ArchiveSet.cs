using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.IO;

namespace StormDll;

public sealed class ArchiveSet
{
    private readonly Archive[] archives;
    private readonly ILogger logger;

    public ArchiveSet(ILogger logger, string[] files)
    {
        this.logger = logger;

        // Only keep successfully opened archives. A failed open used to leave a
        // null slot that later NRE'd in GetStream/Exists. Now it is skipped and
        // logged, so a bad/mismatched native StormLib surfaces a clear message.
        List<Archive> opened = new(files.Length);

        for (int i = 0; i < files.Length; i++)
        {
            Archive a = new(files[i], out bool open, 0,
                OpenArchive.MPQ_OPEN_NO_LISTFILE |
                OpenArchive.MPQ_OPEN_NO_ATTRIBUTES |
                OpenArchive.MPQ_OPEN_NO_HEADER_SEARCH |
                OpenArchive.MPQ_OPEN_READ_ONLY);

            if (open && a.IsOpen())
            {
                opened.Add(a);

                if (logger.IsEnabled(LogLevel.Trace))
                    logger.LogTrace("Archive[{Index}] open {File}", i, files[i]);
            }
            else
                logger.LogWarning("Archive failed to open: {File}", files[i]);
        }

        archives = opened.ToArray();

        if (archives.Length == 0)
            logger.LogError(
                "No MPQ archive could be opened from {Count} file(s). " +
                "The native StormLib may be missing or built for the wrong " +
                "architecture/charset - pathing will not work.", files.Length);
    }

    public MpqFileStream GetStream(ReadOnlySpan<char> fileName)
    {
        for (int i = 0; i < archives.Length; i++)
        {
            Archive a = archives[i];
            if (a.HasFile(fileName))
                return a.GetStream(fileName);
        }

        logger.LogWarning("fileName not found '{FileName}'", fileName.ToString());
        throw new FileNotFoundException($"{nameof(fileName)} - {fileName}");
    }

    public bool Exists(ReadOnlySpan<char> fileName)
    {
        for (int i = 0; i < archives.Length; i++)
        {
            Archive a = archives[i];
            if (a.HasFile(fileName))
                return true;
        }
        return false;
    }

    public void Close()
    {
        for (int i = 0; i < archives.Length; i++)
            archives[i].SFileCloseArchive();
    }
}