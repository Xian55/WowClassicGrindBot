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

        Vector2 pixelOffset = new(dx, dy);

        // North-up means +Y. When minimap rotates, rotate by player direction.
        float angle = settings.RotateMinimap ? playerDirection : 0f;

        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);

        // Rotate pixel offset to world-aligned offset
        Vector2 worldOffsetPixels = new(
            pixelOffset.X * cos - pixelOffset.Y * sin,
            pixelOffset.X * sin + pixelOffset.Y * cos);

        // Convert pixel offset to yards offset
        Vector2 worldOffsetYards = worldOffsetPixels * yardsPerPixel;

        // Get actual zone dimensions from WorldMapArea
        // LocTop/LocBottom define X bounds (Top > Bottom in WoW coords)
        // LocLeft/LocRight define Y bounds (Left > Right in WoW coords)
        WorldMapArea wma = playerReader.WorldMapArea;
        float zoneWidthYards = MathF.Abs(wma.LocTop - wma.LocBottom);
        float zoneHeightYards = MathF.Abs(wma.LocLeft - wma.LocRight);

        // Avoid division by zero for invalid/unloaded zones
        if (zoneWidthYards < 1f || zoneHeightYards < 1f)
        {
            logger.LogWarning(
                "Invalid zone dimensions: width={ZoneWidth}, height={ZoneHeight}, UIMapId={UIMapId}",
                zoneWidthYards, zoneHeightYards, playerReader.UIMapId.Value);
            return;
        }

        // Convert yards to map units (0-100 range per zone dimension)
        // Handle non-square zones by calculating X and Y separately
        float mapUnitsPerYardX = 100f / zoneWidthYards;
        float mapUnitsPerYardY = 100f / zoneHeightYards;

        Vector2 offsetMapUnits = new(
            worldOffsetYards.X * mapUnitsPerYardX,
            worldOffsetYards.Y * mapUnitsPerYardY);

        Vector3 pos = playerMapPos + new Vector3(offsetMapUnits, 0);

        // Clamp to valid map range and warn if out of bounds
        if (pos.X < 0 || pos.X > 100 || pos.Y < 0 || pos.Y > 100)
        {
            logger.LogDebug(
                "Node position out of bounds: ({PosX:F2}, {PosY:F2}), clamping to [0,100]",
                pos.X, pos.Y);
            pos = new Vector3(
                Math.Clamp(pos.X, 0f, 100f),
                Math.Clamp(pos.Y, 0f, 100f),
                pos.Z);
        }

        NodeFound?.Invoke(pos);
    }
}
