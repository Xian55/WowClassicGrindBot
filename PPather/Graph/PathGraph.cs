/*
  This file is part of ppather.

    PPather is free software: you can redistribute it and/or modify
    it under the terms of the GNU Lesser General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    PPather is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Lesser General Public License for more details.

    You should have received a copy of the GNU Lesser General Public License
    along with ppather.  If not, see <http://www.gnu.org/licenses/>.

*/

using Microsoft.Extensions.Logging;

using PPather.Triangles.Data;

using SharedLib.Data;
using SharedLib.Extensions;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;

using WowTriangles;

using static System.Diagnostics.Stopwatch;
using static System.MathF;

#pragma warning disable 162

namespace PPather.Graph;

public sealed class PathGraph
{
    public const int DelayMs = 0;

    private const int TimeoutSeconds = 20 + (DelayMs * 100);
    private const int ProgressTimeoutSeconds = 10 + (DelayMs * 100);

    public const int gradiantMax = 10;

    public const float toonHeight = 2.0f;
    public const float toonSize = 0.2f;

    public const float toonHeightHalf = toonHeight / 2f;
    public const float toonHeightQuad = toonHeight / 4f;

    public const float stepDistance = toonSize / 2f;

    public const float MinStepLength = 4f * toonSize;
    public const float WantedStepLength = 6f * toonSize;
    public const float MaxStepLength = 10f * toonSize;

    public const float StepPercent = 0.75f;
    public const float STEP_D = toonSize / 4f;

    public const float WallAvoidanceDistance = 3.0f;

    public const float IsCloseToModelRange = toonSize * 2f;

    public const float IsCloseToObjectRange = MaxStepLength;

    public const float CeilingCheckHeight = 20f;

    private const int COST_MOVE_THRU_WATER = 128 * 6;

    /*
	public const float IndoorsWantedStepLength = 1.5f;
	public const float IndoorsMaxStepLength = 2.5f;
	*/

    public const float CHUNK_BASE = 100000.0f; // Always keep positive
    public const float MaximumAllowedRangeFromTarget = 5; //60

    private readonly ILogger logger;
    private readonly string chunkDir;

    private readonly float MapId;
    private readonly SparseMatrix2D<GraphChunk> chunks;
    public readonly ChunkedTriangleCollection triangleWorld;

    // Grid density for spot placement (configurable)
    // Lower values = denser grid, more spots, better precision
    // Higher values = sparser grid, fewer spots, faster pathfinding
    // Default: WantedStepLength (1.2 units)
    public const float SpotGridSize = WantedStepLength;

    private readonly HashSet<int> generatedChunks;

    private const int maxCache = 512;
    private long LRU;

    // ==================== NEW: SpotManager for contiguous array storage ====================
    // Manages all spot data in Structure-of-Arrays layout for cache efficiency
    private readonly SpotManager spotManager;
    public SpotManager SpotManager => spotManager;

    public int GetTriangleClosenessScore(Vector3 loc)
    {
        const TriangleType mask = TriangleType.Model | TriangleType.Object;
        const float ignoreStep = toonHeightHalf - stepDistance;

        float dist = triangleWorld.ClosestDistanceToType(
            loc.X, loc.Y, loc.Z + ignoreStep, 7 * WantedStepLength, mask);

        return dist >= 5 * WantedStepLength ? 0
            : dist >= 4 * WantedStepLength ? 16
            : dist >= 3 * WantedStepLength ? 64
            : dist >= 2 * WantedStepLength ? 192
            : 384;
    }

    public int GetTriangleGradiantScore(Vector3 loc, int gradiantMax)
    {
        return triangleWorld.GradiantScoreTiered(loc.X, loc.Y, gradiantMax);
    }

    public PathGraph(float mapId,
                     ChunkedTriangleCollection triangles,
                     ILogger logger, DataConfig dataConfig)
    {
        this.logger = logger;
        this.MapId = mapId;
        this.triangleWorld = triangles;

        chunkDir = System.IO.Path.Join(dataConfig.PathInfo, ContinentDB.IdToName[MapId]);
        if (!Directory.Exists(chunkDir))
            Directory.CreateDirectory(chunkDir);

        chunks = new SparseMatrix2D<GraphChunk>(8);
        spotManager = new SpotManager(capacity: 50000);  // Estimated max spots

        //filePath = System.IO.Path.Join(baseDir, string.Format("c_{0,3:000}_{1,3:000}.bin", ix, iy));
        var files = Directory.GetFiles(chunkDir, "*.bin");
        generatedChunks = new HashSet<int>(Math.Min(files.Length, 512));

        foreach (string file in files)
        {
            ReadOnlySpan<char> parts = System.IO.Path.GetFileNameWithoutExtension(file);

            var a = parts.Slice(2); // remove c_
            var sep = a.IndexOf('_');

            int ix = int.Parse(a[..sep]);
            int iy = int.Parse(a[(sep + 1)..]);

            int key = chunks.GetKey(ix, iy);
            generatedChunks.Add(key);
        }
    }

