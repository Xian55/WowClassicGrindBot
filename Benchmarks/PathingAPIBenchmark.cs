using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Serilog;

namespace Benchmarks;

/// <summary>
/// End-to-end benchmark suite for PathingAPI endpoints.
/// Tests real pathfinding operations and measures:
/// - Cold (first query after server Reset, includes chunk loading) vs warm elapsed time
/// - Path quality: total length, corner count, sharpest corner, endpoint reached
/// - Consistency across multiple runs
///
/// Usage: dotnet run --project Benchmarks -- --pather-benchmark [BaseUrl] [Iterations] [Label]
/// Example: dotnet run --project Benchmarks -- --pather-benchmark http://localhost:5001 4 spot-astar
/// </summary>
public class PathingAPIBenchmark
{
    // Cold measurement = iteration 0 right after a server Reset.
    // Reset clears the explored graph so the first query pays MPQ chunk loading.
    private const string ResetEndpoint = "api/PPather/Reset";

    // Matches PathGraph.MaximumAllowedRangeFromTarget - a path whose last point
    // is farther than this from the requested target did not actually arrive
    // (e.g. ProgressTimeout returned the closest reached spot).
    private const float ReachedDistance = 5f;

    // Heading change (degrees, XY plane) above which a waypoint counts as a corner.
    private const float CornerAngleDeg = 25f;

