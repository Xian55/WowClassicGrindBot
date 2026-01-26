using Core;

using Game;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;

using System;
using System.Diagnostics;

#pragma warning disable 0162

#nullable enable

namespace CoreTests;

internal sealed class Test_MinimapNodeFinder
{
    private const bool saveImage = false;
    private const bool LogEachUpdate = false;

    private readonly ILogger logger;
    private readonly IWowScreen screen;

    private readonly MinimapNodeFinder minimapNodeFinder;

    private readonly Stopwatch stopwatch;

    public Test_MinimapNodeFinder(ILogger logger,
        IWowScreen screen, EventHandler<MinimapNodeEventArgs>? NodeEvent)
    {
        this.logger = logger;
        this.screen = screen;

        stopwatch = new();

        minimapNodeFinder = new(logger, screen);

        minimapNodeFinder.NodeEvent += NodeEvent;
    }

    public void Execute()
    {
        if (LogEachUpdate)
            stopwatch.Restart();

        screen.Update();

        if (LogEachUpdate)
            logger.LogInformation($"Capture: {stopwatch.ElapsedMilliseconds}ms");

        if (LogEachUpdate)
            stopwatch.Restart();

        minimapNodeFinder.Update();

        if (LogEachUpdate)
            logger.LogInformation($"Update: {stopwatch.ElapsedMilliseconds}ms");

        if (saveImage)
        {
            SaveImage();
        }
    }

    private void SaveImage()
    {
        screen.MiniMapImage.SaveAsPng("minimap.png");
    }
}
