using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;

using DotRecast.Core;
using DotRecast.Detour;
using DotRecast.Detour.Io;

using Microsoft.Extensions.Logging.Abstractions;

using PPather.Navmesh;

using Serilog;

using WowTriangles;

namespace Benchmarks.Navmesh;

/// <summary>
/// Stage-resolved profile of the bake pipeline over a fixed tile corpus.
///
/// BenchmarkDotNet measures whole methods; this measures *inside* one bake:
/// the MPQ/extract split from our own stopwatches, and the recast split from the
/// per-stage timers DotRecast already records into <see cref="RcContext"/>
/// (RC_TIMER_* labels), plus GC pressure per tile.
///
/// Usage: dotnet run --project Benchmarks -c Release -- --bake-profile [label] [iterations]
/// </summary>
public static class BakeProfiler
{
    private sealed class TileProfile
    {
        public string Name { get; init; } = string.Empty;
        public bool Success { get; set; }
        public string? Error { get; set; }

        public double ColdExtractMs { get; set; }
        public double ColdBakeMs { get; set; }
        public double WarmExtractMs { get; set; }
        public double WarmBakeMs { get; set; }

        public int GroundTriangles { get; set; }
        public int LiquidTriangles { get; set; }
        public int PolyCount { get; set; }
        public int VertCount { get; set; }

        /// <summary>SHA-256 of the serialized tile - the byte-identity gate for
        /// optimizations that must not change mesh output.</summary>
        public string TileHash { get; set; } = "-";

        public long AllocatedBytes { get; set; }
        public int Gen0 { get; set; }
        public int Gen1 { get; set; }
        public int Gen2 { get; set; }

        /// <summary>RC_TIMER_* label -> milliseconds, from the bake context.</summary>
        public Dictionary<string, double> Stages { get; } = [];
    }

    public static void Run(string label, int iterations, ILogger logger,
        string outputDir = "local/benchmark_results")
    {
        logger.Information("=== Navmesh Bake Profile ===\n");
        logger.Information(string.Format("Label: {0}", label));
        logger.Information(string.Format("Warm iterations per tile: {0}", iterations));
        logger.Information("");

        using GeometryFixture fixture = new();
        List<TileProfile> profiles = [];

        foreach (BakeTile tile in NavmeshCorpus.Tiles)
        {
            TileProfile p = new() { Name = tile.Name };

            try
            {
                ChunkedTriangleCollection world = fixture.World(tile.MapId);
                NavmeshCoords.GetTileIndex(tile.WorldX, tile.WorldY, out int tx, out int tz);

                // --- Cold: ADT not resident, so extract pays MPQ read + parse.
                fixture.Evict(tile.MapId);

                long t0 = Stopwatch.GetTimestamp();
                TileGeometry coldGeom = TileGeometryExtractor.Extract(world, tx, tz);
                p.ColdExtractMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;

                t0 = Stopwatch.GetTimestamp();
                DtMeshData? coldData = NavmeshTileBuilder.Bake(coldGeom, tx, tz);
                p.ColdBakeMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;

                p.GroundTriangles = coldGeom.GroundTriangleCount;
                p.LiquidTriangles = coldGeom.LiquidTriangleCount;
                p.PolyCount = coldData?.header.polyCount ?? 0;
                p.VertCount = coldData?.header.vertCount ?? 0;
                p.TileHash = HashTile(coldData);

                // --- Warm: ADT resident. Averages the requested iterations and
                // captures stage timings + GC pressure from a dedicated pass.
                double extractSum = 0;
                double bakeSum = 0;

                for (int i = 0; i < iterations; i++)
                {
                    t0 = Stopwatch.GetTimestamp();
                    TileGeometry geom = TileGeometryExtractor.Extract(world, tx, tz);
                    extractSum += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;

                    t0 = Stopwatch.GetTimestamp();
                    NavmeshTileBuilder.Bake(geom, tx, tz);
                    bakeSum += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                }

                p.WarmExtractMs = extractSum / iterations;
                p.WarmBakeMs = bakeSum / iterations;

                // --- Instrumented passes: allocation counters, then stage timers.
                //
                // These deliberately do NOT share a pass. Allocation is
                // deterministic, so a single bake measures it exactly. Stage timers
                // are not: at n=1 the smaller stages are dominated by JIT tier state,
                // which makes them depend on how many warm iterations happened to run
                // before this point rather than on the code being measured. Measured
                // across eight identical runs, that put BUILD_POLYMESH anywhere in
                // 11-32ms and RASTERIZE_TRIANGLES in 62-111ms - wide enough to invent
                // a regression that does not exist, which it did.
                TileGeometry profGeom = TileGeometryExtractor.Extract(world, tx, tz);

                int gen0 = GC.CollectionCount(0);
                int gen1 = GC.CollectionCount(1);
                int gen2 = GC.CollectionCount(2);
                long allocated = GC.GetAllocatedBytesForCurrentThread();

                RcContext allocCtx = new();
                NavmeshTileBuilder.Bake(profGeom, tx, tz, allocCtx);

                p.AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                p.Gen0 = GC.CollectionCount(0) - gen0;
                p.Gen1 = GC.CollectionCount(1) - gen1;
                p.Gen2 = GC.CollectionCount(2) - gen2;

                // RcContext accumulates into its timer dictionary across calls, so
                // one context over N bakes yields a sum to divide.
                int stageIterations = Math.Max(1, iterations);
                RcContext ctx = new();
                for (int i = 0; i < stageIterations; i++)
                {
                    NavmeshTileBuilder.Bake(profGeom, tx, tz, ctx);
                }

                foreach (RcTelemetryTick tick in ctx.ToList())
                {
                    p.Stages[tick.Key] =
                        tick.Ticks / (double)TimeSpan.TicksPerMillisecond / stageIterations;
                }

                p.Success = true;

                logger.Information(string.Format(
                    "{0,-20} cold {1,8:F1}ms (extract {2,7:F1} + bake {3,7:F1}) | warm bake {4,7:F1}ms | {5,5} polys | {6,6:F1} MB alloc | gen0 {7}",
                    p.Name, p.ColdExtractMs + p.ColdBakeMs, p.ColdExtractMs, p.ColdBakeMs,
                    p.WarmBakeMs, p.PolyCount, p.AllocatedBytes / (1024.0 * 1024.0), p.Gen0));
            }
            catch (Exception ex)
            {
                p.Success = false;
                p.Error = ex.Message;
                logger.Information(string.Format("{0,-20} FAILED: {1}", p.Name, ex.Message));
            }

            profiles.Add(p);
        }

        string path = WriteReport(outputDir, label, iterations, profiles);
        logger.Information(string.Format("\nReport written: {0}", path));
    }

