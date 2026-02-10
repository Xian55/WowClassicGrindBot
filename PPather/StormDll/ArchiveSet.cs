using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.IO;

namespace StormDll;

public sealed class ArchiveSet
{
    private readonly Archive?[] archives;
    private readonly string[] archiveNames;
    private readonly ILogger logger;

    public ArchiveSet(ILogger logger, string[] files)
    {
        this.logger = logger;
        archiveNames = files;
        archives = new Archive?[files.Length];

        int openedArchives = 0;

        for (int i = 0; i < files.Length; i++)
        {
            string file = files[i];

            Archive a = new(file, out bool open, 0,
                OpenArchive.MPQ_OPEN_NO_LISTFILE |
                OpenArchive.MPQ_OPEN_NO_ATTRIBUTES |
                OpenArchive.MPQ_OPEN_NO_HEADER_SEARCH |
                OpenArchive.MPQ_OPEN_READ_ONLY);

            if (open && a.IsOpen())
            {
                archives[i] = a;
                openedArchives++;

                if (logger.IsEnabled(LogLevel.Trace))
                    logger.LogTrace("Archive[{Index}] open {File}", i, file);
            }
            else
            {
                logger.LogWarning("Archive[{Index}] failed to open {File}", i, file);
            }
        }

        logger.LogInformation("ArchiveSet opened {Opened}/{Total} archives", openedArchives, archives.Length);

        if (openedArchives == 0)
        {
            throw new InvalidOperationException($"No MPQ archives could be opened. Searched: {string.Join(", ", archiveNames)}");
        }
    }

    public MpqFileStream GetStream(ReadOnlySpan<char> fileName)
    {
        for (int i = 0; i < archives.Length; i++)
        {
            Archive? a = archives[i];
            if (a == null)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug("Archive[{Index}] is null (failed open), skipping", i);

                continue;
            }

            if (a.HasFile(fileName))
            {
                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug("Found {FileName} in archive[{Index}]", fileName.ToString(), i);

                return a.GetStream(fileName);
            }
        }

        logger.LogWarning("fileName not found '{FileName}' (searched {ArchiveCount} archives: {Archives})",
            fileName.ToString(), archives.Length, string.Join(", ", archiveNames));
        throw new FileNotFoundException($"{nameof(fileName)} - {fileName}");
    }

    public bool Exists(ReadOnlySpan<char> fileName)
    {
        for (int i = 0; i < archives.Length; i++)
        {
            Archive? a = archives[i];
            if (a == null)
                continue;

            if (a.HasFile(fileName))
                return true;
        }
        return false;
    }

    public void Close()
    {
        for (int i = 0; i < archives.Length; i++)
        {
            Archive? a = archives[i];
            if (a == null)
                continue;

            a.SFileCloseArchive();
        }
    }

    public IReadOnlyList<string> ArchiveNames => archiveNames;
}