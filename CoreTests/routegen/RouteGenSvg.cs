using Core;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;

namespace CoreTests;

/// <summary>
/// Renders a generated route to a standalone SVG over the game's own minimap tiles - spawns,
/// the chosen stops and the walked path.
///
/// <para>The Blazor page draws on the Leaflet map, which needs the bot running. This is the
/// same picture from the offline harness, so a route can be judged while iterating on filters
/// without launching anything - and with the map behind it, "does this cross the river" is
/// answerable.</para>
///
/// <para>Layout comes from <see cref="RouteLayout"/>, shared with
/// <see cref="RouteGenRaster"/>.</para>
/// </summary>
internal static class RouteGenSvg
{
    public static string Render(RouteGenerator.Context context, Vector3[] route,
        IReadOnlyList<Vector3> stops, string title,
        string? tileDir, LeafletTiles.Config? tileConfig)
    {
        RouteLayout layout = RouteLayout.Build(context, title, tileDir, tileConfig);

        StringBuilder sb = new(1 << 20);

        sb.Append("<svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink' ")
          .Append(CultureInfo.InvariantCulture,
            $"width='{layout.Width}' height='{layout.Height}' viewBox='0 0 {layout.Width} {layout.Height}'>")
          .Append("<rect width='100%' height='100%' fill='#0d1117'/>");

        if (layout.Tiles.Count > 0)
        {
            sb.Append("<g>");
            foreach ((int tx, int ty, string path) in layout.Tiles)
            {
                double px = (tx * LeafletTiles.TileSize) + layout.TileOffsetX;
                double py = (ty * LeafletTiles.TileSize) + layout.TileOffsetY;

                sb.Append(CultureInfo.InvariantCulture,
                    $"<image x='{px:F1}' y='{py:F1}' width='{LeafletTiles.TileSize}' height='{LeafletTiles.TileSize}' ")
                  .Append("href='data:image/webp;base64,")
                  .Append(Convert.ToBase64String(File.ReadAllBytes(path)))
                  .Append("'/>");
            }
            sb.Append("</g>");
        }

        Body(sb, context, route, stops, layout);

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static void Body(StringBuilder sb, RouteGenerator.Context context, Vector3[] route,
        IReadOnlyList<Vector3> stops, RouteLayout layout)
    {
        Projector project = layout.Project;

        // Spawns first, so the route draws over them.
        sb.Append("<g fill='#3fb950' fill-opacity='0.7'>");
        foreach (Vector3 spawn in context.Polygon.Spawns)
        {
            project(spawn, out double sx, out double sy);
            sb.Append(CultureInfo.InvariantCulture, $"<circle cx='{sx:F1}' cy='{sy:F1}' r='3'/>");
        }
        sb.Append("</g>");

        if (route.Length > 1)
        {
            // Dark halo under the line so it stays readable over light terrain.
            sb.Append("<polyline fill='none' stroke='#0d1117' stroke-opacity='0.7' stroke-width='5' points='");
            AppendPoints(sb, route, project);
            sb.Append("'/>");

            sb.Append("<polyline fill='none' stroke='#58a6ff' stroke-width='2.5' points='");
            AppendPoints(sb, route, project);
            sb.Append("'/>");
        }

        for (int i = 0; i < stops.Count; i++)
        {
            project(stops[i], out double sx, out double sy);

            sb.Append(CultureInfo.InvariantCulture,
                $"<circle cx='{sx:F1}' cy='{sy:F1}' r='7' fill='#f78166' stroke='#0d1117' stroke-width='2'/>");
            sb.Append(CultureInfo.InvariantCulture,
                $"<text x='{sx + 9:F1}' y='{sy + 4:F1}' fill='#ffffff' stroke='#0d1117' stroke-width='3' ")
              .Append(CultureInfo.InvariantCulture,
                $"paint-order='stroke' font-family='monospace' font-size='12'>{i}</text>");
        }

        double barPixels = RouteLayout.BarYards * layout.YardsToPixels;
        double barX = RouteLayout.Margin;
        double barY = layout.Height - 12;

        sb.Append(CultureInfo.InvariantCulture,
            $"<rect x='{barX - 6:F1}' y='{barY - 20:F1}' width='{barPixels + 60:F1}' height='26' fill='#0d1117' fill-opacity='0.65'/>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<line x1='{barX:F1}' y1='{barY:F1}' x2='{barX + barPixels:F1}' y2='{barY:F1}' stroke='#e6edf3' stroke-width='2'/>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<text x='{barX + barPixels + 6:F1}' y='{barY + 4:F1}' fill='#e6edf3' font-family='monospace' font-size='12'>{RouteLayout.BarYards:F0} yd</text>");

        sb.Append(CultureInfo.InvariantCulture,
            $"<rect x='0' y='0' width='{layout.Width}' height='26' fill='#0d1117' fill-opacity='0.7'/>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<text x='{RouteLayout.Margin}' y='18' fill='#e6edf3' font-family='monospace' font-size='14'>")
          .Append(Escape(layout.Title)).Append("</text>");
    }

    private static void AppendPoints(StringBuilder sb, Vector3[] route, Projector project)
    {
        foreach (Vector3 p in route)
        {
            project(p, out double sx, out double sy);
            sb.Append(CultureInfo.InvariantCulture, $"{sx:F1},{sy:F1} ");
        }
    }

    public static string Write(string directory, string name, string svg)
    {
        string path = RouteLayout.OutputPath(directory, name, "svg");
        File.WriteAllText(path, svg);

        return path;
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
