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
        [472f, 366f, 284f, 220f, 171f, 132f];

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

        // worldOffsetYards.X = -world.Y direction (east on screen)
        // worldOffsetYards.Y = +world.X direction (north on screen)
        // Map.X corresponds to world.Y, Map.Y corresponds to world.X
        Vector2 offsetMapUnits = new(
            worldOffsetYards.X * mapUnitsPerYardY,     // Map X from -world Y offset
            -worldOffsetYards.Y * mapUnitsPerYardX);  // Map Y from +world X offset (negated for map direction)

        Vector3 pos = playerMapPos + new Vector3(offsetMapUnits, 0);

        // Clamp to valid map range and warn if out of bounds
        if (pos.X < 0 || pos.X > 100 || pos.Y < 0 || pos.Y > 100)
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace(
                    "Node position out of bounds: ({PosX:F2}, {PosY:F2}), clamping to [0,100]",
                    pos.X, pos.Y);
            }
            pos = new Vector3(
                Math.Clamp(pos.X, 0f, 100f),
                Math.Clamp(pos.Y, 0f, 100f),
                pos.Z);
        }

        // Diagnostic logging for calibrating minimap conversion (calibration complete)
        //float worldX = wma.ToWorldX(pos.Y);
        //float worldY = wma.ToWorldY(pos.X);
        //float playerWorldX = wma.ToWorldX(playerMapPos.Y);
        //float playerWorldY = wma.ToWorldY(playerMapPos.X);
        //logger.LogInformation(
        //    "Minimap: zoom={Zoom} diameter={Diameter:F1}y pxOff=({PxX:F1},{PxY:F1}) " +
        //    "yardOff=({YdX:F2},{YdY:F2}) zone=({ZW:F0}x{ZH:F0}) " +
        //    "player=({PWX:F1},{PWY:F1}) predicted=({NWX:F1},{NWY:F1})",
        //    settings.Zoom,
        //    diametersYards[settings.Zoom],
        //    dx, dy,
        //    worldOffsetYards.X, worldOffsetYards.Y,
        //    zoneWidthYards, zoneHeightYards,
        //    playerWorldX, playerWorldY,
        //    worldX, worldY);

        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("FoundNodeListener: node map pos=({X:F2},{Y:F2})",
                pos.X, pos.Y);
        }

        NodeFound?.Invoke(pos);
    }
}
