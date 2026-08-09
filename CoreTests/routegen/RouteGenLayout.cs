using Core;

using System;
using System.Collections.Generic;
using System.Numerics;

namespace CoreTests;

internal delegate void Projector(Vector3 p, out double sx, out double sy);

/// <summary>
/// Where everything lands on the canvas: image size, the world-to-pixel projection, the scale
/// bar's ratio, and which minimap tiles sit behind it.
///
/// <para>Shared by the SVG and raster backends on purpose. The zoom search and the pixel
/// flip are the fiddly part of this renderer, and a second copy in the raster path would
/// drift from the first - two pictures of the same route that disagree is worse than
/// having only one format.</para>
/// </summary>
internal sealed class RouteLayout
{
    public const int Margin = 24;

    /// <summary>
    /// Tile budget. In SVG each tile is inlined as a data URI, so this bounds both the file
    /// size and the zoom that gets chosen. The raster backend keeps the same bound so the
    /// two formats show the same level of detail.
    /// </summary>
    private const int MaxTiles = 120;

    private const int PlainWidth = 1400;

    /// <summary>Length of the scale bar drawn bottom-left, in yards.</summary>
    public const float BarYards = 100f;

    /// <summary>
    /// Full path for an output file, creating the directory and scrubbing characters the
    /// filesystem rejects - zone names carry apostrophes and colons.
    /// </summary>
    public static string OutputPath(string directory, string name, string extension)
    {
        System.IO.Directory.CreateDirectory(directory);

        string safe = name;
        foreach (char c in System.IO.Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(c, '_');
        }

        return System.IO.Path.GetFullPath(
            System.IO.Path.Combine(directory, $"{safe}.{extension}"));
    }

    public required int Width { get; init; }
    public required int Height { get; init; }
    public required Projector Project { get; init; }
    public required float YardsToPixels { get; init; }
    public required string Title { get; init; }

    /// <summary>Tiles to paint behind the route. Empty when no map was available.</summary>
    public required IReadOnlyList<(int X, int Y, string Path)> Tiles { get; init; }

    /// <summary>
    /// Pixel offset applied to a tile's grid position, in the same space as
    /// <see cref="Project"/>: a tile goes at <c>tile.X * TileSize + TileOffsetX</c>.
    /// </summary>
    public required double TileOffsetX { get; init; }
    public required double TileOffsetY { get; init; }

    public static RouteLayout Build(RouteGenerator.Context context, string title,
        string? tileDir, LeafletTiles.Config? tileConfig)
    {
        return tileDir != null && tileConfig.HasValue
            ? WithMap(context, title, tileDir, tileConfig.Value)
            : Plain(context, title + "  [no map tiles]");
    }

    private static RouteLayout WithMap(RouteGenerator.Context context, string title,
        string tileDir, LeafletTiles.Config cfg)
    {
        // Pixel space at max zoom. World X maps to pixel y and world Y to pixel x, which is
        // the same flip the map view applies - so the picture is oriented like the in-game
        // map instead of transposed.
        (double ax, double ay) = LeafletTiles.WorldToPixel(
            context.Polygon.MinWorldX, context.Polygon.MinWorldY, cfg);
        (double bx, double by) = LeafletTiles.WorldToPixel(
            context.Polygon.MaxWorldX, context.Polygon.MaxWorldY, cfg);

        double minPxFull = Math.Min(ax, bx);
        double maxPxFull = Math.Max(ax, bx);
        double minPyFull = Math.Min(ay, by);
        double maxPyFull = Math.Max(ay, by);

        // Highest zoom whose tile count fits the budget - the most detail affordable.
        int zoom = cfg.MaxZoom;
        double scale;

        while (true)
        {
            scale = LeafletTiles.ZoomScale(zoom, cfg);

            int wide = (int)Math.Floor(maxPxFull * scale / LeafletTiles.TileSize)
                - (int)Math.Floor(minPxFull * scale / LeafletTiles.TileSize) + 1;
            int high = (int)Math.Floor(maxPyFull * scale / LeafletTiles.TileSize)
                - (int)Math.Floor(minPyFull * scale / LeafletTiles.TileSize) + 1;

            if (zoom <= 1 || wide * high <= MaxTiles)
            {
                break;
            }

            zoom--;
        }

        double minPx = minPxFull * scale;
        double maxPx = maxPxFull * scale;
        double minPy = minPyFull * scale;
        double maxPy = maxPyFull * scale;

        void Point(Vector3 p, out double sx, out double sy)
        {
            (double x, double y) = LeafletTiles.WorldToPixel(p.X, p.Y, cfg);
            sx = (x * scale) - minPx + Margin;
            sy = (y * scale) - minPy + Margin;
        }

        return new RouteLayout
        {
            Width = (int)(maxPx - minPx) + (2 * Margin),
            Height = (int)(maxPy - minPy) + (2 * Margin),
            Project = Point,
            YardsToPixels = (float)(LeafletTiles.PixelsPerYard * scale),
            Title = title,
            Tiles = LeafletTiles.Cover(tileDir, zoom, minPx, minPy, maxPx, maxPy),
            TileOffsetX = Margin - minPx,
            TileOffsetY = Margin - minPy
        };
    }

    private static RouteLayout Plain(RouteGenerator.Context context, string title)
    {
        float minWorldX = context.Polygon.MinWorldX;
        float minWorldY = context.Polygon.MinWorldY;

        float spanX = MathF.Max(1f, context.Polygon.MaxWorldX - minWorldX);
        float spanY = MathF.Max(1f, context.Polygon.MaxWorldY - minWorldY);

        float scale = (PlainWidth - (2 * Margin)) / spanY;

        void Point(Vector3 p, out double sx, out double sy)
        {
            sx = Margin + ((p.Y - minWorldY) * scale);
            sy = Margin + ((p.X - minWorldX) * scale);
        }

        return new RouteLayout
        {
            Width = PlainWidth,
            Height = (int)((spanX * scale) + (2 * Margin)),
            Project = Point,
            YardsToPixels = scale,
            Title = title,
            Tiles = [],
            TileOffsetX = 0,
            TileOffsetY = 0
        };
    }
}