    /// <summary>
    /// Wall-clock to bake a virgin corridor through the real NavmeshTileCache -
    /// worker pool, extract lock and all. This is the number the single-tile
    /// profile cannot show: how much the pipeline actually parallelizes.
    /// </summary>
    public static void RunThroughput(string label, ILogger logger, string outputDir = "local/benchmark_results")
    {
        logger.Information("=== Navmesh Bake Throughput ===\n");
        logger.Information(string.Format("Label: {0}", label));

        using GeometryFixture fixture = new();

        // Long virgin corridor: Barrens, ~2000yd, crosses several ADTs.
        Vector3 from = new(-896f, -3770f, 11f);
        Vector3 to = new(-441f, -2596f, 96f);
        const float MapId = 1;

        string cacheDir = Path.Combine(Path.GetTempPath(), "navmesh_throughput_" + Guid.NewGuid().ToString("N"));

        try
        {
            using NavmeshPathfinder pathfinder = new(NullLogger.Instance, fixture.World(MapId), cacheDir,
                new SharedLib.NavmeshBakeOptions(), new SharedLib.NavmeshQueryOptions());

            long start = Stopwatch.GetTimestamp();
            pathfinder.Tiles.EnsureTilesForSegment(from, to, TimeSpan.FromMinutes(10));
            double elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

            int baked = pathfinder.Tiles.TilesBakedThisSession;
            int workers = NavmeshTileCache.BakeWorkerCount;

            logger.Information(string.Format(
                "Corridor baked: {0} tiles in {1:F0}ms ({2:F0}ms per tile, {3} workers, {4} cores)",
                baked, elapsedMs, baked > 0 ? elapsedMs / baked : 0, workers, Environment.ProcessorCount));

            double wait = pathfinder.Tiles.ExtractWait.TotalMilliseconds;
            double hold = pathfinder.Tiles.ExtractHold.TotalMilliseconds;
            logger.Information(string.Format(
                "extractLock: held {0:F0}ms ({1:P0} of wall), waited {2:F0}ms across {3} workers",
                hold, hold / elapsedMs, wait, workers));

            Directory.CreateDirectory(Path.IsPathRooted(outputDir)
                ? outputDir
                : outputDir = Path.Combine(NavmeshCorpus.SolutionRoot(), outputDir));

            string path = Path.Combine(outputDir, string.Create(CultureInfo.InvariantCulture,
                $"throughput_{Sanitize(label)}_{DateTime.Now:yyyyMMdd_HHmmss}.md"));

            StringBuilder sb = new();
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"# Bake throughput - {label}"));
            sb.AppendLine();
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {Environment.ProcessorCount} cores | {workers} bake workers"));
            sb.AppendLine();
            sb.AppendLine("| Corridor | Tiles baked | Wall clock | Per tile |");
            sb.AppendLine("|---|---|---|---|");
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| Barrens ~2000yd (virgin) | {baked} | {elapsedMs:F0}ms | {(baked > 0 ? elapsedMs / baked : 0):F0}ms |"));