    private static readonly List<PathTest> TestRoutes =
    [
        // Elwynn Forest routes
        new("Elwynn vendor to path", "api/PPather/WorldRoute?x1=-8898.32&y1=-117.35608&z1=81.840546&x2=-8779.58&y2=-106.12012&z2=0&mapid=0"),
        new("Z Elwynn vendor to path", "api/PPather/WorldRoute2?x1=-8898.32&y1=-117.35608&z1=0&x2=-8779.58&y2=-106.12012&z2=0&uimap=1429&startindoors=false"),
        new("Z Coldridge to 5_gnome", "api/PPather/WorldRoute?x1=-6120.8084&y1=542.90857&z1=0&x2=-5880.7373&y2=-116.15503&z2=0&mapid=0"),

        // Redridge Mountains
        new("Redridge Grave to North", "api/PPather/MapRoute?uimap1=1433&x1=30&y1=60&uimap2=1433&x2=54.3&y2=43.1"),

        // Stormwind - the walkable route loops around the canals, so it bulges
        // far outside the straight from->to corridor. Truncated 254yd short
        // until the pathfinder learned to widen the loaded band on a stall.
        new("Stormwind city to trainer", "api/PPather/WorldRoute?x1=-8915.693&y1=-132.94092&z1=102.475426&x2=-8688.56&y2=325.76&z2=109.52&mapid=0"),

        // Alterac Mountains
        new("Alterac Mountains Horde grave", "api/PPather/WorldRoute?x1=-17.51&y1=-986.82&z1=55.83&x2=305.33337&y2=-364.6667&z2=168.28902&mapid=0"),

        // Durotar routes
        new("Z Durotar Sen'jin village to grind", "api/PPather/WorldRoute?x1=-779.3728&y1=-4926.2754&z1=0&x2=-575.14014&y2=-4298.2&z2=0&mapid=1&startindoors=true"),
        new("Durotar Sen'jin village to grind", "api/PPather/WorldRoute?x1=-779.3728&y1=-4926.2754&z1=22.3297&x2=-575.14014&y2=-4298.2&z2=0&mapid=1&startindoors=true"),

        // Orgrimmar
        new("Z Orgrimmar to vendor", "api/PPather/WorldRoute2?x1=1618.5112&y1=-4433.742&z1=0&x2=1633.98&y2=-4439.37&z2=15.51&uimap=1454&mapid=1&startindoors=false"),

        // Teldrassil
        new("Z Teldrassil to vendor", "api/PPather/WorldRoute2?x1=10477.419&y1=659.36426&z1=1326.2318&x2=10442.9&y2=783.989&z2=1337.37&uimap=1438&mapid=1&startindoors=false"),

        // Tanaris
        new("Tanaris FP to ZF", "api/PPather/MapRoute?uimap1=1446&x1=51.0&y1=29.3&uimap2=1446&x2=38.7&y2=20.1"),

        // Silithus
        new("Silithus Inn to AQ", "api/PPather/MapRoute?uimap1=1451&x1=51.4&y1=37.8&uimap2=1451&x2=29&y2=92"),

        // Kalimdor
        new("Kalimdor Search Barrens", "api/PPather/WorldRoute?x1=-896&y1=-3770&z1=11&x2=-441&y2=-2596&z2=96&mapid=1"),

        // Dun Morogh routes
        new("Azeroth Dun morogh 1", "api/PPather/WorldRoute?x1=-6238.128&y1=139.40344&z1=430.9192&x2=-5884.185&y2=-118.66675&z2=364.64783&mapid=0"),
        new("Dun morogh Vendor to grind", "api/PPather/WorldRoute2?x1=-6101.417&y1=390.9181&z1=395.626&x2=-6154.18&y2=609.84973&z2=395.626&uimap=1426&mapid=0&startindoors=true"),
        new("Azeroth Dun morogh 2", "api/PPather/WorldRoute?x1=-5609.00&y1=-479.00&z1=397.49&x2=-5884.185&y2=-118.66675&z2=364.64783&mapid=0"),
        new("Z Dun morogh Vendor Rybrad Coldbank", "api/PPather/WorldRoute?x1=-6200.1924&y1=700.5774&z1=384.6462&x2=-6103.1836&y2=393.5332&z2=0&mapid=0"),
        new("Azeroth Dun morogh Vendor Rybrad Coldbank", "api/PPather/WorldRoute?x1=-6200.1924&y1=700.5774&z1=384.6462&x2=-6103.1836&y2=393.5332&z2=396.0979&mapid=0"),

        // Problem routes (test cases)
        new("Z Azshara issue no result", "api/PPather/WorldRoute2?x1=2293.7908&y1=-6636.0283&z1=120.13607&x2=2309.54&y2=-6666.62&z2=0&uimap=1447"),
        new("Z Honor hold issue no result", "api/PPather/WorldRoute2?x1=-732.031&y1=2448.2805&z1=58.940506&x2=-755.79004&y2=2491.16&z2=0&uimap=1944"),
        new("Duskwood issue", "api/PPather/MapRoute?uimap1=1431&x1=43.097&y1=18.38&uimap2=1431&x2=45.34&y2=16.438"),

        // Building navigation tests (indoor corpus - multi-floor Z resolution)
        new("Loch Modan building Yanni Stoutheart", "api/PPather/MapRoute?uimap1=1432&x1=35.2&y1=46.9&uimap2=1432&x2=34.8&y2=48.6"),
        new("Loch Modan building innkeeper", "api/PPather/MapRoute?uimap1=1432&x1=35.2&y1=46.9&uimap2=1432&x2=35.5&y2=48.5"),
        new("Loch Modan building Vidra Heartstove", "api/PPather/MapRoute?uimap1=1432&x1=35.2&y1=46.9&uimap2=1432&x2=34.8&y2=49.1"),
        new("Dun morogh building Grundel Harkin", "api/PPather/MapRoute?uimap1=1426&x1=28.7&y1=70.1&uimap2=1426&x2=28.8&y2=67.9"),
        new("Dun morogh building Grundel Harkin reverse", "api/PPather/MapRoute?uimap1=1426&x1=28.8&y1=67.9&uimap2=1426&x2=28.7&y2=70.1"),
        new("Dun morogh Coldridge pass - unable to find", "api/PPather/MapRoute?uimap1=1426&x1=33.90&y1=71.86&uimap2=1426&x2=38.94&y2=61.0"),
        new("Dun morogh Coldridge pass", "api/PPather/MapRoute?uimap1=1426&x1=33.76&y1=71.91&uimap2=1426&x2=39.0&y2=61.13"),

        // Cross-zone long haul
        new("Cross-zone Elwynn to Redridge", "api/PPather/WorldRoute?x1=-9170.5&y1=355.4&z1=81.05&x2=-9230.0&y2=-2211.0&z2=0&mapid=0"),

        // Other zones
        new("Hinterlands 1 water issue", "api/PPather/MapRoute?uimap1=1425&x1=82.2771&y1=48.698803&uimap2=1425&x2=81.65922&y2=49.87935"),
        new("Hinterlands 2 water issue", "api/PPather/MapRoute?uimap1=1425&x1=80.7258&y1=64.2714&uimap2=1425&x2=78.321304&y2=64.0448"),
        new("Azshara 1", "api/PPather/MapRoute?uimap1=1447&x1=67.25&y1=82.88&uimap2=1447&x2=66.68&y2=90.23"),

        // TBC
        new("Ammen Vale", "api/PPather/MapRoute?uimap1=1943&x1=81.05&y1=45.33&uimap2=1943&x2=78.92&y2=44.14"),

        // Wotlk
        new("Hellfire 1", "api/PPather/MapRoute?uimap1=1944&x1=60.0762&y1=43.4372&uimap2=1944&x2=61.1473&y2=39.955284"),
        new("Hellfire 2", "api/PPather/MapRoute?uimap1=1944&x1=60.0762&y1=43.4372&uimap2=1944&x2=61.14&y2=38.38"),
        new("Zul'Drak stairs", "api/PPather/MapRoute?uimap1=121&x1=40.4058&y1=63.024403&uimap2=121&x2=40.3816&y2=64.259705"),
        new("Zul'Drak stairs reverse", "api/PPather/MapRoute?uimap1=121&x1=40.3816&y1=64.259705&uimap2=121&x2=40.4058&y2=63.024403"),
    ];

