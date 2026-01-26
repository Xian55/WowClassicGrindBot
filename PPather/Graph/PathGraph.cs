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

    public const float IsCloseToModelRange = toonSize * 2f;

    public const float IsCloseToObjectRange = MinStepLength;

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

    private readonly HashSet<int> generatedChunks;

    private const int maxCache = 512;
    private long LRU;

    public int GetTriangleClosenessScore(Vector3 loc)
    {
        const TriangleType mask = TriangleType.Model | TriangleType.Object;

        const float ignoreStep = toonHeightHalf - stepDistance;

        return !triangleWorld.IsCloseToType(loc.X, loc.Y, loc.Z + ignoreStep, 4 * WantedStepLength, mask)
            ? 0
            : !triangleWorld.IsCloseToType(loc.X, loc.Y, loc.Z + ignoreStep, 2 * WantedStepLength, mask)
            ? 32
            : !triangleWorld.IsCloseToType(loc.X, loc.Y, loc.Z + ignoreStep, 1 * WantedStepLength, mask)
            ? 64
            : 128;
    }

    public int GetTriangleGradiantScore(Vector3 loc, int gradiantMax)
    {
        return triangleWorld.GradiantScore(loc.X, loc.Y, 1) > gradiantMax
            ? 128
            : triangleWorld.GradiantScore(loc.X, loc.Y, 2) > gradiantMax
            ? 64
            : triangleWorld.GradiantScore(loc.X, loc.Y, 3) > gradiantMax
            ? 32
            : 0;
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

        gc = new GraphChunk(base_x, base_y, ix, iy, logger, chunkDir);

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
        GraphChunk gc = LoadChunk(s.Loc.X, s.Loc.Y);
        return gc.AddSpot(s);
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

        ReadOnlySpan<Spot> close = FindAllSpots(s, MaxStepLength);
        for (int i = 0; i < close.Length; i++)
        {
            Spot cs = close[i];
            if (s == cs)
                continue;

            if (cs.IsBlocked() || s.IsBlocked() || (cs.HasPathTo(this, s) && s.HasPathTo(this, cs)))
            {
                continue;
            }

            if (triangleWorld.LineOfSightExists(s.Loc + avoidSmallBumps, cs.Loc + avoidSmallBumps))
            {
                s.AddPathTo(cs);
                cs.AddPathTo(s);
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
                wasAt.AddPathTo(isAtSpot);
                isAtSpot.AddPathTo(wasAt);
            }

            ReadOnlySpan<Spot> sl = FindAllSpots(isAtSpot, MaxStepLength);
            int connected = 0;
            for (int i = 0; i < sl.Length; i++)
            {
                Spot other = sl[i];
                if (other != isAtSpot)
                {
                    other.AddPathTo(isAtSpot);
                    isAtSpot.AddPathTo(other);
                    connected++;
                    // Log("  connect to " + other.location);
                }
            }
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Learned a new spot at " + isAtSpot.Loc + " connected to " + connected + " other spots");
            wasAt = isAtSpot;
        }
        else
        {
            if (wasAt != null && wasAt != isAtSpot)
            {
                // moved to an old spot, make sure they are connected
                wasAt.AddPathTo(isAtSpot);
                isAtSpot.AddPathTo(wasAt);
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

    private static float TurnCost(Spot from, Spot to)
    {
        if (from.traceBack == null)
            return 0.0f;

        Spot prev = from.traceBack;
        return TurnCost(prev.Loc, from.Loc, to.Loc);
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

    public readonly HashSet<Vector3> TestPoints = [];
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

        // lowest first queue
        PriorityQueue<Spot, float> prioritySpotQueue = new();
        prioritySpotQueue.Enqueue(fromSpot, fromSpot.GetDistanceTo(destinationSpot) * heuristicsFactor);

        fromSpot.SearchScoreSet(currentSearchID, 0.0f);
        fromSpot.traceBack = null;
        fromSpot.traceBackDistance = 0;

        // A* -ish algorithm
        while (prioritySpotQueue.TryDequeue(out currentSearchSpot, out _))
        {
            //if (sleepMSBetweenSpots > 0) { Thread.Sleep(sleepMSBetweenSpots); } // slow down the pathing

            // force the world to be loaded
            _ = triangleWorld.GetChunkAt(currentSearchSpot.Loc.X, currentSearchSpot.Loc.Y);

            if (currentSearchSpot.SearchIsClosed(currentSearchID))
            {
                continue;
            }
            currentSearchSpot.SearchClose(currentSearchID);

            //update status
            //if (!searchProgress.CheckProgress(currentSearchSpot)) { break; }

            // are we there?

            //float distance = currentSearchSpot.location.GetDistanceTo(destinationSpot.location);
            float distance = Vector3.Distance(currentSearchSpot.Loc, destinationSpot.Loc);
            float distance2D = Vector2.Distance(currentSearchSpot.Loc.AsVector2(), destinationSpot.Loc.AsVector2());

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
                logger.LogWarning($"search failed, {ProgressTimeoutSeconds} seconds since last progress, returning the closest spot {ClosestSpot.Loc}");
                return ClosestSpot;
            }

            //Find spots to link to
            CreateSpotsAroundSpot(currentSearchSpot, destinationSpot);

            //score each spot around the current search spot and add them to the queue
            ReadOnlySpan<Spot> spots = currentSearchSpot.GetPathsToSpots(this);

            for (int i = 0; i < spots.Length; i++)
            {
                Spot linked = spots[i];
                if (linked != null && !linked.IsBlocked() && !linked.SearchIsClosed(currentSearchID))
                {
                    TestPoints.Add(linked.Loc);

                    ScoreSpot(linked, destinationSpot, searchScoreSpot, currentSearchID, prioritySpotQueue);
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
        float G_Score = currentSearchSpot.traceBackDistance + currentSearchSpot.GetDistanceTo(spotLinkedToCurrent);//  the movement cost to move from the starting point A to a given square on the grid, following the path generated to get there.
        float H_Score = spotLinkedToCurrent.GetDistanceTo2D(destinationSpot) * heuristicsFactor;// the estimated movement cost to move from that given square on the grid to the final destination, point B. This is often referred to as the heuristic, which can be a bit confusing. The reason why it is called that is because it is a guess. We really don�t know the actual distance until we find the path, because all sorts of things can be in the way (walls, water, etc.). You are given one way to calculate H in this tutorial, but there are many others that you can find in other articles on the web.
        float F_Score = G_Score + H_Score;

        if (spotLinkedToCurrent.IsFlagSet(Spot.FLAG_WATER)) { F_Score += COST_MOVE_THRU_WATER; }

        if (!spotLinkedToCurrent.SearchScoreIsSet(currentSearchID) || F_Score < spotLinkedToCurrent.SearchScoreGet(currentSearchID))
        {
            // shorter path to here found
            spotLinkedToCurrent.traceBack = currentSearchSpot;
            spotLinkedToCurrent.traceBackDistance = G_Score;
            spotLinkedToCurrent.SearchScoreSet(currentSearchID, F_Score);
            prioritySpotQueue.Enqueue(spotLinkedToCurrent, F_Score);
        }
    }

    public void ScoreSpot_A_Star_With_Model_And_Gradient_Avoidance(Spot spotLinkedToCurrent, Spot destinationSpot, int currentSearchID, PriorityQueue<Spot, float> prioritySpotQueue)
    {
        //score spot
        //  the movement cost to move from the starting point A to a given square on the grid, following the path generated to get there.
        float G_Score = currentSearchSpot.traceBackDistance + currentSearchSpot.GetDistanceTo(spotLinkedToCurrent);
        // the estimated movement cost to move from that given square on the grid to the final destination, point B.
        // This is often referred to as the heuristic, which can be a bit confusing.
        // The reason why it is called that is because it is a guess.
        // We really dont know the actual distance until we find the path,
        // because all sorts of things can be in the way (walls, water, etc.).
        // You are given one way to calculate H in this tutorial, but there are many others that you can find in other articles on the web.
        float H_Score = spotLinkedToCurrent.GetDistanceTo2D(destinationSpot) * heuristicsFactor;
        float F_Score = G_Score + H_Score;

        if (spotLinkedToCurrent.IsFlagSet(Spot.FLAG_WATER)) { F_Score += COST_MOVE_THRU_WATER; }

        int score = GetTriangleClosenessScore(spotLinkedToCurrent.Loc);

        // Edges have less neighbours
        //int neighbourCount = GetNeighborCount(spotLinkedToCurrent);
        //F_Score += Min(0, 50 * (8 - neighbourCount));

        score += GetTriangleGradiantScore(spotLinkedToCurrent.Loc, gradiantMax);
        F_Score += score * 2;

        if (!spotLinkedToCurrent.SearchScoreIsSet(currentSearchID) || F_Score < spotLinkedToCurrent.SearchScoreGet(currentSearchID))
        {
            // shorter path to here found
            spotLinkedToCurrent.traceBack = currentSearchSpot;
            spotLinkedToCurrent.traceBackDistance = G_Score;
            spotLinkedToCurrent.SearchScoreSet(currentSearchID, F_Score);
            prioritySpotQueue.Enqueue(spotLinkedToCurrent, F_Score);
        }
    }

    public void ScoreSpot_Pather(Spot spotLinkedToCurrent, Spot destinationSpot, int currentSearchID, PriorityQueue<Spot, float> prioritySpotQueue)
    {
        //score spots
        float currentSearchSpotScore = currentSearchSpot.SearchScoreGet(currentSearchID);
        float linkedSpotScore = 1E30f;
        float new_score = currentSearchSpotScore + currentSearchSpot.GetDistanceTo(spotLinkedToCurrent) + TurnCost(currentSearchSpot, spotLinkedToCurrent);

        if (spotLinkedToCurrent.IsFlagSet(Spot.FLAG_WATER)) { new_score += COST_MOVE_THRU_WATER; }

        if (spotLinkedToCurrent.SearchScoreIsSet(currentSearchID))
        {
            linkedSpotScore = spotLinkedToCurrent.SearchScoreGet(currentSearchID);
        }

        if (new_score < linkedSpotScore)
        {
            // shorter path to here found
            spotLinkedToCurrent.traceBack = currentSearchSpot;
            spotLinkedToCurrent.SearchScoreSet(currentSearchID, new_score);
            prioritySpotQueue.Enqueue(spotLinkedToCurrent, (new_score + spotLinkedToCurrent.GetDistanceTo(destinationSpot) * heuristicsFactor));
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

        //Vector3 target = destination.Loc;

        // Calculate the initial angle based on the facing direction
        //float initialAngle = Atan2(target.Y - loc.Y, target.X - loc.X);

        // Loop through the spots in a circle around the current search spot, starting from the facing direction
        //for (float radianAngle = initialAngle; radianAngle < initialAngle + Tau; radianAngle += PI / 8) // 4
        for (float radianAngle = 0; radianAngle < Tau; radianAngle += PI / 8) // 4
        {
            //calculate the location of the spot at the angle
            float nx = loc.X + (Sin(radianAngle) * WantedStepLength);
            float ny = loc.Y + (Cos(radianAngle) * WantedStepLength);

            PeekSpot = new Spot(nx, ny, loc.Z);
            if (DelayMs > 0)
                Thread.Sleep(DelayMs / 2);

            //find the spot at this location, stop if there is one already
            if (GetSpot(nx, ny, loc.Z) != null)
            {
                continue;
            }

            //see if there is a close spot, stop if there is
            if (FindClosestSpot(PeekSpot.Loc, WantedStepLength) != null) //MinStepLength
            {
                continue;
            }

            // check we can stand at this new location
            if (!triangleWorld.FindStandableAt(nx, ny,
                loc.Z - MaxStepLength,
                loc.Z + MaxStepLength,
                out float new_Z, out TriangleType flags, toonHeight, toonSize))
            {
                loc.Z = new_Z;
                Spot blockedSpot = new(loc);
                blockedSpot.SetFlag(Spot.FLAG_BLOCKED, true);
                AddSpot(blockedSpot);

                BlockedPoints.Add(loc);
                continue;
            }

            loc.Z = new_Z;

            const float ignoreStep = toonHeightHalf - stepDistance; //toonHeightQuad;

            if (IsCloseToObjectRange > 0 &&
                triangleWorld.IsCloseToType(nx, ny, loc.Z + ignoreStep, WantedStepLength, TriangleType.Object | TriangleType.Model))
            {
                //loc.Z += toonHeightQuad;
                Spot blockedSpot = new(loc);
                blockedSpot.SetFlag(Spot.FLAG_BLOCKED, true);
                AddSpot(blockedSpot);

                BlockedPoints.Add(loc);
                continue;
            }

            var tempSpot = new Spot(nx, ny, loc.Z);
            if (flags.Has(TriangleType.Water))
            {
                tempSpot.SetFlag(Spot.FLAG_WATER, true);
            }

            if (flags.Has(TriangleType.Object))
            {
                tempSpot.SetFlag(Spot.FLAG_INDOORS, true);
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

    private static List<Spot> FollowTraceBack(Spot from, Spot to)
    {
        List<Spot> path = [];
        for (Spot backtrack = to; backtrack != null; backtrack = backtrack.traceBack)
        {
            path.Insert(0, backtrack);
            if (backtrack == from)
                break;
        }
        return path;
    }

    private static List<Vector3> FollowTraceBackLocations(Spot from, Spot to)
    {
        List<Vector3> path = [];
        for (Spot backtrack = to; backtrack != null; backtrack = backtrack.traceBack)
        {
            path.Insert(0, backtrack.Loc);
            if (backtrack == from)
                break;
        }
        return path;
    }

    private Path CreatePath(Spot from, Spot to, SearchStrategy searchScoreSpot, float minHowClose)
    {
        Spot newTo = Search(from, to, searchScoreSpot, minHowClose);
        if (newTo == null)
            return null;

        float distance = newTo.GetDistanceTo(to);
        if (distance <= MaximumAllowedRangeFromTarget)
        {
            List<Spot> path = FollowTraceBack(from, newTo);
            return new Path(path);
        }

        logger.LogWarning($"Closest spot is too far from target. {distance}>{MaximumAllowedRangeFromTarget}");
        return null;
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

    public Path CreatePath(Vector3 fromLoc, Vector3 toLoc, SearchStrategy searchScoreSpot, float howClose)
    {
        if (logger.IsEnabled(LogLevel.Trace))
            logger.LogTrace($"CreatePath from {fromLoc} to {toLoc}");

        long timestamp = GetTimestamp();

        fromLoc = GetBestLocations(fromLoc);
        toLoc = GetBestLocations(toLoc);

        Spot from = FindClosestSpot(fromLoc, MinStepLength);
        Spot to = FindClosestSpot(toLoc, MinStepLength);

        from ??= AddAndConnectSpot(new Spot(fromLoc));
        to ??= AddAndConnectSpot(new Spot(toLoc));

        Path rawPath = CreatePath(from, to, searchScoreSpot, howClose);

        if (logger.IsEnabled(LogLevel.Trace))
            logger.LogTrace($"CreatePath took {GetElapsedTime(timestamp).TotalMilliseconds}ms");

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
        return rawPath;
    }
}