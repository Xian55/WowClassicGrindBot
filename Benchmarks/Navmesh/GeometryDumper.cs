using System;
using System.Globalization;
using System.IO;

using PPather.Navmesh;

using Serilog;

namespace Benchmarks.Navmesh;

/// <summary>
/// Writes the corpus tiles' triangle soup to disk so the native
/// recastnavigation harness can bake exactly the same input.
///
/// The point is an apples-to-apples comparison of the C++ pipeline against
/// DotRecast's: same geometry, same config, same stage order. Anything the
/// two sides could disagree about - bounds, cell size, agent dimensions -
/// travels in the file rather than being duplicated in two places.
/// </summary>
public static class GeometryDumper
{
    /// <summary>Bumped whenever the layout below changes.</summary>
    public const int FormatVersion = 1;

    private const string Magic = "RCDUMP\0\0";

    public static void Run(string outputDir, ILogger logger)
    {
        if (!Path.IsPathRooted(outputDir))
        {
            outputDir = Path.Combine(NavmeshCorpus.SolutionRoot(), outputDir);
        }

        Directory.CreateDirectory(outputDir);

        using GeometryFixture fixture = new();

        foreach (BakeTile tile in NavmeshCorpus.Tiles)
        {
            NavmeshCoords.GetTileIndex(tile.WorldX, tile.WorldY, out int tx, out int tz);
            TileGeometry geom = TileGeometryExtractor.Extract(fixture.World(tile.MapId), tx, tz);

            string path = Path.Combine(outputDir, tile.Name + ".rcdump");
            Write(path, tx, tz, geom);

            logger.Information(string.Create(CultureInfo.InvariantCulture,
                $"{tile.Name,-18} ground {geom.GroundTriangleCount,6} tris, liquid {geom.LiquidTriangleCount,5} tris -> {Path.GetFileName(path)}"));
        }

        logger.Information($"Dumps written to {outputDir}");
    }

    private static void Write(string path, int tileX, int tileZ, TileGeometry geom)
    {
        using FileStream fs = File.Create(path);
        using BinaryWriter w = new(fs);

        foreach (char c in Magic)
        {
            w.Write((byte)c);
        }

        w.Write(FormatVersion);

        // Tile identity - the native side needs it for DtNavMeshCreateParams.
        w.Write(tileX);
        w.Write(tileZ);

        // Config, so the harness cannot drift from the bake defaults.
        SharedLib.NavmeshBakeOptions bake = new();
        w.Write(NavmeshSettings.CellSize);
        w.Write(NavmeshSettings.CellHeight);
        w.Write(bake.WalkableSlope);
        w.Write(NavmeshSettings.AgentHeight);
        w.Write(bake.AgentRadius);
        w.Write(bake.AgentMaxClimb);
        w.Write(NavmeshSettings.MinRegionAreaWorld);
        w.Write(NavmeshSettings.MergeRegionAreaWorld);
        w.Write(NavmeshSettings.MaxEdgeLenWorld);
        w.Write(NavmeshSettings.MaxSimplificationError);
        w.Write(NavmeshSettings.VertsPerPoly);
        w.Write(NavmeshSettings.DetailSampleDistFactor);
        w.Write(NavmeshSettings.DetailSampleMaxErrorFactor);
        w.Write(NavmeshSettings.TileWorldSize);
        w.Write(NavmeshSettings.AreaGround);
        w.Write(NavmeshSettings.AreaWater);

        WriteVec(w, geom.BMin);
        WriteVec(w, geom.BMax);

        WriteFloats(w, geom.GroundVerts);
        WriteInts(w, geom.GroundTris);

        WriteFloats(w, geom.LiquidVerts);
        WriteInts(w, geom.LiquidTris);
        WriteInts(w, geom.LiquidAreas);
    }

    private static void WriteVec(BinaryWriter w, System.Numerics.Vector3 v)
    {
        w.Write(v.X);
        w.Write(v.Y);
        w.Write(v.Z);
    }

    private static void WriteFloats(BinaryWriter w, float[] data)
    {
        w.Write(data.Length);
        for (int i = 0; i < data.Length; i++)
        {
            w.Write(data[i]);
        }
    }

    private static void WriteInts(BinaryWriter w, int[] data)
    {
        w.Write(data.Length);
        for (int i = 0; i < data.Length; i++)
        {
            w.Write(data[i]);
        }
    }
}
