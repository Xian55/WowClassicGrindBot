using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using PPather.Navmesh;

using Serilog;

namespace Benchmarks.Navmesh;

/// <summary>
/// Attributes bake allocations to pipeline stages.
///
/// A heap snapshot cannot answer this: the short-lived objects that drive gen0
/// churn are already collected by the time the dump's forced GC runs. This
/// instead reads GC.GetTotalAllocatedBytes around each stage, which counts
/// allocate-and-die traffic on every thread - including the parallel
/// rasterization bands and detail-mesh workers.
/// </summary>
public static class AllocProfiler
{
    public static void Run(ILogger logger)
    {
        using GeometryFixture fixture = new();

        Dictionary<string, long> total = [];
        long grand = 0;

        foreach (BakeTile tile in NavmeshCorpus.Tiles)
        {
            NavmeshCoords.GetTileIndex(tile.WorldX, tile.WorldY, out int tx, out int tz);
            TileGeometry geom = TileGeometryExtractor.Extract(fixture.World(tile.MapId), tx, tz);

            // Warm up so JIT and first-touch costs do not land on a stage.
            NavmeshTileBuilder.Bake(geom, tx, tz);

            Dictionary<string, long> perTile = [];
            NavmeshTileBuilder.StageAllocProbe = (stage, bytes) =>
            {
                perTile[stage] = perTile.TryGetValue(stage, out long v) ? v + bytes : bytes;
            };

            try
            {
                NavmeshTileBuilder.Bake(geom, tx, tz);
            }
            finally
            {
                NavmeshTileBuilder.StageAllocProbe = null;
            }

            long tileTotal = perTile.Values.Sum();
            grand += tileTotal;

            foreach ((string stage, long bytes) in perTile)
            {
                total[stage] = total.TryGetValue(stage, out long v) ? v + bytes : bytes;
            }

            logger.Information(string.Create(CultureInfo.InvariantCulture,
                $"{tile.Name,-18} {tileTotal / (1024.0 * 1024.0),7:F1} MB"));
        }

        logger.Information("");
        logger.Information(string.Create(CultureInfo.InvariantCulture,
            $"{"stage",-22} {"corpus MB",10} {"share",7}"));

        foreach ((string stage, long bytes) in total.OrderByDescending(kv => kv.Value))
        {
            logger.Information(string.Create(CultureInfo.InvariantCulture,
                $"{stage,-22} {bytes / (1024.0 * 1024.0),10:F1} {100.0 * bytes / grand,6:F1}%"));
        }

        logger.Information(string.Create(CultureInfo.InvariantCulture,
            $"{"TOTAL",-22} {grand / (1024.0 * 1024.0),10:F1}"));
    }
}
