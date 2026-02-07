using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;

namespace Benchmarks;

/// <summary>
/// End-to-end benchmark suite for PathingAPI endpoints.
/// Tests real pathfinding operations and measures:
/// - Total elapsed time per request
/// - Memory usage patterns
/// - Consistency across multiple runs
///
/// Usage: dotnet run --project Benchmarks -- --pather-benchmark [BaseUrl] [Iterations]
/// Example: dotnet run --project Benchmarks -- --pather-benchmark http://localhost:5000 5
/// </summary>
public class PathingAPIBenchmark
{
    private static readonly List<PathTest> TestRoutes =
    [
        // Elwynn Forest routes
        new("Elwynn vendor to path", "api/PPather/WorldRoute?x1=-8898.32&y1=-117.35608&z1=81.840546&x2=-8779.58&y2=-106.12012&z2=0&mapid=0"),
        new("Z Elwynn vendor to path", "api/PPather/WorldRoute2?x1=-8898.32&y1=-117.35608&z1=0&x2=-8779.58&y2=-106.12012&z2=0&uimap=1429&startindoors=false"),
        new("Z Coldridge to 5_gnome", "api/PPather/WorldRoute?x1=-6120.8084&y1=542.90857&z1=0&x2=-5880.7373&y2=-116.15503&z2=0&mapid=0"),

        // Redridge Mountains
        new("Redridge Grave to North", "api/PPather/MapRoute?uimap1=1433&x1=30&y1=60&uimap2=1433&x2=54.3&y2=43.1"),

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

        // Building navigation tests
        //new("Loch Modan building Yanni Stoutheart", "api/PPather/MapRoute?uimap1=1432&x1=35.2&y1=46.9&uimap2=1432&x2=34.8&y2=48.6"),
        //new("Loch Modan building innkeeper", "api/PPather/MapRoute?uimap1=1432&x1=35.2&y1=46.9&uimap2=1432&x2=35.5&y2=48.5"),
        //new("Loch Modan building Vidra Heartstove", "api/PPather/MapRoute?uimap1=1432&x1=35.2&y1=46.9&uimap2=1432&x2=34.8&y2=49.1"),
        //new("Dun morogh building Grundel Harkin", "api/PPather/MapRoute?uimap1=1426&x1=28.7&y1=70.1&uimap2=1426&x2=28.8&y2=67.9"),
        //new("Dun morogh building Grundel Harkin reverse", "api/PPather/MapRoute?uimap1=1426&x1=28.8&y1=67.9&uimap2=1426&x2=28.7&y2=70.1"),
        new("Dun morogh Coldridge pass - unable to find", "api/PPather/MapRoute?uimap1=1426&x1=33.90&y1=71.86&uimap2=1426&x2=38.94&y2=61.0"),
        new("Dun morogh Coldridge pass", "api/PPather/MapRoute?uimap1=1426&x1=33.76&y1=71.91&uimap2=1426&x2=39.0&y2=61.13"),

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

    private class BenchmarkResult
    {
        public string TestName { get; set; }
        public double[] ElapsedMs { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public int PointCount { get; set; }

        public double MinMs => Success ? ElapsedMs.Min() : -1;
        public double MaxMs => Success ? ElapsedMs.Max() : -1;
        public double AvgMs => Success ? ElapsedMs.Average() : -1;
        public double MedianMs => Success ? Median(ElapsedMs) : -1;

        private static double Median(double[] values)
        {
            var sorted = values.OrderBy(x => x).ToArray();
            return sorted.Length % 2 == 0
                ? (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2
                : sorted[sorted.Length / 2];
        }
    }

    public static async Task RunBenchmark(string baseUrl, int iterations = 3, bool resetBetweenRuns = true, ILogger? logger = null)
    {
        logger ??= Log.Logger;

        int nameWidth = TestRoutes.Max(t => t.Name.Length) + 2;
        int separatorWidth = nameWidth + 52; // 4 time columns × 11 chars each + Pts column 8 chars

        string progressOkFmt = $"[{{0:D2}}/{{1:D2}}] {{2,-{nameWidth}}} ... OK Min: {{3,8:F1}}ms | Avg: {{4,8:F1}}ms | Median: {{5,8:F1}}ms | Max: {{6,8:F1}}ms | Pts: {{7,5}}";
        string progressFailFmt = $"[{{0:D2}}/{{1:D2}}] {{2,-{nameWidth}}} ... FAIL {{3}}";
        string tableHeaderFmt = $"{{0,-{nameWidth}}} | {{1,8}} | {{2,8}} | {{3,8}} | {{4,8}} | {{5,5}}";
        string tableRowFmt = $"{{0,-{nameWidth}}} | {{1,8:F1}}ms | {{2,8:F1}}ms | {{3,8:F1}}ms | {{4,8:F1}}ms | {{5,5}}";

        logger.Information("=== PathingAPI End-to-End Benchmark Suite ===\n");
        logger.Information(string.Format("Base URL: {0}", baseUrl));
        logger.Information(string.Format("Test Routes: {0}", TestRoutes.Count));
        logger.Information(string.Format("Iterations per route: {0}", iterations));
        logger.Information(string.Format("Reset between runs: {0}", resetBetweenRuns));
        logger.Information("");

        using HttpClient client = new();
        List<BenchmarkResult> results = [];

        long begin = Stopwatch.GetTimestamp();

        // Run benchmarks
        for (int i = 0; i < TestRoutes.Count; i++)
        {
            PathTest test = TestRoutes[i];

            BenchmarkResult result = new()
            {
                TestName = test.Name,
                ElapsedMs = new double[iterations]
            };

            try
            {
                for (int iter = 0; iter < iterations; iter++)
                {
                    if (resetBetweenRuns && iter > 0)
                    {
                        // Call reset endpoint between iterations
                        try
                        {
                            await client.PostAsync($"{baseUrl}/api/Reset", null);
                        }
                        catch
                        {
                            // Reset might not exist, continue anyway
                        }
                    }

                    long startTime = Stopwatch.GetTimestamp();

                    HttpResponseMessage response = await client.GetAsync($"{baseUrl}/{test.Endpoint}");

                    result.ElapsedMs[iter] = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;

                    if (!response.IsSuccessStatusCode)
                    {
                        result.Success = false;
                        result.ErrorMessage = $"HTTP {response.StatusCode}";
                        break;
                    }

                    if (iter == 0)
                    {
                        using JsonDocument doc = JsonDocument.Parse(
                            await response.Content.ReadAsStreamAsync());
                        result.PointCount = doc.RootElement.GetArrayLength();
                    }
                }

                if (result.ErrorMessage == null)
                    result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            if (result.Success)
            {
                logger.Information(string.Format(progressOkFmt,
                    i + 1, TestRoutes.Count, test.Name, result.MinMs, result.AvgMs, result.MedianMs, result.MaxMs, result.PointCount));
            }
            else
            {
                logger.Information(string.Format(progressFailFmt,
                    i + 1, TestRoutes.Count, test.Name, result.ErrorMessage));
            }

            results.Add(result);
        }

        logger.Information("");
        logger.Information("=== Summary ===\n");
        logger.Information(string.Format("Total elapsed time: {0:F2}s", Stopwatch.GetElapsedTime(begin).TotalSeconds));

        // Statistics
        List<BenchmarkResult> successfulResults = results.Where(r => r.Success).ToList();
        List<BenchmarkResult> failedResults = results.Where(r => !r.Success).ToList();

        logger.Information(string.Format("Successful tests: {0}/{1}", successfulResults.Count, results.Count));
        if (failedResults.Count > 0)
        {
            logger.Information(string.Format("Failed tests: {0}", failedResults.Count));
            foreach (BenchmarkResult failed in failedResults)
            {
                logger.Information(string.Format("  - {0}: {1}", failed.TestName, failed.ErrorMessage));
            }
        }

        if (successfulResults.Count > 0)
        {
            logger.Information("");
            List<double> allTimes = successfulResults.SelectMany(r => r.ElapsedMs).OrderBy(t => t).ToList();
            logger.Information(string.Format("Overall Statistics (all {0} measurements):", allTimes.Count));
            logger.Information(string.Format("  Min:    {0}ms", allTimes.Min()));
            logger.Information(string.Format("  Max:    {0}ms", allTimes.Max()));
            logger.Information(string.Format("  Avg:    {0:F1}ms", allTimes.Average()));
            logger.Information(string.Format("  Median: {0}ms", Median(allTimes)));
            logger.Information(string.Format("  P95:    {0}ms", Percentile(allTimes, 95)));
            logger.Information(string.Format("  P99:    {0}ms", Percentile(allTimes, 99)));
        }

        // Detailed results table
        logger.Information("\n=== Detailed Results ===\n");
        logger.Information(string.Format(tableHeaderFmt,
            "Test Name", "Min", "Avg", "Median", "Max", "Pts"));
        logger.Information(new string('-', separatorWidth));

        foreach (BenchmarkResult result in successfulResults.OrderBy(r => r.AvgMs))
        {
            logger.Information(string.Format(tableRowFmt,
                result.TestName, result.MinMs, result.AvgMs, result.MedianMs, result.MaxMs, result.PointCount));
        }

        logger.Information("\nBenchmark complete!");
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