    private sealed record PathTest(string Name, string Endpoint);

    private readonly record struct PathPoint(float X, float Y, float Z);

    public sealed class BenchmarkResult
    {
        public string TestName { get; set; } = string.Empty;
        public double ColdMs { get; set; } = -1;
        public double[] WarmMs { get; set; } = [];
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int PointCount { get; set; }
        public double PathLengthYd { get; set; }
        public int CornerCount { get; set; }
        public double MaxCornerDeg { get; set; }

        // true/false = endpoint check performed (world-coord routes);
        // null = not applicable (map-coord routes have no world-space target).
        public bool? Reached { get; set; }

        public bool HasWarm => Success && WarmMs.Length > 0;
        public double WarmMinMs => HasWarm ? WarmMs.Min() : -1;
        public double WarmMaxMs => HasWarm ? WarmMs.Max() : -1;
        public double WarmAvgMs => HasWarm ? WarmMs.Average() : -1;
        public double WarmMedianMs => HasWarm ? Median(WarmMs.OrderBy(x => x).ToList()) : -1;
    }

    /// <summary>
    /// Runs the suite against each engine in turn (switching via
    /// POST api/PPather/Engine) and writes per-engine reports plus a
    /// side-by-side comparison markdown.
    /// </summary>
    public static async Task RunEngineComparison(string baseUrl, int iterations,
        string[] engines, ILogger? logger = null, string outputDir = "local/benchmark_results")
    {
        logger ??= Log.Logger;

        using HttpClient client = new();
        List<(string engine, List<BenchmarkResult> results)> runs = [];

        foreach (string engine in engines)
        {
            HttpResponseMessage response = await client.PostAsync(
                $"{baseUrl}/api/PPather/Engine?engine={engine}", null);

            if (!response.IsSuccessStatusCode)
            {
                logger.Error(string.Format("Engine switch to {0} failed: HTTP {1} - aborting comparison",
                    engine, response.StatusCode));
                return;
            }

            logger.Information(string.Format("\n===== Engine: {0} =====\n", engine));
            List<BenchmarkResult> results = await RunBenchmark(baseUrl, iterations,
                resetBetweenRuns: true, logger, label: engine, outputDir);
            runs.Add((engine, results));
        }

        string path = WriteComparisonReport(outputDir, baseUrl, iterations, runs);
        logger.Information(string.Format("\nComparison written: {0}", path));
    }

