using Microsoft.Extensions.Logging;

using SharedLib;
using SharedLib.Extensions;

using System;
using System.Numerics;

namespace Core.GoalsComponent;

public sealed class FoundNodeListener : IDisposable
{
    private readonly ILogger<FoundNodeListener> logger;
    private readonly IMinimapImageProvider provider;
    private readonly PlayerReader playerReader;
    private readonly AddonBits addonBits;
    private readonly MinimapNodeFinder minimapNodeFinder;
    private readonly KeyAction keyAction;

    private static readonly float[] OutdoorZoomDiameters = [233.333f, 116.666f, 58.333f, 29.166f, 14.583f, 7.291f];
    private static readonly float[] IndoorZoomDiameters = [133.333f, 66.666f, 33.333f, 16.666f, 8.333f, 4.166f];

    private Vector3? lastNode;

    public FoundNodeListener(
        ILogger<FoundNodeListener> logger,
        IMinimapImageProvider provider,
        PlayerReader playerReader,
        AddonBits addonBits,
        MinimapNodeFinder minimapNodeFinder,
        KeyAction keyAction)
    {
        this.logger = logger;
        this.provider = provider;
        this.addonBits = addonBits;
        this.playerReader = playerReader;
        this.minimapNodeFinder = minimapNodeFinder;
        this.keyAction = keyAction;

        minimapNodeFinder.NodeEvent += MinimapNodeFinder_NodeEvent;
    }

    public void Dispose()
    {
        minimapNodeFinder.NodeEvent -= MinimapNodeFinder_NodeEvent;
    }

    private void MinimapNodeFinder_NodeEvent(object? sender, MinimapNodeEventArgs e)
    {
        // have to convert minimap screen cordinates to map coordinates
        Vector3 playerMapPos = playerReader.MapPos;
        float playerDirection = playerReader.Direction;

        var settings = provider.MinimapSettings;

        var array = addonBits.Indoors() ? IndoorZoomDiameters : OutdoorZoomDiameters;

        float metersPerPixel = array[settings.Zoom] / settings.Width;

        Vector2 node = new(e.X, e.Y);
        Vector2 center = e.Rect.Centre();

        float dx = node.X - center.X;
        float dy = center.Y - node.Y; // invert Y

        dy = -dy;

        Vector2 v = new(dx, dy);

        float angle = 0f; // north-up
        if (settings.RotateMinimap)
        {
            angle = -playerDirection; // de-rotate so north = +Y
        }

        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);

        Vector2 worldOffset = new(
            v.X * cos - v.Y * sin,
            v.X * sin + v.Y * cos);

        worldOffset *= metersPerPixel;

        Vector3 pos = playerMapPos + new Vector3(worldOffset, 0);

        if (pos.X < 0 || pos.X > 100 || pos.Y < 0 || pos.Y > 100)
        {
            return;
        }

        if (lastNode.HasValue && Vector3.Distance(lastNode.Value, pos) < 0.5f)
        {
            // same node within 0.5 world units → ignore
            return;
        }

        lastNode = pos;

        logger.LogWarning($"Found node at {pos.X}/{pos.Y}");
        keyAction.Path = [pos];
    }
}
