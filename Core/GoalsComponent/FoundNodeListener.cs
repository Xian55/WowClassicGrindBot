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

    private static readonly float[] OutdoorZoomDiametersYards =
        [233.33f, 116.67f, 58.33f, 29.17f, 14.58f, 7.29f];

    private static readonly float[] IndoorZoomDiametersYards =
        [133.33f, 66.67f, 33.33f, 16.67f, 8.33f, 4.17f];

    public event Action<Vector3>? NodeFound;

    public FoundNodeListener(
        ILogger<FoundNodeListener> logger,
        IMinimapImageProvider provider,
        PlayerReader playerReader,
        AddonBits addonBits,
        MinimapNodeFinder minimapNodeFinder)
    {
        this.logger = logger;
        this.provider = provider;
        this.addonBits = addonBits;
        this.playerReader = playerReader;
        this.minimapNodeFinder = minimapNodeFinder;

        minimapNodeFinder.NodeEvent += MinimapNodeFinder_NodeEvent;
    }

    public void Dispose()
    {
        minimapNodeFinder.NodeEvent -= MinimapNodeFinder_NodeEvent;
    }

    private void MinimapNodeFinder_NodeEvent(object? sender, MinimapNodeEventArgs e)
    {
        if (e.Amount == 0)
        {
            NodeFound?.Invoke(default);
            return;
        }

        // have to convert minimap screen cordinates to map coordinates
        Vector3 playerMapPos = playerReader.MapPosNoZ;
        float playerDirection = playerReader.Direction;

        var settings = provider.MinimapSettings;

        // Choose the proper yard-based zoom scale
        var diametersYards = addonBits.Indoors()
            ? IndoorZoomDiametersYards
            : OutdoorZoomDiametersYards;

        float yardsPerPixel = diametersYards[settings.Zoom] / settings.Width;

        Vector2 center = e.Rect.Centre();

        Vector2 node = new(e.X, e.Y);

        float dx = node.X - center.X;
        float dy = node.Y - center.Y; // screen space
        dy = -dy;                     // flip Y to world space

        Vector2 v = new(dx, dy);

        // North-up means +Y. When minimap rotates, rotate by player direction.
        float angle = settings.RotateMinimap ? playerDirection : 0f;

        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);

        Vector2 worldOffset = new(
            v.X * cos - v.Y * sin,
            v.X * sin + v.Y * cos);

        const float zoneDiameterYards = 10000f;
        float mapUnitsPerPixel = yardsPerPixel / zoneDiameterYards * 100f;
        worldOffset *= mapUnitsPerPixel;

        Vector3 pos = playerMapPos + new Vector3(worldOffset, 0);

        NodeFound?.Invoke(pos);
    }
}