    public void Clear()
    {
        triangleWorld.Close();

        foreach (GraphChunk chunk in chunks.GetAllElements())
        {
            chunk.Clear();
        }
        chunks.Clear();
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void GetChunkCoord(float x, float y, out int ix, out int iy)
    {
        ix = (int)((CHUNK_BASE + x) / GraphChunk.CHUNK_SIZE);
        iy = (int)((CHUNK_BASE + y) / GraphChunk.CHUNK_SIZE);
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void GetChunkBase(int ix, int iy, out float bx, out float by)
    {
        bx = (float)ix * GraphChunk.CHUNK_SIZE - CHUNK_BASE;
        by = (float)iy * GraphChunk.CHUNK_SIZE - CHUNK_BASE;
    }

    /// <summary>
    /// Snap coordinates to the configured grid for uniform spot placement.
    /// This ensures spots are placed at predictable, evenly-spaced positions.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SnapToGrid(float x, float y, out float snappedX, out float snappedY)
    {
        snappedX = Round(x / SpotGridSize) * SpotGridSize;
        snappedY = Round(y / SpotGridSize) * SpotGridSize;
    }

    private bool GetChunkAt(float x, float y, [MaybeNullWhen(false)] out GraphChunk c)
    {
        GetChunkCoord(x, y, out int ix, out int iy);
        if (chunks.TryGetValue(ix, iy, out c))
        {
            c.LRU = LRU++;
            return true;
        }

        c = default;
        return false;
    }

    private void CheckForChunkEvict()
    {
        if (chunks.Count < maxCache)
            return;

        //lock (chunks)
        {
            GraphChunk evict = null;
            foreach (GraphChunk gc in chunks.GetAllElements())
            {
                if (evict == null || gc.LRU < evict.LRU)
                {
                    evict = gc;
                }
            }

            evict.Save();
            chunks.Remove(evict.ix, evict.iy);
            evict.Clear();
        }
    }

    public void Save()
    {
        foreach (GraphChunk gc in chunks.GetAllElements())
        {
            if (gc.modified)
            {
                gc.Save();
            }
        }
    }

    // Create and load from file if exisiting
    private GraphChunk LoadChunk(float x, float y)
    {
        if (GetChunkAt(x, y, out GraphChunk gc))
            return gc;

        GetChunkCoord(x, y, out int ix, out int iy);
        GetChunkBase(ix, iy, out float base_x, out float base_y);

        gc = new GraphChunk(base_x, base_y, ix, iy, logger, chunkDir, spotManager);

        int key = chunks.GetKey(ix, iy);

        if (generatedChunks.Contains(key))
        {
            gc.Load();
        }

        chunks.Add(ix, iy, gc);
        generatedChunks.Add(key);

        return gc;
    }

    public Spot AddSpot(Spot s)
    {
        // Snap spot coordinates to grid for uniform placement
        SnapToGrid(s.Loc.X, s.Loc.Y, out float snappedX, out float snappedY);
        s.Loc = new Vector3(snappedX, snappedY, s.Loc.Z);

        GraphChunk gc = LoadChunk(s.Loc.X, s.Loc.Y);
        var result = gc.AddSpot(s);

        return result;
    }

    [InlineArray(8)]
    private struct SpotBuffer8
    {
        private Spot _element;
    }

    // Connect according to MPQ data
    public Spot AddAndConnectSpot(Spot s)
    {
        s = AddSpot(s);
        if (s.IsFlagSet(Spot.FLAG_MPQ_MAPPED))
        {
            return s;
        }

        Vector3 avoidSmallBumps = new(0, 0, toonHeightHalf);
        Vector3 origin = s.Loc + avoidSmallBumps;

        // Grid-based connectivity: only connect to the 8 immediate grid neighbors
        // This prevents long-distance shortcuts and maintains uniform pathfinding
        ReadOnlySpan<(int dx, int dy)> neighborOffsets =
        [
            (-1, 0), (1, 0), (0, -1), (0, 1),      // Cardinal: W, E, S, N
            (-1, -1), (-1, 1), (1, -1), (1, 1)     // Diagonal: SW, NW, SE, NE
        ];

        // Phase 1: Collect valid neighbors needing LOS checks
        Span<Vector3> targets = stackalloc Vector3[8];
        SpotBuffer8 neighbors = default;
        int count = 0;

        foreach (var (dx, dy) in neighborOffsets)
        {
            // Calculate grid neighbor position
            float nx = s.Loc.X + (dx * SpotGridSize);
            float ny = s.Loc.Y + (dy * SpotGridSize);

            // Try to find spot at this grid position (with Z tolerance for terrain following)
            Spot neighbor = GetSpot2D(nx, ny);

            if (neighbor == null || neighbor == s)
                continue;

            // Skip if either spot is blocked or already connected
            if (neighbor.IsBlocked() || s.IsBlocked() ||
                (spotManager.HasPathTo(s, neighbor) && spotManager.HasPathTo(neighbor, s)))
            {
                continue;
            }

            targets[count] = neighbor.Loc + avoidSmallBumps;
            neighbors[count] = neighbor;
            count++;
        }

        if (count == 0)
            return s;

        // Phase 2: Batch LOS check — single GetAllCloseTo query for all neighbors
        Span<bool> results = stackalloc bool[count];
        // maxRange covers diagonal distance + margin: SpotGridSize * sqrt(2) + 1
        float maxRange = SpotGridSize * 1.415f + 1f;
        triangleWorld.LineOfSightBatch(origin, targets[..count], results, maxRange);

        // Phase 3: Connect spots where LOS exists
        for (int i = 0; i < count; i++)
        {
            if (results[i])
            {
                spotManager.AddPathTo(s, neighbors[i]);
                spotManager.AddPathTo(neighbors[i], s);
            }
        }

        return s;
    }

    public Spot GetSpot(float x, float y, float z)
    {
        GraphChunk gc = LoadChunk(x, y);
        return gc.GetSpot(x, y, z);
    }

    public Spot GetSpot2D(float x, float y)
    {
        GraphChunk gc = LoadChunk(x, y);
        return gc.GetSpot2D(x, y);
    }

    public Spot GetSpot(Vector3 l)
    {
        return GetSpot(l.X, l.Y, l.Z);
    }

    public Spot FindClosestSpot(Vector3 l, float max_d)
    {
        Spot closest = null;
        float closest_d = Math.Max(WantedStepLength, max_d);
        ReadOnlySpan<int> dx = [-1, 1, 0, 0];
        ReadOnlySpan<int> dy = [0, 0, -1, 1];

        for (int y = 0; y < 4; y++)
        {
            float nx = l.X + (dx[y] * WantedStepLength);
            float ny = l.Y + (dy[y] * WantedStepLength);

            Spot s = GetSpot2D(nx, ny);
            while (s != null)
            {
                float di = s.GetDistanceTo(l);
                if (di < closest_d && !s.IsBlocked())
                {
                    closest = s;
                    closest_d = di;
                }
                s = s.next;
            }
        }

        return closest;
    }

    public ReadOnlySpan<Spot> FindAllSpots(Spot s, float max_d)
    {
        Vector3 l = s.Loc;

        const int SV_LENGTH = 4;
        var pooler = ArrayPool<Spot>.Shared;
        Spot[] sv = pooler.Rent(SV_LENGTH);

        int size = (int)Ceiling(2 * (max_d / STEP_D));
        Spot[] sl = pooler.Rent(size);
        int c = 0;

        int d = 0;
        while (d <= max_d + STEP_D)
        {
            for (int i = -d; i <= d; i++)
            {
                float x_up = l.X + d;
                float x_dn = l.X - d;
                float y_up = l.Y + d;
                float y_dn = l.Y - d;

                sv[0] = GetSpot2D(x_up, l.Y + i);
                sv[1] = GetSpot2D(x_dn, l.Y + i);
                sv[2] = GetSpot2D(l.X + i, y_dn);
                sv[3] = GetSpot2D(l.X + i, y_up);

                for (int j = 0; j < SV_LENGTH; j++)
                {
                    Spot ss = sv[j];
                    Spot sss = ss;
                    while (sss != null)
                    {
                        float di = sss.GetDistanceTo(l);
                        if (di < max_d)
                        {
                            sl[c++] = sss;
                        }
                        sss = sss.next;
                    }
                }
            }
            d++;
        }

        pooler.Return(sv);
        pooler.Return(sl);

        return new(sl, 0, c);
    }

    public int GetNeighborCount(Spot s)
    {
        Vector3 l = s.Loc;

        const float step = WantedStepLength;

        int c = 0;
        for (float x = -step; x <= step; x += step)
        {
            for (float y = -step; y <= step; y += step)
            {
                var n = GetSpot2D(l.X + step, l.Y + step);
                if (n == null || n.IsBlocked() || n == s)
                    continue;

                c++;
            }
        }
        return c;
    }

    public Spot TryAddSpot(Spot wasAt, Vector3 isAt)
    {
        //if (IsUnderwaterOrInAir(isAt)) { return wasAt; }
        Spot isAtSpot = FindClosestSpot(isAt, WantedStepLength);
        if (isAtSpot == null)
        {
            isAtSpot = GetSpot(isAt);
            if (isAtSpot == null)
            {
                Spot s = new Spot(isAt);
                s = AddSpot(s);
                isAtSpot = s;
            }
            if (isAtSpot.IsFlagSet(Spot.FLAG_BLOCKED))
            {
                isAtSpot.SetFlag(Spot.FLAG_BLOCKED, false);
                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug("Cleared blocked flag");
            }
            if (wasAt != null)
            {
                // Only connect if wasAt is a grid neighbor (not a long-distance movement)
                float distance = Vector3.Distance(wasAt.Loc, isAtSpot.Loc);
                if (distance <= SpotGridSize * 1.5f) // Allow up to 1.5x for diagonal
                {
                    spotManager.AddPathTo(wasAt, isAtSpot);
                    spotManager.AddPathTo(isAtSpot, wasAt);
                }
            }

            // Grid-based connectivity: connect to 8 grid neighbors only
            ReadOnlySpan<(int dx, int dy)> neighborOffsets =
            [
                (-1, 0), (1, 0), (0, -1), (0, 1),
                (-1, -1), (-1, 1), (1, -1), (1, 1)
            ];

            int connected = 0;
            foreach (var (dx, dy) in neighborOffsets)
            {
                float nx = isAtSpot.Loc.X + (dx * SpotGridSize);
                float ny = isAtSpot.Loc.Y + (dy * SpotGridSize);

                Spot neighbor = GetSpot2D(nx, ny);
                if (neighbor != null && neighbor != isAtSpot && !neighbor.IsBlocked())
                {
                    // Check elevation difference
                    float elevationDiff = MathF.Abs(isAtSpot.Loc.Z - neighbor.Loc.Z);
                    if (elevationDiff <= MaxStepLength)
                    {
                        spotManager.AddPathTo(neighbor, isAtSpot);
                        spotManager.AddPathTo(isAtSpot, neighbor);
                        connected++;
                    }
                }
            }
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Learned a new spot at {Location} connected to {ConnectedCount} other spots", isAtSpot.Loc, connected);
            wasAt = isAtSpot;
        }
        else
        {
            if (wasAt != null && wasAt != isAtSpot)
            {
                // moved to an old spot, make sure they are connected
                // Only connect if it's a grid neighbor (not a long-distance movement)
                float distance = Vector3.Distance(wasAt.Loc, isAtSpot.Loc);
                if (distance <= SpotGridSize * 1.5f) // Allow up to 1.5x for diagonal
                {
                    spotManager.AddPathTo(wasAt, isAtSpot);
                    spotManager.AddPathTo(isAtSpot, wasAt);
                }
            }
            wasAt = isAtSpot;
        }

        return wasAt;
    }

    private static bool LineCrosses(Vector3 line0, Vector3 line1, Vector3 point)
    {
        //float LineMag = line0.GetDistanceTo(line1); // Magnitude( LineEnd, LineStart );
        float LineMag = Vector3.DistanceSquared(line0, line1);

        float U =
            (((point.X - line0.X) * (line1.X - line0.X)) +
              ((point.Y - line0.Y) * (line1.Y - line0.Y)) +
              ((point.Z - line0.Z) * (line1.Z - line0.Z))) /
            (LineMag * LineMag);

        if (U < 0.0f || U > 1.0f)
            return false;

        float InterX = line0.X + U * (line1.X - line0.X);
        float InterY = line0.Y + U * (line1.Y - line0.Y);
        float InterZ = line0.Z + U * (line1.Z - line0.Z);

        float Distance = Vector3.DistanceSquared(point, new(InterX, InterY, InterZ));
        if (Distance < 0.5f)
            return true;
        return false;
    }

    //////////////////////////////////////////////////////
    // Searching
    //////////////////////////////////////////////////////

    public Spot currentSearchStartSpot;
    public Spot currentSearchSpot;

    // NEW: Updated to use SpotManager for traceback queries
    private float TurnCost(Spot from, Spot to)
    {
        Spot prev = spotManager.GetTraceBack(from);
        if (prev == null)
            return 0.0f;

        return TurnCost(spotManager.GetLocation(prev), spotManager.GetLocation(from), spotManager.GetLocation(to));
    }

    private static float TurnCost(Vector3 p0, Vector3 p1, Vector3 p2)
    {
        Vector3 v1 = Vector3.Normalize(p1 - p0);
        Vector3 v2 = Vector3.Normalize(p2 - p1);

        return Vector3.Distance(v1, v2);
    }

    // return null if failed or the last spot in the path found

    //SearchProgress searchProgress;
    //public SearchProgress SearchProgress
    //{
    //    get
    //    {
    //        return searchProgress;
    //    }
    //}
    private int searchID;

    private const float heuristicsFactor = 5f;

    public Spot ClosestSpot;
    public Spot PeekSpot;

    public readonly HashSet<Vector4> TestPoints = [];
    public readonly HashSet<Vector3> BlockedPoints = [];

    private Spot Search(Spot fromSpot, Spot destinationSpot, SearchStrategy searchScoreSpot, float minHowClose)
    {
        long searchDuration = GetTimestamp();
        long timeSinceProgress = searchDuration;

        float closest = 99999f;
        ClosestSpot = null;

        currentSearchStartSpot = fromSpot;
        searchID++;
        int currentSearchID = searchID;
        //searchProgress = new SearchProgress(fromSpot, destinationSpot, searchID);

        // Initialize SpotManager search (allocates SearchBuffer from pool)
        // NOTE: Caller (CreatePath) is responsible for calling CompleteSearch() after path reconstruction
        spotManager.InitializeSearch(currentSearchID, fromSpot);

        // lowest first queue
        PriorityQueue<Spot, float> prioritySpotQueue = new();
        // Use SpotManager for distance calculation
        prioritySpotQueue.Enqueue(fromSpot, spotManager.GetDistance(fromSpot, destinationSpot) * heuristicsFactor);

        // A* -ish algorithm
        while (prioritySpotQueue.TryDequeue(out currentSearchSpot, out _))
        {
            //if (sleepMSBetweenSpots > 0) { Thread.Sleep(sleepMSBetweenSpots); } // slow down the pathing

            // force the world to be loaded
            // Use SpotManager to get location
            Vector3 currentLoc = spotManager.GetLocation(currentSearchSpot);
            _ = triangleWorld.GetChunkAt(currentLoc.X, currentLoc.Y);

            // Use SpotManager for search state checks
            if (spotManager.SearchIsClosed(currentSearchSpot))
            {
                continue;
            }
            spotManager.SearchClose(currentSearchSpot);

            //update status
            //if (!searchProgress.CheckProgress(currentSearchSpot)) { break; }

            // are we there?

            // Use SpotManager for distance calculations
            float distance = spotManager.GetDistance(currentSearchSpot, destinationSpot);
            float distance2D = spotManager.GetDistance2D(currentSearchSpot, destinationSpot);

            if (distance <= minHowClose || (distance2D <= minHowClose / 2f))
            {
                return currentSearchSpot; // got there
            }

            if (distance < closest)
            {
                // spamming as hell
                //logger.WriteLine($"Closet spot is {distance} from the target");
                closest = distance;
                ClosestSpot = currentSearchSpot;
                PeekSpot = ClosestSpot;
                timeSinceProgress = GetTimestamp();
            }

            if (GetElapsedTime(timeSinceProgress).TotalSeconds > ProgressTimeoutSeconds ||
                GetElapsedTime(searchDuration).TotalSeconds > TimeoutSeconds)
            {
                logger.LogWarning("search failed, {TimeoutSeconds} seconds since last progress, returning the closest spot {ClosestLocation}", ProgressTimeoutSeconds, ClosestSpot.Loc);
                return ClosestSpot;
            }

            //Find spots to link to
            long buildStart = GetTimestamp();
            CreateSpotsAroundSpot(currentSearchSpot, destinationSpot);
            spotManager.AddBuildTicks(GetTimestamp() - buildStart);

            // Timeout check after expensive CreateSpotsAroundSpot
            if (GetElapsedTime(searchDuration).TotalSeconds > TimeoutSeconds)
            {
                logger.LogWarning("search timeout after CreateSpotsAroundSpot, returning closest spot {ClosestLocation}", ClosestSpot?.Loc);
                return ClosestSpot;
            }

            //score each spot around the current search spot and add them to the queue
            ReadOnlySpan<Spot> spots = spotManager.GetPathsToSpots(currentSearchSpot, this);

            for (int i = 0; i < spots.Length; i++)
            {
                Spot linked = spots[i];
                // Use SpotManager for flag and search state checks
                if (linked != null && !spotManager.IsFlagSet(linked, Spot.FLAG_BLOCKED) && !spotManager.SearchIsClosed(linked))
                {
                    ScoreSpot(linked, destinationSpot, searchScoreSpot, currentSearchID, prioritySpotQueue);

                    // Store location with F_Score in W for weighted visualization
                    Vector3 loc = spotManager.GetLocation(linked);
                    TestPoints.Add(new Vector4(loc.X, loc.Y, loc.Z, spotManager.SearchScoreGet(linked)));
                }
            }
        }

        //we ran out of spots to search
        //searchProgress.LogStatus("  search failed. ");

        if (ClosestSpot != null && closest < MaximumAllowedRangeFromTarget)
        {
            logger.LogWarning("search failed, returning the closest spot.");
            return ClosestSpot;
        }

        return null;
    }

    private void ScoreSpot(Spot spotLinkedToCurrent, Spot destinationSpot, SearchStrategy searchScoreSpot, int currentSearchID, PriorityQueue<Spot, float> prioritySpotQueue)
    {
        switch (searchScoreSpot)
        {
            case SearchStrategy.A_Star:
                ScoreSpot_A_Star(spotLinkedToCurrent, destinationSpot, currentSearchID, prioritySpotQueue);
                break;

            case SearchStrategy.A_Star_With_Model_Avoidance:
                ScoreSpot_A_Star_With_Model_And_Gradient_Avoidance(spotLinkedToCurrent, destinationSpot, currentSearchID, prioritySpotQueue);
                break;

            case SearchStrategy.Original:
            default:
                ScoreSpot_Pather(spotLinkedToCurrent, destinationSpot, currentSearchID, prioritySpotQueue);
                break;
        }
    }

    public void ScoreSpot_A_Star(Spot spotLinkedToCurrent, Spot destinationSpot, int currentSearchID, PriorityQueue<Spot, float> prioritySpotQueue)
    {
        //score spot
        // NEW: Use SpotManager for distance and state queries
        float G_Score = spotManager.GetTraceBackDistance(currentSearchSpot) + spotManager.GetDistance(currentSearchSpot, spotLinkedToCurrent);//  the movement cost to move from the starting point A to a given square on the grid, following the path generated to get there.
        float H_Score = spotManager.GetDistance2D(spotLinkedToCurrent, destinationSpot) * heuristicsFactor;// the estimated movement cost to move from that given square on the grid to the final destination, point B. This is often referred to as the heuristic, which can be a bit confusing. The reason why it is called that is because it is a guess. We really don't know the actual distance until we find the path, because all sorts of things can be in the way (walls, water, etc.). You are given one way to calculate H in this tutorial, but there are many others that you can find in other articles on the web.
        float F_Score = G_Score + H_Score;

        if (spotManager.IsFlagSet(spotLinkedToCurrent, Spot.FLAG_WATER)) { F_Score += COST_MOVE_THRU_WATER; }

        // NEW: Use SpotManager for search state management
        if (!spotManager.SearchScoreIsSet(spotLinkedToCurrent) || F_Score < spotManager.SearchScoreGet(spotLinkedToCurrent))
        {
            // shorter path to here found
            spotManager.SetTraceBack(spotLinkedToCurrent, currentSearchSpot);
            spotManager.SetTraceBackDistance(spotLinkedToCurrent, G_Score);
            spotManager.SearchScoreSet(spotLinkedToCurrent, F_Score);
            prioritySpotQueue.Enqueue(spotLinkedToCurrent, F_Score);
        }
    }

    public void ScoreSpot_A_Star_With_Model_And_Gradient_Avoidance(Spot spotLinkedToCurrent, Spot destinationSpot, int currentSearchID, PriorityQueue<Spot, float> prioritySpotQueue)
    {
        //  the movement cost to move from the starting point A to a given square on the grid, following the path generated to get there.
        float G_Score = spotManager.GetTraceBackDistance(currentSearchSpot) + spotManager.GetDistance(currentSearchSpot, spotLinkedToCurrent);
        // the estimated movement cost to move from that given square on the grid to the final destination, point B.
        float H_Score = spotManager.GetDistance2D(spotLinkedToCurrent, destinationSpot) * heuristicsFactor;
        float F_Score = G_Score + H_Score;

        if (spotManager.IsFlagSet(spotLinkedToCurrent, Spot.FLAG_WATER)) { F_Score += COST_MOVE_THRU_WATER; }

        if (!spotManager.TryGetCachedTriangleScore(spotLinkedToCurrent, out int score))
        {
            bool skipCloseness = spotManager.IsFlagSet(spotLinkedToCurrent, Spot.FLAG_ON_OBJECT)
                && !spotManager.IsFlagSet(spotLinkedToCurrent, Spot.FLAG_INDOORS);

            if (skipCloseness)
            {
                // Skip closeness penalty for outdoor object surfaces (bridges, ramps)
                // Indoor corridors (FLAG_ON_OBJECT + FLAG_INDOORS) still get scored
                // Still need gradient score
                Vector3 loc = spotManager.GetLocation(spotLinkedToCurrent);
                score = triangleWorld.GradiantScoreTiered(loc.X, loc.Y, gradiantMax);
            }
            else
            {
                // Combined single-pass: closeness + gradient scoring
                Vector3 loc = spotManager.GetLocation(spotLinkedToCurrent);
                const float ignoreStep = toonHeightHalf - stepDistance;
                const float closenessRange = 7 * WantedStepLength;
                const TriangleType closenessMask = TriangleType.Model | TriangleType.Object;

                (float dist, int gradientScore) = triangleWorld.CombinedScoring(
                    loc.X, loc.Y, loc.Z + ignoreStep,
                    closenessRange, closenessMask, gradiantMax);

                const float closenessMax = 384;
                const float scoringRange = 5 * WantedStepLength;
                int closenessScore = dist >= scoringRange
                    ? 0
                    : (int)(closenessMax * (1f - dist / scoringRange));

                score = closenessScore + gradientScore;
            }

            spotManager.SetCachedTriangleScore(spotLinkedToCurrent, score);
        }

        F_Score += score * 2;

        if (!spotManager.SearchScoreIsSet(spotLinkedToCurrent) || F_Score < spotManager.SearchScoreGet(spotLinkedToCurrent))
        {
            // shorter path to here found
            spotManager.SetTraceBack(spotLinkedToCurrent, currentSearchSpot);
            spotManager.SetTraceBackDistance(spotLinkedToCurrent, G_Score);
            spotManager.SearchScoreSet(spotLinkedToCurrent, F_Score);
            prioritySpotQueue.Enqueue(spotLinkedToCurrent, F_Score);
        }
    }

    public void ScoreSpot_Pather(Spot spotLinkedToCurrent, Spot destinationSpot, int currentSearchID, PriorityQueue<Spot, float> prioritySpotQueue)
    {
        //score spots
        // NEW: Use SpotManager for state queries
        float currentSearchSpotScore = spotManager.SearchScoreGet(currentSearchSpot);
        float linkedSpotScore = 1E30f;
        float new_score = currentSearchSpotScore + spotManager.GetDistance(currentSearchSpot, spotLinkedToCurrent) + TurnCost(currentSearchSpot, spotLinkedToCurrent);

        if (spotManager.IsFlagSet(spotLinkedToCurrent, Spot.FLAG_WATER)) { new_score += COST_MOVE_THRU_WATER; }

        if (spotManager.SearchScoreIsSet(spotLinkedToCurrent))
        {
            linkedSpotScore = spotManager.SearchScoreGet(spotLinkedToCurrent);
        }

        if (new_score < linkedSpotScore)
        {
            // shorter path to here found
            // NEW: Use SpotManager for state management
            spotManager.SetTraceBack(spotLinkedToCurrent, currentSearchSpot);
            spotManager.SearchScoreSet(spotLinkedToCurrent, new_score);
            prioritySpotQueue.Enqueue(spotLinkedToCurrent, (new_score + spotManager.GetDistance(spotLinkedToCurrent, destinationSpot) * heuristicsFactor));
        }
    }

    public void CreateSpotsAroundSpot(Spot currentSearchSpot, Spot destination)
    {
        CreateSpotsAroundSpot(currentSearchSpot, currentSearchSpot.IsFlagSet(Spot.FLAG_MPQ_MAPPED), destination);
    }

    public void CreateSpotsAroundSpot(Spot current, bool mapped, Spot destination)
    {
        if (mapped)
        {
            return;
        }

        //mark as mapped
        current.SetFlag(Spot.FLAG_MPQ_MAPPED, true);

        Vector3 loc = current.Loc;

        // Snap current location to grid for uniform sampling
        SnapToGrid(loc.X, loc.Y, out float gridX, out float gridY);

        // Grid-based neighbor offsets: 8 directions (cardinal + diagonal)
        // Using grid spacing for uniform, predictable spot placement
        ReadOnlySpan<(int dx, int dy)> neighborOffsets =
        [
            (-1, 0), (1, 0), (0, -1), (0, 1),      // Cardinal: West, East, South, North
            (-1, -1), (-1, 1), (1, -1), (1, 1)     // Diagonal: SW, NW, SE, NE
        ];

        bool currentOnObject = current.IsFlagSet(Spot.FLAG_ON_OBJECT);
        bool currentOnWater = current.IsFlagSet(Spot.FLAG_WATER);

        foreach (var (dx, dy) in neighborOffsets)
        {
            // Calculate grid-aligned neighbor position
            float nx = gridX + (dx * SpotGridSize);
            float ny = gridY + (dy * SpotGridSize);

            // Quick check: spot already exists at this exact grid position (cheap hash lookup)
            if (GetSpot(nx, ny, loc.Z) != null)
            {
                continue;
            }

            // check we can stand at this new location
            if (!triangleWorld.FindStandableAt(nx, ny,
                loc.Z - MaxStepLength,
                loc.Z + MaxStepLength,
                out float new_Z, out TriangleType flags, toonHeight, toonSize))
            {
                Vector3 blockedLoc = new(nx, ny, new_Z);
                Spot blockedSpot = new(blockedLoc);
                blockedSpot.SetFlag(Spot.FLAG_BLOCKED, true);
                AddSpot(blockedSpot);

                BlockedPoints.Add(blockedLoc);
                continue;
            }

            // LoS check against Object/Model geometry only (not Terrain/Water)
            // Prevents paths through walls, tree trunks, and under bridges
            // while still allowing normal terrain slopes.
            // Skip when stepping onto an Object/Model surface (bridge, ramp) —
            // the walkable surface's own triangles would falsely block the ray.
            Vector3 neighborLoc = new(nx, ny, new_Z);
            Vector3 avoidSmallBumps = new(0, 0, toonHeightHalf);

            if (!currentOnObject && !currentOnWater &&
                !flags.Has(TriangleType.Object | TriangleType.Model) &&
                !triangleWorld.LineOfSightExists(loc + avoidSmallBumps, neighborLoc + avoidSmallBumps,
                TriangleType.Object | TriangleType.Model))
            {
                continue;
            }

            PeekSpot = new Spot(nx, ny, new_Z);
            if (DelayMs > 0)
                Thread.Sleep(DelayMs / 2);

            const float ignoreStep = toonHeightHalf - stepDistance; //toonHeightQuad;

            if (!currentOnObject && !currentOnWater &&
                !flags.Has(TriangleType.Object | TriangleType.Model) &&
                IsCloseToObjectRange > 0 &&
                triangleWorld.IsCloseToType(nx, ny, new_Z + ignoreStep, IsCloseToObjectRange, TriangleType.Object | TriangleType.Model))
            {
                continue;
            }

            var tempSpot = new Spot(nx, ny, new_Z);
            if (flags.Has(TriangleType.Water))
            {
                tempSpot.SetFlag(Spot.FLAG_WATER, true);
            }

            if (flags.Has(TriangleType.Object))
            {
                tempSpot.SetFlag(Spot.FLAG_ON_OBJECT, true);

                // Standing on Object geometry — raycast up to distinguish
                // bridge (open sky) from building interior (ceiling above)
                Vector3 head = new(nx, ny, new_Z + toonHeight);
                Vector3 sky = new(nx, ny, new_Z + CeilingCheckHeight);
                if (!triangleWorld.LineOfSightExists(head, sky, TriangleType.Object | TriangleType.Model))
                {
                    tempSpot.SetFlag(Spot.FLAG_INDOORS, true);
                }
            }

            Spot newSpot = AddAndConnectSpot(tempSpot);
            if (DelayMs > 0)
                Thread.Sleep(DelayMs / 2);
        }
    }

    private Spot lastCurrentSearchSpot;

    public List<Vector3> CurrentSearchPath()
    {
        if (lastCurrentSearchSpot == currentSearchSpot)
        {
            return [];
        }

        lastCurrentSearchSpot = currentSearchSpot;
        return FollowTraceBackLocations(currentSearchStartSpot, currentSearchSpot);
    }

    // NEW: Updated to use SpotManager for traceback
    private List<Spot> FollowTraceBack(Spot from, Spot to)
    {
        List<Spot> path = [];
        Spot prev = null;
        for (Spot backtrack = to; backtrack != null; backtrack = spotManager.GetTraceBack(backtrack))
        {
            if (prev != null)
            {
                float dist = spotManager.GetDistance(prev, backtrack);
                if (dist > MaxStepLength * 3)
                {
                    logger.LogWarning("Traceback gap detected: {Distance:F1} units between spots at {PrevLoc} and {BacktrackLoc}",
                        dist, spotManager.GetLocation(prev), spotManager.GetLocation(backtrack));
                }
            }
            path.Insert(0, backtrack);
            prev = backtrack;
            if (backtrack == from)
                break;
        }
        return path;
    }

    // NEW: Updated to use SpotManager for traceback and location queries
    private List<Vector3> FollowTraceBackLocations(Spot from, Spot to)
    {
        List<Vector3> path = [];
        for (Spot backtrack = to; backtrack != null; backtrack = spotManager.GetTraceBack(backtrack))
        {
            path.Insert(0, spotManager.GetLocation(backtrack));
            if (backtrack == from)
                break;
        }
        return path;
    }

    private Path CreatePath(Spot from, Spot to, SearchStrategy searchScoreSpot, float minHowClose)
    {
        Spot newTo = Search(from, to, searchScoreSpot, minHowClose);
        try
        {
            if (newTo == null)
                return null;

            // Use SpotManager for distance calculation
            float distance = spotManager.GetDistance(newTo, to);
            if (distance <= MaximumAllowedRangeFromTarget)
            {
                List<Spot> path = FollowTraceBack(from, newTo);
                return new Path(path);
            }

            logger.LogWarning("Closest spot is too far from target. {Distance}>{MaxAllowedRange}", distance, MaximumAllowedRangeFromTarget);
            return null;
        }
        finally
        {
            spotManager.CompleteSearch();
        }
    }

    private Vector3 GetBestLocations(Vector3 location)
    {
        const float zExtendBig = 500;
        const float zExtendSmall = 1;

        float zExtend = zExtendSmall;

        if (location.Z == 0)
        {
            zExtend = zExtendBig;
        }

        float newZ = 0;
        ReadOnlySpan<float> a = [0, 1f, 0.5f, -0.5f, -1f];

        for (int z = 0; z < a.Length; z++)
        {
            for (int x = 0; x < a.Length; x++)
            {
                for (int y = 0; y < a.Length; y++)
                {
                    if (triangleWorld.FindStandableAt(
                        location.X + a[x],
                        location.Y + a[y],
                        location.Z + a[z] - zExtend - WantedStepLength * StepPercent,
                        location.Z + a[z] + zExtend + WantedStepLength * StepPercent,
                        out newZ, out _,
                        toonHeight, toonSize))
                    {
                        goto end;
                    }
                }
            }
        }
    end:
        return new(location.X, location.Y, newZ);
    }

    private void ApplyWallRepulsion(List<Vector3> locations)
    {
        const TriangleType wallMask = TriangleType.Model | TriangleType.Object;
        const float ignoreStep = toonHeightHalf - stepDistance;

        // Two passes: second pass catches waypoints whose neighbors weren't yet repelled
        for (int pass = 0; pass < 2; pass++)
        {
            // Skip first and last waypoints to preserve endpoints
            for (int i = 1; i < locations.Count - 1; i++)
            {
                Vector3 wp = locations[i];

                // Outdoor Object surfaces (bridges, ramps): no walls to repel from,
                // but edges can be dangerous drop-offs. Probe 8 directions for drops
                // and push waypoint toward center of the walkable surface.
                // Indoor corridors (ceiling blocks sky raycast) use normal wall repulsion.
                if (triangleWorld.FindStandableAt(wp.X, wp.Y,
                    wp.Z - toonHeight, wp.Z + toonHeight,
                    out _, out TriangleType standFlags, toonHeight, toonSize) &&
                    standFlags.Has(TriangleType.Object))
                {
                    Vector3 head = new(wp.X, wp.Y, wp.Z + toonHeight);
                    Vector3 sky = new(wp.X, wp.Y, wp.Z + CeilingCheckHeight);
                    if (triangleWorld.LineOfSightExists(head, sky, TriangleType.Object | TriangleType.Model))
                    {
                        // Edge repulsion: probe 8 directions for drop-offs
                        const float probeDistance = 1.5f;
                        const float dropThreshold = MinStepLength;
                        const float edgePushDistance = 1.0f;

                        float repX = 0, repY = 0;
                        int edgeCount = 0;

                        for (int d = 0; d < 8; d++)
                        {
                            float angle = d * (MathF.PI / 4f);
                            float probeX = wp.X + MathF.Cos(angle) * probeDistance;
                            float probeY = wp.Y + MathF.Sin(angle) * probeDistance;

                            bool hasGround = triangleWorld.FindStandableAt(
                                probeX, probeY,
                                wp.Z - MaxStepLength, wp.Z + MaxStepLength,
                                out float probeZ, out _, toonHeight, toonSize);

                            if (!hasGround || (wp.Z - probeZ) > dropThreshold)
                            {
                                repX -= MathF.Cos(angle);
                                repY -= MathF.Sin(angle);
                                edgeCount++;
                            }
                        }

                        if (edgeCount > 0)
                        {
                            float len = MathF.Sqrt(repX * repX + repY * repY);
                            if (len > 0.001f)
                            {
                                repX /= len;
                                repY /= len;

                                float edgeX = wp.X + repX * edgePushDistance;
                                float edgeY = wp.Y + repY * edgePushDistance;

                                if (triangleWorld.FindStandableAt(
                                    edgeX, edgeY,
                                    wp.Z - MaxStepLength, wp.Z + MaxStepLength,
                                    out float edgeZ, out _, toonHeight, toonSize))
                                {
                                    locations[i] = new(edgeX, edgeY, edgeZ);
                                }
                            }
                        }

                        continue;
                    }
                }

                if (!triangleWorld.TryGetWallRepulsion(
                    wp.X, wp.Y, wp.Z + ignoreStep,
                    WallAvoidanceDistance, wallMask,
                    out Vector3 repulsionDir, out float wallDistance))
                {
                    continue;
                }

                // Push away from wall: offset = direction * (desired - actual)
                float pushAmount = WallAvoidanceDistance - wallDistance;
                if (pushAmount <= 0)
                    continue;

                float newX = wp.X + repulsionDir.X * pushAmount;
                float newY = wp.Y + repulsionDir.Y * pushAmount;

                // Validate: must be walkable
                if (!triangleWorld.FindStandableAt(newX, newY,
                    wp.Z - MaxStepLength, wp.Z + MaxStepLength,
                    out float newZ, out _, toonHeight, toonSize))
                {
                    continue;
                }

                locations[i] = new(newX, newY, newZ);
            }
        }
    }

    public Path CreatePath(Vector3 fromLoc, Vector3 toLoc, SearchStrategy searchScoreSpot, float howClose)
    {
        if (logger.IsEnabled(LogLevel.Trace))
            logger.LogTrace("CreatePath from {FromLocation} to {ToLocation}", fromLoc, toLoc);

        long timestamp = GetTimestamp();

        fromLoc = GetBestLocations(fromLoc);
        toLoc = GetBestLocations(toLoc);

        Spot from = FindClosestSpot(fromLoc, MinStepLength);
        Spot to = FindClosestSpot(toLoc, MinStepLength);

        // If spots don't exist, create them and ensure grid connectivity
        if (from == null)
        {
            from = AddAndConnectSpot(new Spot(fromLoc));
            // Create neighbors to integrate into grid (respects FLAG_MPQ_MAPPED)
            CreateSpotsAroundSpot(from, to ?? new Spot(toLoc));
        }

        if (to == null)
        {
            to = AddAndConnectSpot(new Spot(toLoc));
            // Create neighbors to integrate into grid (respects FLAG_MPQ_MAPPED)
            CreateSpotsAroundSpot(to, from);
        }

        Path rawPath = CreatePath(from, to, searchScoreSpot, howClose);

        if (logger.IsEnabled(LogLevel.Trace))
            logger.LogTrace("CreatePath took {ElapsedMs}ms", GetElapsedTime(timestamp).TotalMilliseconds);

        if (rawPath == null)
        {
            return null;
        }
        else
        {
            Vector3 last = rawPath.GetLast;
            if (Vector3.DistanceSquared(last, toLoc) > 1.0)
            {
                rawPath.Add(toLoc);
            }
        }

        if (searchScoreSpot == SearchStrategy.A_Star_With_Model_Avoidance)
        {
            ApplyWallRepulsion(rawPath.locations);
        }

        return rawPath;
    }
}