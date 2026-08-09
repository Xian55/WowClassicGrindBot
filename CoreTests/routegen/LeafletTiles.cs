using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace CoreTests;

/// <summary>
/// Places the pre-extracted minimap tiles behind a rendered route.
///
/// <para>Without a map behind it a route is an abstract squiggle - you cannot tell whether it
/// crosses a road, hugs a lake or runs through a building. These are the same tiles the
/// Leaflet page uses, read straight off disk.</para>
///
/// <para>The per-continent extents live only in <c>leaflet-watch.js</c>, so they are parsed
/// out of it rather than copied. <c>Frontend/CLAUDE.md</c> already warns that the JS mirrors
/// <c>DataConfig</c> and the two must be kept in step; a third copy in a test harness would
/// be one more thing to drift.</para>
/// </summary>
internal static partial class LeafletTiles
{
    // Mirrors the derivation in leaflet-watch.js: resX/resY are normalised away, so every
    // continent ends up on the same world extent and only the offset differs.
    private const double MapSize = 34133.33333333334;
    private const double AdtSize = MapSize / 64.0;
    private const double BlpSize = 512.0;
    private const double PxPerCoord = AdtSize / BlpSize;
    private const int TilePixels = 256;

    internal readonly record struct Config(int MaxZoom, int OffsetX, int OffsetY);

    internal static int TileSize => TilePixels;

    /// <summary>Max-zoom pixels per world yard, for scale bars.</summary>
    internal static double PixelsPerYard => 1.0 / PxPerCoord;

    /// <summary>World position to pixel coordinates at the continent's maximum zoom.</summary>
    internal static (double X, double Y) WorldToPixel(double worldX, double worldY, Config config)
    {
        double offsetX = (config.OffsetY * AdtSize) / PxPerCoord;
        double offsetY = (config.OffsetX * AdtSize) / PxPerCoord;

        double x = (((MapSize / 2) - worldY) / PxPerCoord) - offsetX;
        double y = (((MapSize / 2) - worldX) / PxPerCoord) - offsetY;

        return (x, y);
    }

    /// <summary>Scale factor from max-zoom pixels to the given zoom level.</summary>
    internal static double ZoomScale(int zoom, Config config) =>
        Math.Pow(2, zoom - config.MaxZoom);

    /// <summary>
    /// Reads the extent for one continent out of the Leaflet script. Returns null when the
    /// script, the era or the continent is absent - the caller then renders without a map
    /// rather than failing.
    /// </summary>
    internal static Config? ReadConfig(string repoRoot, string era, string continent)
    {
        string script = Path.Combine(repoRoot,
            "Frontend", "wwwroot", "script", "leaflet-watch.js");

        if (!File.Exists(script))
        {
            return null;
        }

        string text = File.ReadAllText(script);

        // The Configs object is JS, not JSON - unquoted keys, comments, trailing commas - so
        // it is scanned rather than parsed. Narrow to the era block first, because continent
        // names repeat across eras with different offsets.
        Match eraMatch = Regex.Match(text,
            "'" + Regex.Escape(era) + @"'\s*:\s*\{",
            RegexOptions.None, TimeSpan.FromSeconds(5));

        if (!eraMatch.Success)
        {
            return null;
        }

        string eraBlock = ExtractBlock(text, eraMatch.Index + eraMatch.Length - 1);

        Match contMatch = Regex.Match(eraBlock,
            "'" + Regex.Escape(continent) + @"'\s*:\s*\{",
            RegexOptions.None, TimeSpan.FromSeconds(5));

        if (!contMatch.Success)
        {
            return null;
        }

        string block = ExtractBlock(eraBlock, contMatch.Index + contMatch.Length - 1);

        Match maxZoom = MaxZoomRegex().Match(block);
        Match min = OffsetMinRegex().Match(block);

        if (!maxZoom.Success || !min.Success)
        {
            return null;
        }

        return new Config(
            int.Parse(maxZoom.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(min.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(min.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>Substring of one brace-balanced block starting at an opening brace.</summary>
    private static string ExtractBlock(string text, int openBrace)
    {
        int depth = 0;

        for (int i = openBrace; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                depth++;
            }
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return text[openBrace..(i + 1)];
                }
            }
        }

        return text[openBrace..];
    }

    /// <summary>Tile files covering a pixel rectangle at a zoom.</summary>
    internal static List<(int X, int Y, string Path)> Cover(string tileDir, int zoom,
        double minPx, double minPy, double maxPx, double maxPy)
    {
        List<(int, int, string)> result = [];

        if (!Directory.Exists(tileDir))
        {
            return result;
        }

        int x0 = (int)Math.Floor(minPx / TilePixels);
        int x1 = (int)Math.Floor(maxPx / TilePixels);
        int y0 = (int)Math.Floor(minPy / TilePixels);
        int y1 = (int)Math.Floor(maxPy / TilePixels);

        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                string path = Path.Combine(tileDir, $"z{zoom}x{x}y{y}.webp");
                if (File.Exists(path))
                {
                    result.Add((x, y, path));
                }
            }
        }

        return result;
    }

    [GeneratedRegex(@"maxZoom\s*:\s*(\d+)")]
    private static partial Regex MaxZoomRegex();

    [GeneratedRegex(@"min\s*:\s*\{\s*x\s*:\s*(-?\d+)\s*,\s*y\s*:\s*(-?\d+)")]
    private static partial Regex OffsetMinRegex();
}