    public static async Task<List<BenchmarkResult>> RunBenchmark(string baseUrl, int iterations = 3,
        bool resetBetweenRuns = true, ILogger? logger = null,
        string label = "spot-astar", string outputDir = "local/benchmark_results")
    {
        logger ??= Log.Logger;

        int nameWidth = TestRoutes.Max(t => t.Name.Length) + 2;
        int separatorWidth = nameWidth + 96;

        string progressOkFmt = $"[{{0:D2}}/{{1:D2}}] {{2,-{nameWidth}}} ... OK Cold: {{3,8:F1}}ms | Warm Avg: {{4,8:F1}}ms | Pts: {{5,5}} | Len: {{6,7:F1}}yd | Crn: {{7,3}} | {{8}}";
        string progressFailFmt = $"[{{0:D2}}/{{1:D2}}] {{2,-{nameWidth}}} ... FAIL {{3}}";
        string tableHeaderFmt = $"{{0,-{nameWidth}}} | {{1,8}} | {{2,8}} | {{3,8}} | {{4,8}} | {{5,8}} | {{6,5}} | {{7,8}} | {{8,4}} | {{9,7}} | {{10,7}}";
        string tableRowFmt = $"{{0,-{nameWidth}}} | {{1,8:F1}} | {{2,8:F1}} | {{3,8:F1}} | {{4,8:F1}} | {{5,8:F1}} | {{6,5}} | {{7,8:F1}} | {{8,4}} | {{9,7:F1}} | {{10,7}}";

        logger.Information("=== PathingAPI End-to-End Benchmark Suite ===\n");
        logger.Information(string.Format("Base URL: {0}", baseUrl));
        logger.Information(string.Format("Label: {0}", label));
        logger.Information(string.Format("Test Routes: {0}", TestRoutes.Count));
        logger.Information(string.Format("Iterations per route: {0} (1 cold + {1} warm)", iterations, Math.Max(0, iterations - 1)));
        logger.Information(string.Format("Reset before each route: {0}", resetBetweenRuns));
        logger.Information("");

        using HttpClient client = new();
        List<BenchmarkResult> results = [];
        bool resetAvailable = true;

        long begin = Stopwatch.GetTimestamp();

        for (int i = 0; i < TestRoutes.Count; i++)
        {
            PathTest test = TestRoutes[i];

            BenchmarkResult result = new()
            {
                TestName = test.Name
            };

            try
            {
                // Reset BEFORE the first iteration: iteration 0 then measures a
                // cold query (fresh graph, pays chunk loading), the rest are warm.
                if (resetBetweenRuns && resetAvailable)
                {
                    HttpResponseMessage resetResponse = await client.PostAsync($"{baseUrl}/{ResetEndpoint}", null);
                    if (!resetResponse.IsSuccessStatusCode)
                    {
                        resetAvailable = false;
                        logger.Warning(string.Format(
                            "Reset endpoint {0} returned {1} - cold timings will include prior state!",
                            ResetEndpoint, resetResponse.StatusCode));
                    }
                }

                List<double> warm = new(Math.Max(0, iterations - 1));

                for (int iter = 0; iter < iterations; iter++)
                {
                    long startTime = Stopwatch.GetTimestamp();

                    HttpResponseMessage response = await client.GetAsync($"{baseUrl}/{test.Endpoint}");

                    double elapsedMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;

                    if (!response.IsSuccessStatusCode)
                    {
                        result.Success = false;
                        result.ErrorMessage = $"HTTP {response.StatusCode}";
                        break;
                    }

                    if (iter == 0)
                    {
                        result.ColdMs = elapsedMs;

                        PathPoint[] points = ParsePoints(
                            await response.Content.ReadAsStreamAsync());

                        result.PointCount = points.Length;
                        AnalyzePath(points, test.Endpoint, result);
                    }
                    else
                    {
                        warm.Add(elapsedMs);
                    }
                }

                if (result.ErrorMessage == null)
                {
                    result.Success = true;
                    result.WarmMs = [.. warm];
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            if (result.Success)
            {
                logger.Information(string.Format(progressOkFmt,
                    i + 1, TestRoutes.Count, test.Name, result.ColdMs,
                    result.HasWarm ? result.WarmAvgMs : double.NaN,
                    result.PointCount, result.PathLengthYd, result.CornerCount,
                    FormatReached(result.Reached)));
            }
            else
            {
                logger.Information(string.Format(progressFailFmt,
                    i + 1, TestRoutes.Count, test.Name, result.ErrorMessage));
            }

            results.Add(result);
        }

        double totalSeconds = Stopwatch.GetElapsedTime(begin).TotalSeconds;

        logger.Information("");
        logger.Information("=== Summary ===\n");
        logger.Information(string.Format("Total elapsed time: {0:F2}s", totalSeconds));

        List<BenchmarkResult> successfulResults = results.Where(r => r.Success).ToList();
        List<BenchmarkResult> failedResults = results.Where(r => !r.Success).ToList();
        List<BenchmarkResult> notReached = successfulResults.Where(r => r.Reached == false).ToList();

        logger.Information(string.Format("Successful tests: {0}/{1}", successfulResults.Count, results.Count));

        if (failedResults.Count > 0)
        {
            logger.Information(string.Format("Failed tests: {0}", failedResults.Count));
            foreach (BenchmarkResult failed in failedResults)
            {
                logger.Information(string.Format("  - {0}: {1}", failed.TestName, failed.ErrorMessage));
            }
        }

        if (notReached.Count > 0)
        {
            logger.Information(string.Format("Endpoint NOT reached (HTTP OK but last point > {0}yd from target): {1}", ReachedDistance, notReached.Count));
            foreach (BenchmarkResult miss in notReached)
            {
                logger.Information(string.Format("  - {0}: {1} points", miss.TestName, miss.PointCount));
            }
        }

        List<double> coldTimes = successfulResults.Where(r => r.ColdMs >= 0).Select(r => r.ColdMs).OrderBy(t => t).ToList();
        List<double> warmTimes = successfulResults.SelectMany(r => r.WarmMs).OrderBy(t => t).ToList();

        if (coldTimes.Count > 0)
        {
            logger.Information("");
            logger.Information(string.Format("Cold Statistics ({0} measurements - first query after Reset, includes chunk load):", coldTimes.Count));
            LogStats(logger, coldTimes);
        }

        if (warmTimes.Count > 0)
        {
            logger.Information("");
            logger.Information(string.Format("Warm Statistics ({0} measurements):", warmTimes.Count));
            LogStats(logger, warmTimes);
        }

        logger.Information("\n=== Detailed Results ===\n");
        logger.Information(string.Format(tableHeaderFmt,
            "Test Name", "Cold", "WarmMin", "WarmAvg", "WarmMed", "WarmMax", "Pts", "LenYd", "Crn", "MaxCrn", "Reached"));
        logger.Information(new string('-', separatorWidth));

        foreach (BenchmarkResult result in successfulResults.OrderBy(r => r.HasWarm ? r.WarmAvgMs : r.ColdMs))
        {
            logger.Information(string.Format(tableRowFmt,
                result.TestName, result.ColdMs, result.WarmMinMs, result.WarmAvgMs,
                result.WarmMedianMs, result.WarmMaxMs, result.PointCount,
                result.PathLengthYd, result.CornerCount, result.MaxCornerDeg,
                FormatReached(result.Reached)));
        }

        string reportPath = WriteMarkdownReport(outputDir, label, baseUrl, iterations,
            resetBetweenRuns && resetAvailable, totalSeconds, results, coldTimes, warmTimes, notReached);
        logger.Information(string.Format("\nReport written: {0}", reportPath));

        logger.Information("\nBenchmark complete!");

        return results;
    }

    private static string WriteComparisonReport(string outputDir, string baseUrl,
        int iterations, List<(string engine, List<BenchmarkResult> results)> runs)
    {
        Directory.CreateDirectory(outputDir);

        string fileName = string.Create(CultureInfo.InvariantCulture,
            $"comparison_{DateTime.Now:yyyyMMdd_HHmmss}.md");
        string path = Path.Combine(outputDir, fileName);

        StringBuilder sb = new();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"# Engine comparison - {string.Join(" vs ", runs.Select(r => r.engine))}"));
        sb.AppendLine();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {baseUrl} | {iterations} iterations (1 cold + {iterations - 1} warm)"));
        sb.AppendLine();
        sb.AppendLine("Cold = first query after server Reset. For SpotAStar that pays MPQ");
        sb.AppendLine("chunk loading; for Navmesh it pays disk tile loads (or bakes on a");
        sb.AppendLine("first-ever visit - a once-per-tile-per-lifetime cost).");
        sb.AppendLine();

        sb.AppendLine("## Overall");
        sb.AppendLine();
        sb.AppendLine("| Engine | OK | Reached | Cold Med | Cold P95 | Warm Med | Warm P95 | Avg Len yd | Avg Corners |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");

        foreach ((string engine, List<BenchmarkResult> results) in runs)
        {
            List<BenchmarkResult> ok = results.Where(r => r.Success).ToList();
            List<double> cold = ok.Where(r => r.ColdMs >= 0).Select(r => r.ColdMs).OrderBy(t => t).ToList();
            List<double> warm = ok.SelectMany(r => r.WarmMs).OrderBy(t => t).ToList();
            List<BenchmarkResult> withPath = ok.Where(r => r.PointCount > 0).ToList();

            int reached = results.Count(r => r.Reached == true);
            int reachable = results.Count(r => r.Reached != null);

            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {engine} | {ok.Count}/{results.Count} | {reached}/{reachable} | {MedianOrDash(cold)} | {PercentileOrDash(cold, 95)} | {MedianOrDash(warm)} | {PercentileOrDash(warm, 95)} | {(withPath.Count > 0 ? withPath.Average(r => r.PathLengthYd) : 0):F0} | {(withPath.Count > 0 ? withPath.Average(r => r.CornerCount) : 0):F0} |"));
        }

        sb.AppendLine();
        sb.AppendLine("## Per-route");
        sb.AppendLine();

        sb.Append("| Route |");
        foreach ((string engine, _) in runs)
        {
            sb.Append(string.Create(CultureInfo.InvariantCulture,
                $" {engine} Cold | {engine} Warm | {engine} Pts | {engine} Len | {engine} Crn | {engine} Reached |"));
        }
        sb.AppendLine();

        sb.Append("|---|");
        foreach ((string _, _) in runs)
        {
            sb.Append("---|---|---|---|---|---|");
        }
        sb.AppendLine();

        int routeCount = runs[0].results.Count;
        for (int i = 0; i < routeCount; i++)
        {
            sb.Append(string.Create(CultureInfo.InvariantCulture, $"| {runs[0].results[i].TestName} |"));

            foreach ((string _, List<BenchmarkResult> results) in runs)
            {
                BenchmarkResult r = results[i];
                if (r.Success)
                {
                    sb.Append(string.Create(CultureInfo.InvariantCulture,
                        $" {r.ColdMs:F0} | {(r.HasWarm ? r.WarmAvgMs : double.NaN):F1} | {r.PointCount} | {r.PathLengthYd:F0} | {r.CornerCount} | {FormatReached(r.Reached)} |"));
                }
                else
                {
                    sb.Append(" FAIL | - | - | - | - | - |");
                }
            }

            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
        return path;

        static string MedianOrDash(List<double> sorted)
        {
            return sorted.Count == 0 ? "-" : string.Create(CultureInfo.InvariantCulture, $"{Median(sorted):F1}ms");
        }

        static string PercentileOrDash(List<double> sorted, int p)
        {
            return sorted.Count == 0 ? "-" : string.Create(CultureInfo.InvariantCulture, $"{Percentile(sorted, p):F1}ms");
        }
    }

    private static void LogStats(ILogger logger, List<double> sortedTimes)
    {
        logger.Information(string.Format("  Min:    {0:F1}ms", sortedTimes[0]));
        logger.Information(string.Format("  Max:    {0:F1}ms", sortedTimes[^1]));
        logger.Information(string.Format("  Avg:    {0:F1}ms", sortedTimes.Average()));
        logger.Information(string.Format("  Median: {0:F1}ms", Median(sortedTimes)));
        logger.Information(string.Format("  P95:    {0:F1}ms", Percentile(sortedTimes, 95)));
        logger.Information(string.Format("  P99:    {0:F1}ms", Percentile(sortedTimes, 99)));
    }

    private static string FormatReached(bool? reached)
    {
        return reached switch
        {
            true => "yes",
            false => "NO",
            null => "n/a",
        };
    }

    /// <summary>
    /// Parses a JSON array of {"x":..,"y":..,"z":..} objects
    /// (see <c>SharedLib.Converters.Vector3Converter</c>; upper-case variant tolerated).
    /// </summary>
    private static PathPoint[] ParsePoints(Stream stream)
    {
        using JsonDocument doc = JsonDocument.Parse(stream);

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return [];

        int count = doc.RootElement.GetArrayLength();
        PathPoint[] points = new PathPoint[count];

        int i = 0;
        foreach (JsonElement element in doc.RootElement.EnumerateArray())
        {
            points[i++] = new PathPoint(
                GetFloat(element, "x", "X"),
                GetFloat(element, "y", "Y"),
                GetFloat(element, "z", "Z"));
        }

        return points;

        static float GetFloat(JsonElement element, string lower, string upper)
        {
            if (element.TryGetProperty(lower, out JsonElement value) ||
                element.TryGetProperty(upper, out value))
            {
                return value.GetSingle();
            }
            return 0f;
        }
    }

    /// <summary>
    /// Computes path length, corner metrics and - for world-coordinate routes -
    /// whether the path actually arrives at the requested target.
    /// </summary>
    private static void AnalyzePath(PathPoint[] points, string endpoint, BenchmarkResult result)
    {
        double length = 0;
        int corners = 0;
        double maxCornerDeg = 0;

        for (int i = 1; i < points.Length; i++)
        {
            float dx = points[i].X - points[i - 1].X;
            float dy = points[i].Y - points[i - 1].Y;
            float dz = points[i].Z - points[i - 1].Z;
            length += Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }

        // Corner detection on the XY plane - Z is height-guessed and noisy.
        double prevHeading = double.NaN;
        for (int i = 1; i < points.Length; i++)
        {
            float dx = points[i].X - points[i - 1].X;
            float dy = points[i].Y - points[i - 1].Y;

            if ((dx * dx) + (dy * dy) < 0.0001f)
                continue;

            double heading = Math.Atan2(dy, dx);

            if (!double.IsNaN(prevHeading))
            {
                double deltaDeg = Math.Abs(NormalizeAngle(heading - prevHeading)) * (180.0 / Math.PI);
                if (deltaDeg > CornerAngleDeg)
                    corners++;
                if (deltaDeg > maxCornerDeg)
                    maxCornerDeg = deltaDeg;
            }

            prevHeading = heading;
        }

        result.PathLengthYd = length;
        result.CornerCount = corners;
        result.MaxCornerDeg = maxCornerDeg;
        result.Reached = ComputeReached(points, endpoint);
    }

    private static double NormalizeAngle(double radians)
    {
        while (radians > Math.PI) radians -= 2 * Math.PI;
        while (radians < -Math.PI) radians += 2 * Math.PI;
        return radians;
    }

    /// <summary>
    /// World-coordinate routes (WorldRoute/WorldRoute2) carry the target as x2/y2
    /// query parameters - compare against the last path point (XY plane, Z is guessed).
    /// Map-coordinate routes have no world-space target: returns null (not applicable).
    /// </summary>
    private static bool? ComputeReached(PathPoint[] points, string endpoint)
    {
        if (!endpoint.Contains("WorldRoute"))
            return null;

        if (!TryGetQueryFloat(endpoint, "x2", out float targetX) ||
            !TryGetQueryFloat(endpoint, "y2", out float targetY))
        {
            return null;
        }

        if (points.Length == 0)
            return false;

        PathPoint last = points[^1];
        float dx = last.X - targetX;
        float dy = last.Y - targetY;
        return ((dx * dx) + (dy * dy)) <= (ReachedDistance * ReachedDistance);
    }

    private static bool TryGetQueryFloat(string endpoint, string name, out float value)
    {
        value = 0f;

        int queryStart = endpoint.IndexOf('?');
        if (queryStart < 0)
            return false;

        foreach (string pair in endpoint[(queryStart + 1)..].Split('&'))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0)
                continue;

            if (pair.AsSpan(0, eq).Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return float.TryParse(pair.AsSpan(eq + 1), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out value);
            }
        }