            File.WriteAllText(path, sb.ToString());
            logger.Information(string.Format("\nReport written: {0}", path));
        }
        finally
        {
            try
            {
                if (Directory.Exists(cacheDir))
                {
                    Directory.Delete(cacheDir, recursive: true);
                }
            }
            catch (IOException)
            {
                // best effort - temp dir
            }
        }
    }

    /// <summary>
    /// Serializes the tile exactly as the disk cache would and hashes the bytes,
    /// so a run can be compared against another for output identity.
    /// </summary>
    private static string HashTile(DtMeshData? data)
    {
        if (data == null)
        {
            return "-";
        }

        using MemoryStream ms = new();
        using (BinaryWriter bw = new(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            new DtMeshDataWriter().Write(bw, data, RcByteOrder.LITTLE_ENDIAN, false);
        }

        byte[] hash = System.Security.Cryptography.SHA256.HashData(ms.ToArray());
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    private static string WriteReport(string outputDir, string label, int iterations,
        List<TileProfile> profiles)
    {
        // The fixture moves the working directory to HeadlessServer so DataConfig
        // resolves; reports still belong next to the other benchmark artifacts.
        if (!Path.IsPathRooted(outputDir))
        {
            outputDir = Path.Combine(NavmeshCorpus.SolutionRoot(), outputDir);
        }

        Directory.CreateDirectory(outputDir);

        string fileName = string.Create(CultureInfo.InvariantCulture,
            $"bake_{Sanitize(label)}_{DateTime.Now:yyyyMMdd_HHmmss}.md");
        string path = Path.Combine(outputDir, fileName);

        StringBuilder sb = new();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"# Navmesh Bake Profile - {label}"));
        sb.AppendLine();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {Environment.ProcessorCount} cores | warm iterations: {iterations}"));
        sb.AppendLine();
        sb.AppendLine("Cold = ADT evicted first, so extract pays MPQ read + parse. Warm = ADT resident.");
        sb.AppendLine("Allocation and GC counts cover the recast bake only, measured on a dedicated pass.");
        sb.AppendLine();

        sb.AppendLine("## Per-tile totals");
        sb.AppendLine();
        sb.AppendLine("| Tile | Cold extract | Cold bake | Warm extract | Warm bake | Ground tris | Liquid tris | Polys | Alloc MB | gen0 | gen1 | gen2 | Tile hash |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");

        foreach (TileProfile p in profiles)
        {
            if (!p.Success)
            {
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"| {p.Name} | FAIL | - | - | - | - | - | - | - | - | - | {p.Error} |"));
                continue;
            }

            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {p.Name} | {p.ColdExtractMs:F1}ms | {p.ColdBakeMs:F1}ms | {p.WarmExtractMs:F2}ms | {p.WarmBakeMs:F1}ms | {p.GroundTriangles} | {p.LiquidTriangles} | {p.PolyCount} | {p.AllocatedBytes / (1024.0 * 1024.0):F1} | {p.Gen0} | {p.Gen1} | {p.Gen2} | {p.TileHash} |"));
        }

        sb.AppendLine();
        sb.AppendLine("## Recast stage breakdown (ms)");
        sb.AppendLine();
        sb.AppendLine("From DotRecast's built-in `RC_TIMER_*` counters. Nested labels overlap their parent;");
        sb.AppendLine("compare each against the tile's warm bake total rather than summing the column.");
        sb.AppendLine();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Averaged over {Math.Max(1, iterations)} bake(s) per tile. At one sample these numbers are"));
        sb.AppendLine("dominated by JIT tier state, not by the code - do not compare single-sample runs.");
        sb.AppendLine();

        List<string> stageKeys = [.. profiles
            .Where(p => p.Success)
            .SelectMany(p => p.Stages.Keys)
            .Distinct()
            .OrderBy(k => k, StringComparer.Ordinal)];

        List<TileProfile> ok = [.. profiles.Where(p => p.Success)];

        if (stageKeys.Count > 0 && ok.Count > 0)
        {
            sb.Append("| Stage |");
            foreach (TileProfile p in ok)
            {
                sb.Append(string.Create(CultureInfo.InvariantCulture, $" {p.Name} |"));
            }
            sb.AppendLine(" share of slowest |");

            sb.Append("|---|");
            foreach (TileProfile _ in ok)
            {
                sb.Append("---|");
            }
            sb.AppendLine("---|");

            double slowestBake = ok.Max(p => p.WarmBakeMs);

            foreach (string key in stageKeys)
            {
                sb.Append(string.Create(CultureInfo.InvariantCulture, $"| {key} |"));

                double maxForStage = 0;
                foreach (TileProfile p in ok)
                {
                    double ms = p.Stages.TryGetValue(key, out double v) ? v : 0;
                    maxForStage = Math.Max(maxForStage, ms);
                    sb.Append(string.Create(CultureInfo.InvariantCulture, $" {ms:F1} |"));
                }

                double share = slowestBake > 0 ? (maxForStage / slowestBake) * 100.0 : 0;
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $" {share:F0}% |"));
            }
        }

        File.WriteAllText(path, sb.ToString());
        return path;
    }

    private static string Sanitize(string label)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            label = label.Replace(c, '-');
        }

        return label.Replace(' ', '-').ToLowerInvariant();
    }
}
