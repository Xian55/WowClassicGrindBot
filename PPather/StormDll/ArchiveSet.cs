using Microsoft.Extensions.Logging;

using System;
using System.IO;

namespace StormDll;

public sealed class ArchiveSet
{
    private readonly Archive[] archives;
    private readonly ILogger logger;

    public ArchiveSet(ILogger logger, string[] files)
    {
        this.logger = logger;
        archives = new Archive[files.Length];

        for (int i = 0; i < files.Length; i++)
        {
            Archive a = new(files[i], out bool open, 0,
                OpenArchive.MPQ_OPEN_NO_LISTFILE |
                OpenArchive.MPQ_OPEN_NO_ATTRIBUTES |
                OpenArchive.MPQ_OPEN_NO_HEADER_SEARCH |
                OpenArchive.MPQ_OPEN_READ_ONLY);

            if (open && a.IsOpen())
            {
                archives[i] = a;

                if (logger.IsEnabled(LogLevel.Trace))
                    logger.LogTrace($"Archive[{i}] open {files[i]}");
            }
            else if (logger.IsEnabled(LogLevel.Trace))
                logger.LogTrace($"Archive[{i}] openfail {files[i]}");
        }
    }

    public MpqFileStream GetStream(ReadOnlySpan<char> fileName)
    {
        for (int i = 0; i < archives.Length; i++)
        {
            Archive a = archives[i];
            if (a.HasFile(fileName))
                return a.GetStream(fileName);
        }

        logger.LogWarning($"{nameof(fileName)} not found '{fileName}'");
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