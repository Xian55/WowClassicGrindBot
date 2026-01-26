using Microsoft.Extensions.Logging;

using System;
using System.IO;

namespace Core;

public abstract class ScreenCaptureCleaner : IScreenCapture
{
    public ScreenCaptureCleaner(ILogger logger, DataConfig dataConfig)
    {
        try
        {
            DateTime olderThen = DateTime.UtcNow.AddDays(-7);

            DirectoryInfo di = new(dataConfig.Screenshot);
            FileInfo[] files = di.GetFiles("*.jpg");
            for (int i = files.Length - 1; i >= 0; i--)
            {
                FileInfo file = files[i];
                if (file.CreationTimeUtc < olderThen)
                {
                    file.Delete();
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
        }
    }

    public abstract void Request();
}
