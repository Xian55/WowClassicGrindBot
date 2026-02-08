using System;
using System.Collections.Generic;
using System.Numerics;

using BenchmarkDotNet.Attributes;

using PPather.Graph;

namespace Benchmarks.PPather;

/// <summary>
/// Benchmarks for PathGraph and SpotManager performance improvements from SoA refactoring.
///
/// Measures:
/// - SpotManager registration and lookup (array-based storage)
/// - SearchBuffer allocation and reset (pooled search state)
/// - Path scoring operations in tight A* loop
/// - Overall memory allocation patterns
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class PPather_PathGraph_Performance
{
    private const int SpotCount = 10000;
    private const int SearchCount = 100;

    private SpotManager spotManager = null!;
    private List<Spot> testSpots = null!;
    private GraphChunk dummyChunk = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Create a SpotManager with capacity for test spots
        spotManager = new SpotManager(capacity: SpotCount);
        testSpots = new List<Spot>(SpotCount);

        // Create a dummy chunk for spot registration
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<GraphChunk>();
        dummyChunk = new GraphChunk(0f, 0f, 0, 0, logger, ".", spotManager);

        // Create and register test spots (simulates loading a chunk)
        var random = new Random(42);
        for (int i = 0; i < SpotCount; i++)
        {
            var loc = new Vector3(
                random.Next(0, 256),
                random.Next(0, 256),
                random.Next(-100, 100)
            );

            var spot = new Spot(loc) { flags = 0 };
            testSpots.Add(spot);
            dummyChunk.AddSpot(spot);
        }
    }

    /// <summary>
    /// Benchmark: SpotManager location lookups (hot path access)
    /// Measures O(1) array lookups for spot locations
    /// </summary>
    [Benchmark]
    public Vector3 SpotManager_Location_Lookups()
    {
        Vector3 result = Vector3.Zero;
        var random = new Random(42);

        for (int i = 0; i < 1000; i++)
        {
            int randomIdx = random.Next(testSpots.Count);
            var spot = testSpots[randomIdx];
            result = spotManager.GetLocation(spot);
        }

        return result;
    }

    /// <summary>
    /// Benchmark: SpotManager distance calculations (hot path in A* scoring)
    /// Measures cache-friendly vector math on contiguous array data
    /// </summary>
    [Benchmark]
    public float SpotManager_Distance_Calculations()
    {
        float totalDistance = 0f;
        var random = new Random(42);

        for (int i = 0; i < 1000; i++)
        {
            int idx1 = random.Next(testSpots.Count);
            int idx2 = random.Next(testSpots.Count);

            var dist = spotManager.GetDistance(testSpots[idx1], testSpots[idx2]);
            totalDistance += dist;
        }

        return totalDistance;
    }

    /// <summary>
    /// Benchmark: SearchBuffer allocation and reset (per-search overhead)
    /// Measures pooling efficiency and array clearing performance
    /// </summary>
    [Benchmark]
    public int SearchBuffer_Allocation_And_Reset()
    {
        int count = 0;

        for (int search = 0; search < SearchCount; search++)
        {
            spotManager.InitializeSearch(search, testSpots[0]);
            spotManager.CompleteSearch();
            count++;
        }

        return count;
    }

    /// <summary>
    /// Benchmark: Spot registration (eager registration via AddSpot)
    /// Measures performance of registering spots when they're added to chunks
    /// </summary>
    [Benchmark]
    public int SpotManager_Eager_Registration()
    {
        var random = new Random(123);
        int registered = 0;

        // Clear and re-register a subset of spots (simulates loading a chunk)
        for (int i = 0; i < 500; i++)
        {
            int idx = random.Next(testSpots.Count);
            var spot = testSpots[idx];

            // Registration overhead is minimal since spot is already registered
            // This measures the cost of the registration check
            spotManager.RegisterSpot(spot, spot.Loc, spot.flags, dummyChunk);
            registered++;
        }

        return registered;
    }

    /// <summary>
    /// Benchmark: SpotManager flag operations (common in path evaluation)
    /// Measures performance of bit flag checks on array data
    /// </summary>
    [Benchmark]
    public int SpotManager_Flag_Operations()
    {
        int flaggedSpots = 0;

        // Set water flag on random spots
        var random = new Random(42);
        for (int i = 0; i < 500; i++)
        {
            int idx = random.Next(testSpots.Count);
            spotManager.SetFlag(testSpots[idx], Spot.FLAG_WATER, true);
        }

        // Count spots with water flag
        foreach (var spot in testSpots)
        {
            if (spotManager.IsFlagSet(spot, Spot.FLAG_WATER))
                flaggedSpots++;
        }

        return flaggedSpots;
    }

    /// <summary>
    /// Benchmark: Path reconstruction (traceback following)
    /// Simulates walking back through parent pointers to reconstruct path
    /// </summary>
    [Benchmark]
    public int SpotManager_Path_Reconstruction()
    {
        int pathsReconstructed = 0;

        // Create some parent relationships
        spotManager.InitializeSearch(999, testSpots[0]);

        var random = new Random(42);
        for (int i = 1; i < 100; i++)
        {
            int childIdx = random.Next(1, Math.Min(i + 50, testSpots.Count));
            int parentIdx = Math.Max(0, childIdx - 1);
            spotManager.SetTraceBack(testSpots[childIdx], testSpots[parentIdx]);
        }

        // Trace back paths
        for (int i = 50; i < 100; i++)
        {
            var spot = testSpots[i];
            var current = spot;
            int steps = 0;

            while (current != null && steps < 100)
            {
                current = spotManager.GetTraceBack(current);
                if (current == null) break;
                steps++;
            }

            pathsReconstructed++;
        }

        spotManager.CompleteSearch();
        return pathsReconstructed;
    }

    /// <summary>
    /// Benchmark: Combined A* scoring operations
    /// Simulates the tight loop in SpotManager's ScoreSpot method
    /// </summary>
    [Benchmark]
    public float SpotManager_AStar_Scoring_Loop()
    {
        spotManager.InitializeSearch(888, testSpots[0]);

        float totalScore = 0f;
        var random = new Random(42);

        for (int iteration = 0; iteration < 1000; iteration++)
        {
            int currentIdx = random.Next(testSpots.Count - 1);
            int nextIdx = currentIdx + 1;

            var current = testSpots[currentIdx];
            var next = testSpots[nextIdx];

            // Typical A* scoring operations
            float gScore = spotManager.GetTraceBackDistance(current) +
                          spotManager.GetDistance(current, next);
            float hScore = spotManager.GetDistance2D(next, testSpots[0]);
            float fScore = gScore + hScore;

            if (!spotManager.SearchScoreIsSet(next) || fScore < spotManager.SearchScoreGet(next))
            {
                spotManager.SetTraceBack(next, current);
                spotManager.SetTraceBackDistance(next, gScore);
                spotManager.SearchScoreSet(next, fScore);
                totalScore += fScore;
            }
        }

        spotManager.CompleteSearch();
        return totalScore;
    }
}
