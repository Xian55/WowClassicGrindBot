using Core;

using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace CoreTests;

/// <summary>
/// The same picture <see cref="RouteGenSvg"/> draws, rasterised to PNG or WebP.
///
/// <para>An SVG with a hundred inlined tiles is several hundred KB of base64 and needs a
/// browser to look at. A raster of the same view is smaller, opens in any image viewer, and
/// can be dropped into an issue - which is what makes it worth carrying a second backend.</para>
///
/// <para>Geometry is not recomputed here: <see cref="RouteLayout"/> is shared with the SVG
/// path so both formats frame the route identically.</para>
/// </summary>
internal static class RouteGenRaster
{
    private static readonly Color Background = Color.ParseHex("0d1117");
    private static readonly Color SpawnColour = Color.ParseHex("3fb950").WithAlpha(0.7f);
    private static readonly Color RouteHalo = Color.ParseHex("0d1117").WithAlpha(0.7f);
    private static readonly Color RouteLine = Color.ParseHex("58a6ff");
    private static readonly Color StopFill = Color.ParseHex("f78166");
    private static readonly Color Ink = Color.ParseHex("e6edf3");

    public static string Render(RouteGenerator.Context context, Vector3[] route,
        IReadOnlyList<Vector3> stops, string title,
        string? tileDir, LeafletTiles.Config? tileConfig,
        string directory, string name, string format)
    {
        RouteLayout layout = RouteLayout.Build(context, title, tileDir, tileConfig);

        using Image<Rgba32> image = new(layout.Width, layout.Height, Background.ToPixel<Rgba32>());

        DrawTiles(image, layout);
        DrawSpawns(image, context, layout);
        DrawRoute(image, route, layout);
        DrawStops(image, stops, layout);
        DrawChrome(image, layout);

        string path = RouteLayout.OutputPath(directory, name, format);

        if (format == "webp")
        {
            image.Save(path, new WebpEncoder { Quality = 90 });
        }
        else
        {
            image.Save(path, new PngEncoder());
        }

        return path;
    }

    private static void DrawTiles(Image<Rgba32> image, RouteLayout layout)
    {
        foreach ((int tx, int ty, string tilePath) in layout.Tiles)
        {
            // A tile that fails to decode should cost that tile, not the whole render - the
            // extracted set legitimately has gaps at the edge of a continent.
            Image<Rgba32> tile;
            try
            {
                tile = Image.Load<Rgba32>(tilePath);
            }
            catch (ImageFormatException)
            {
                continue;
            }

            using (tile)
            {
                int px = (int)((tx * LeafletTiles.TileSize) + layout.TileOffsetX);
                int py = (int)((ty * LeafletTiles.TileSize) + layout.TileOffsetY);

                image.Mutate(c => c.DrawImage(tile, new Point(px, py), 1f));
            }
        }
    }

    private static void DrawSpawns(Image<Rgba32> image, RouteGenerator.Context context,
        RouteLayout layout)
    {
        image.Mutate(c =>
        {
            foreach (Vector3 spawn in context.Polygon.Spawns)
            {
                layout.Project(spawn, out double sx, out double sy);
                c.Fill(SpawnColour, new EllipsePolygon((float)sx, (float)sy, 3f));
            }
        });
    }

    private static void DrawRoute(Image<Rgba32> image, Vector3[] route, RouteLayout layout)
    {
        if (route.Length < 2)
        {
            return;
        }

        PointF[] points = new PointF[route.Length];
        for (int i = 0; i < route.Length; i++)
        {
            layout.Project(route[i], out double sx, out double sy);
            points[i] = new PointF((float)sx, (float)sy);
        }

        // Dark halo under the line so it stays readable over light terrain.
        image.Mutate(c => c
            .DrawLine(RouteHalo, 5f, points)
            .DrawLine(RouteLine, 2.5f, points));
    }

    private static void DrawStops(Image<Rgba32> image, IReadOnlyList<Vector3> stops,
        RouteLayout layout)
    {
        Font? font = Mono(12);

        image.Mutate(c =>
        {
            for (int i = 0; i < stops.Count; i++)
            {
                layout.Project(stops[i], out double sx, out double sy);

                EllipsePolygon dot = new((float)sx, (float)sy, 7f);
                c.Fill(StopFill, dot);
                c.Draw(Background, 2f, dot);

                if (font != null)
                {
                    c.DrawText(
                        i.ToString(CultureInfo.InvariantCulture),
                        font, Color.White,
                        new PointF((float)sx + 9f, (float)sy - 6f));
                }
            }
        });
    }

    private static void DrawChrome(Image<Rgba32> image, RouteLayout layout)
    {
        float barPixels = RouteLayout.BarYards * layout.YardsToPixels;
        float barX = RouteLayout.Margin;
        float barY = layout.Height - 12;

        Font? bar = Mono(12);
        Font? heading = Mono(14);

        image.Mutate(c =>
        {
            c.Fill(Background.WithAlpha(0.65f), new RectangleF(
                barX - 6, barY - 20, barPixels + 60, 26));

            c.DrawLine(Ink, 2f,
                new PointF(barX, barY), new PointF(barX + barPixels, barY));

            if (bar != null)
            {
                c.DrawText($"{RouteLayout.BarYards:F0} yd", bar, Ink,
                    new PointF(barX + barPixels + 6, barY - 8));
            }

            c.Fill(Background.WithAlpha(0.7f), new RectangleF(0, 0, layout.Width, 26));

            if (heading != null)
            {
                c.DrawText(layout.Title, heading, Ink, new PointF(RouteLayout.Margin, 5));
            }
        });
    }

    /// <summary>
    /// A monospace face, or null when the host has none of them. Text is decoration here -
    /// a machine without Consolas should still get the route, not an exception.
    /// </summary>
    private static Font? Mono(float size)
    {
        foreach (string name in (string[])["Consolas", "Courier New", "DejaVu Sans Mono", "Segoe UI"])
        {
            if (SystemFonts.TryGet(name, out FontFamily family))
            {
                return family.CreateFont(size, FontStyle.Regular);
            }
        }

        return SystemFonts.Families.GetEnumerator().MoveNext()
            ? SystemFonts.Families.GetEnumerator().Current.CreateFont(size, FontStyle.Regular)
            : null;
    }
}