        return false;
    }

    private static string WriteMarkdownReport(string outputDir, string label,
        string baseUrl, int iterations, bool coldValid, double totalSeconds,
        List<BenchmarkResult> results, List<double> coldTimes, List<double> warmTimes,
        List<BenchmarkResult> notReached)
    {
        Directory.CreateDirectory(outputDir);

        string fileName = string.Create(CultureInfo.InvariantCulture,
            $"pathing_{Sanitize(label)}_{DateTime.Now:yyyyMMdd_HHmmss}.md");
        string path = Path.Combine(outputDir, fileName);

        StringBuilder sb = new();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"# PathingAPI Benchmark - {label}"));
        sb.AppendLine();
        sb.AppendLine("| Setting | Value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| Date | {DateTime.Now:yyyy-MM-dd HH:mm:ss} |"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| Base URL | {baseUrl} |"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| Routes | {results.Count} |"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| Iterations | {iterations} (1 cold + {Math.Max(0, iterations - 1)} warm) |"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| Cold timings valid | {coldValid} |"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| Total duration | {totalSeconds:F1}s |"));
        sb.AppendLine();

        AppendStatsSection(sb, "Cold (first query after Reset, includes chunk load)", coldTimes);
        AppendStatsSection(sb, "Warm", warmTimes);

        List<BenchmarkResult> failed = results.Where(r => !r.Success).ToList();
        if (failed.Count > 0)
        {
            sb.AppendLine("## Failed");
            sb.AppendLine();
            foreach (BenchmarkResult f in failed)
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"- {f.TestName}: {f.ErrorMessage}"));
            sb.AppendLine();
        }

        if (notReached.Count > 0)
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"## Endpoint not reached (last point > {ReachedDistance}yd from target)"));
            sb.AppendLine();
            foreach (BenchmarkResult miss in notReached)
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"- {miss.TestName}: {miss.PointCount} points"));
            sb.AppendLine();
        }

        sb.AppendLine("## Per-route results");
        sb.AppendLine();
        sb.AppendLine("| Route | Cold ms | Warm Min | Warm Avg | Warm Med | Warm Max | Pts | Len yd | Corners | Max Crn deg | Reached |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");

        foreach (BenchmarkResult r in results)
        {
            if (r.Success)
            {
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"| {r.TestName} | {r.ColdMs:F1} | {r.WarmMinMs:F1} | {r.WarmAvgMs:F1} | {r.WarmMedianMs:F1} | {r.WarmMaxMs:F1} | {r.PointCount} | {r.PathLengthYd:F1} | {r.CornerCount} | {r.MaxCornerDeg:F1} | {FormatReached(r.Reached)} |"));
            }
            else
            {
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"| {r.TestName} | FAIL | - | - | - | - | - | - | - | - | {r.ErrorMessage} |"));
            }
        }

        File.WriteAllText(path, sb.ToString());
        return path;
    }

    private static void AppendStatsSection(StringBuilder sb, string title, List<double> sortedTimes)
    {
        if (sortedTimes.Count == 0)
            return;

        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"## {title}"));
        sb.AppendLine();
        sb.AppendLine("| Min | Max | Avg | Median | P95 | P99 | Samples |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"| {sortedTimes[0]:F1}ms | {sortedTimes[^1]:F1}ms | {sortedTimes.Average():F1}ms | {Median(sortedTimes):F1}ms | {Percentile(sortedTimes, 95):F1}ms | {Percentile(sortedTimes, 99):F1}ms | {sortedTimes.Count} |"));
        sb.AppendLine();
    }

    private static string Sanitize(string label)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            label = label.Replace(c, '-');
        return label.Replace(' ', '-').ToLowerInvariant();
    }

    private static double Median(List<double> values)
    {
        return values.Count % 2 == 0
            ? (values[values.Count / 2 - 1] + values[values.Count / 2]) / 2
            : values[values.Count / 2];
    }

    private static double Percentile(List<double> values, int p)
    {
        int index = (int)Math.Ceiling(values.Count * (p / 100.0)) - 1;
        return values[Math.Max(0, Math.Min(index, values.Count - 1))];
    }
}
