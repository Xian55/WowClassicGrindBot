using Game;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;

using System;
using System.IO;
using System.Threading;

namespace Core;

public sealed partial class ScreenCapture : ScreenCaptureCleaner, IDisposable
{
    private readonly ILogger<ScreenCapture> logger;
    private readonly DataConfig dataConfig;
    private readonly IWowScreen screen;
    private readonly CancellationToken token;

    private readonly ManualResetEventSlim manualReset;
    private readonly Thread thread;

    public ScreenCapture(ILogger<ScreenCapture> logger, DataConfig dataConfig,
        CancellationTokenSource cts, IWowScreen screen)
        : base(logger, dataConfig)
    {
        this.logger = logger;
        this.dataConfig = dataConfig;
        this.token = cts.Token;
        this.screen = screen;

        manualReset = new(false);
        thread = new(Thread);
        thread.Start();
    }

    public void Dispose()
    {
        manualReset.Set();
    }

    private void Thread()
    {
        manualReset.Wait();

        while (!token.IsCancellationRequested)
        {
            manualReset.Reset();
            try
            {
                string fileName = $"{DateTimeOffset.Now:MM_dd_HH_mm_ss_fff}.jpg";
                LogScreenCapture(logger, fileName);

                screen.ScreenImage.SaveAsJpeg(Path.Join(dataConfig.Screenshot, fileName));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, ex.Message);
            }
            manualReset.Wait();
        }

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug($"Thread stopped!");
    }

    public override void Request()
    {
        manualReset.Set();
    }


    #region Logging 

    [LoggerMessage(
        EventId = 0111,
        Level = LogLevel.Information,
        Message = "{fileName}")]
    static partial void LogScreenCapture(ILogger logger, string fileName);

    #endregion
}
