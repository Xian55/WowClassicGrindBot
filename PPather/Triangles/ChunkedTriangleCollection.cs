/*
 *  Part of PPather
 *  Copyright Pontus Borg 2008
 *
 */

using Microsoft.Extensions.Logging;

using PPather;
using PPather.Graph;
using PPather.Triangles;
using PPather.Triangles.Data;

using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

using Wmo;

using static System.MathF;
using static WowTriangles.Utils;

namespace WowTriangles;

/// <summary>
/// A chunked collection of triangles
/// </summary>
public sealed class ChunkedTriangleCollection
{
    /// <summary>
    /// In World of Warcraft, the maximum slope angle a player can traverse is 60 degrees.
    /// Any terrain steeper than this will cause the player to slide down.
    /// This mechanic prevents players from climbing excessively steep surfaces and helps enforce natural movement limitations within the game world.
    /// </summary>
    private const float MaxStandableAngleDegrees = 51f;
    private const float MaxStandableAngleRadians = MaxStandableAngleDegrees * (PI / 180f);
    private readonly float MaxStandableAngleCos = Cos(MaxStandableAngleRadians);

    private readonly ILogger logger;
    private readonly MPQTriangleSupplier supplier;
    private readonly SparseMatrix2D<TriangleCollection> chunks;

    private const int maxCache = 128;
    public Action<ChunkEventArgs> NotifyChunkAdded;

    public ChunkedTriangleCollection(ILogger logger, int initCapacity, MPQTriangleSupplier supplier)
    {
        this.logger = logger;
        this.supplier = supplier;
        chunks = new SparseMatrix2D<TriangleCollection>(initCapacity);
    }

    public void Close()
    {
        NotifyChunkAdded = null;
        supplier.Clear();
        EvictAll();
    }

    public void EvictAll()
    {
        foreach (TriangleCollection chunk in chunks.GetAllElements())
        {
            chunk.Clear();
        }

        chunks.Clear();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void GetGridStartAt(float x, float y, out int grid_x, out int grid_y)
    {
        x = ChunkReader.ZEROPOINT - x;
        grid_x = (int)(x / ChunkReader.TILESIZE);
        y = ChunkReader.ZEROPOINT - y;
        grid_y = (int)(y / ChunkReader.TILESIZE);
    }

    /// <summary>
    /// Get MCNK-level grid coordinates (256x finer granularity than ADT)
    /// Key format: (adt_x << 24) | (adt_y << 16) | (mcnk_x << 8) | mcnk_y
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void GetMCNKGridCoord(float x, float y, out int grid_x, out int grid_y, out int mcnk_x, out int mcnk_y)
    {
        MCNKHelper.GetMCNKCoord(x, y, out grid_x, out grid_y, out mcnk_x, out mcnk_y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GetGridLimits(int grid_x, int grid_y,
                                out float min_x, out float min_y,
                                out float max_x, out float max_y)
    {
        max_x = ChunkReader.ZEROPOINT - (grid_x * ChunkReader.TILESIZE);
        min_x = max_x - ChunkReader.TILESIZE;
        max_y = ChunkReader.ZEROPOINT - (grid_y * ChunkReader.TILESIZE);
        min_y = max_y - ChunkReader.TILESIZE;
    }

    private TriangleCollection LoadChunkAt(float x, float y)
    {
        GetGridStartAt(x, y, out int grid_x, out int grid_y);

        if (chunks.TryGetValue(grid_x, grid_y, out TriangleCollection r))
        {
            return r;
        }

        GetGridLimits(grid_x, grid_y, out float min_x, out float min_y, out float max_x, out float max_y);

        long startTime = Stopwatch.GetTimestamp();
        TriangleCollection tc = new(logger);
        tc.SetLimits(min_x - 1, min_y - 1, -1E30f, max_x + 1, max_y + 1, 1E30f);

        supplier.GetTriangles(tc, min_x, min_y, max_x, max_y);
        var endTime = Stopwatch.GetElapsedTime(startTime);

        chunks.Add(grid_x, grid_y, tc);

        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("Grid [{GridX},{GridY}] Bounds: [{MinX:F4}, {MinY:F4}] [{MaxX:F4}, {MaxY:F4}] [{X}, {Y}] - Count: {ChunkCount} - Loaded {ElapsedMs}ms",
                grid_x, grid_y, min_x, min_y, max_x, max_y, x, y, chunks.Count, endTime.TotalMilliseconds);
        }
        NotifyChunkAdded?.Invoke(new ChunkEventArgs(grid_x, grid_y));

        return tc;
    }

    public TriangleCollection GetChunkAt(float x, float y)
    {
        return LoadChunkAt(x, y);
    }

    /// <summary>
    /// Load triangles for a specific MCNK chunk (33.33 yards)
    /// This loads only:
    /// - 145 MCVT terrain vertices for this MCNK
    /// - Models/WMOs that intersect this MCNK's bounds
    /// Memory: ~97% reduction compared to loading full ADT
    /// </summary>
    private TriangleCollection LoadMCNKAt(float x, float y)
    {
        GetMCNKGridCoord(x, y, out int adt_x, out int adt_y, out int mcnk_x, out int mcnk_y);

        // Use combined grid coordinate that includes MCNK offsets
        // grid_x_combined = (adt_x * 16) + mcnk_x
        // grid_y_combined = (adt_y * 16) + mcnk_y
        int grid_x_combined = (adt_x * MapTile.SIZE) + mcnk_x;
        int grid_y_combined = (adt_y * MapTile.SIZE) + mcnk_y;

        if (chunks.TryGetValue(grid_x_combined, grid_y_combined, out TriangleCollection cached))
        {
            return cached;
        }

        MCNKHelper.GetMCNKBounds(adt_x, adt_y, mcnk_x, mcnk_y,
            out float min_x, out float min_y, out float max_x, out float max_y);

        long startTime = Stopwatch.GetTimestamp();
        TriangleCollection tc = new(logger);
        tc.SetLimits(min_x - 1, min_y - 1, -1E30f, max_x + 1, max_y + 1, 1E30f);

        // Load only this specific MCNK's geometry
        supplier.GetMCNKTriangles(tc, adt_x, adt_y, mcnk_x, mcnk_y);
        var endTime = Stopwatch.GetElapsedTime(startTime);

        chunks.Add(grid_x_combined, grid_y_combined, tc);

        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace($"MCNK [{adt_x},{adt_y}:{mcnk_x},{mcnk_y}] Bounds: [{min_x:F4}, {min_y:F4}] [{max_x:F4}, {max_y:F4}] - Count: {chunks.Count} - Loaded {endTime.TotalMilliseconds}ms");
        }
        NotifyChunkAdded?.Invoke(new ChunkEventArgs(adt_x, adt_y));

        return tc;
    }

    /// <summary>
    /// Get MCNK-level chunk at world position (new optimized path)
    /// </summary>
    public TriangleCollection GetMCNKAt(float x, float y)
    {
        return LoadMCNKAt(x, y);
    }

    public TriangleCollection GetChunkAt(int grid_x, int grid_y)
    {
        return chunks.TryGetValue(grid_x, grid_y, out TriangleCollection tc)
            ? tc
            : default;
    }

    [SkipLocalsInit]
    public bool IsSpotBlocked(float x, float y, float z,
                              float toonHeight, float toonSize)
    {
        if (z == float.MinValue)
            return true;

        TriangleCollection tc = GetChunkAt(x, y);
        TriangleMatrix tm = tc.GetTriangleMatrix();
        ReadOnlySpan<int> ts = tm.GetAllCloseTo(x, y, toonHeight);

        var tSpan = tc.TrianglesSpan;
        var vSpan = tc.VerteciesSpan;

        Vector3 toon = new(x, y, z + toonHeight);
        float halfSize = toonSize * 0.5f;

        foreach (int index in ts)
        {
            TriangleCollection.GetTriangleVertices(tSpan, vSpan, index,
                out Vector3 v0,
                out Vector3 v1,
                out Vector3 v2,
                out _);

            float d = PointDistanceToTriangle(toon, v0, v1, v2);
            if (d < halfSize)
                return true;
        }

        return false;
    }

    [SkipLocalsInit]
    public bool IsStepBlocked(float x0, float y0, float z0,
                              float x1, float y1, float z1,
                              float toonHeight, float toonSize)
    {
        TriangleCollection tc = GetChunkAt(x0, y0);

        float dx = x0 - x1;
        float dy = y0 - y1;
        float dz = z0 - z1;
        float stepLength = Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        // 1: check steepness

        float cosTheta = (stepLength > 0) ? Abs(dz) / stepLength : 1.0f;
        if (cosTheta > MaxStandableAngleCos)
            return true;

        // 2: check is there is a big step

        float mid_x = (x0 + x1) * 0.5f;
        float mid_y = (y0 + y1) * 0.5f;
        float mid_z = (z0 + z1) * 0.5f;
        float mid_dz = Abs(stepLength);
        if (FindStandableAt(mid_x, mid_y, mid_z - mid_dz, mid_z + mid_dz, out float mid_z_hit, out _, toonHeight, toonSize))
        {
            float dz0 = Abs(z0 - mid_z_hit);
            float dz1 = Abs(z1 - mid_z_hit);

            if ((dz0 > stepLength * 0.75f && dz0 > 1.2f) ||
                (dz1 > stepLength * 0.75f && dz1 > 1.2f))
            {
                return true;
            }
        }
        else
        {
            // bad!
            return true;
        }

        // 3: check collision with objects

        Vector3 from, from_up, from_low;
        Vector3 to, to_up, to_low;

        float halfToonHeight = toonHeight * 0.5f;

        from.X = x0;
        from.Y = y0;
        from.Z = z0 + toonSize + PathGraph.stepDistance;

        to.X = x1;
        to.Y = y1;
        to.Z = z1 + toonSize;

        from_up = new Vector3(from.X, from.Y, z0 + PathGraph.stepDistance / 10f); // small amount
        to_up = new Vector3(to.X, to.Y, z1 + toonHeight);

        TriangleMatrix tm = tc.GetTriangleMatrix();
        ReadOnlySpan<int> ts = tm.GetAllInSquare(Min(x0, x1), Min(y0, y1), Max(x0, x1), Max(y0, y1));

        //diagonal
        if (CheckForCollision(tc, ts, from, to_up))
        {
            return true;
        }

        // this does not work properly!
        //diagonal 
        //if (CheckForCollision(tc, ts, from_up, to))
        //{
        //    return true;
        //}

        //head height
        if (CheckForCollision(tc, ts, from_up, to_up))
        {
            return true;
        }

        from_low = new Vector3(from.X, from.Y, from.Z);
        to_low = new Vector3(to.X, to.Y, to.Z);

        if (CheckForCollision(tc, ts, from_low, to_low, PathGraph.stepDistance))
        {
            return true;
        }

        GetNormal(x0, y0, x1, y1, out float ddx, out float ddy, 0.2f);

        from_low.X += ddy;
        from_low.Y += ddx;
        to_low.X += ddy;
        to_low.Y += ddx;

        if (CheckForCollision(tc, ts, from_low, to_low, PathGraph.stepDistance))
        {
            return true;
        }

        // cause problems with stairs
        //from_low.X -= 2 * ddy;
        //from_low.Y -= 2 * ddx;
        //to_low.X -= 2 * ddy;
        //to_low.Y -= 2 * ddx;

        //if (CheckForCollision(tc, ts, from_low, to_low, PathGraph.stepDistance))
        //{
        //    return true;
        //}

        return false;
    }

    public static void GetNormal(float x1, float y1, float x2, float y2, out float dx, out float dy, float factor)
    {
        dx = x2 - x1;
        dy = y2 - y1;

        if (Abs(dx) > Abs(dy))
        {
            dy /= dx;
            dx = 1;
        }
        else
        {
            dx /= dy;
            dy = 1;
        }

        dx *= factor;
        dy *= factor;
    }

    [SkipLocalsInit]
    private static bool CheckForCollision(TriangleCollection tc, ReadOnlySpan<int> ts, in Vector3 from, in Vector3 to, float stepThreshold = 0)
    {
        float baseZ = Min(from.Z, to.Z);

        var tSpan = tc.TrianglesSpan;
        var vSpan = tc.VerteciesSpan;

        foreach (int index in ts)
        {
            TriangleCollection.GetTriangleVertices(tSpan, vSpan, index,
                out Vector3 v0,
                out Vector3 v1,
                out Vector3 v2,
                out TriangleType flags);

            if (flags is not TriangleType.Water && SegmentTriangleIntersect(from, to, v0, v1, v2, out Vector3 intersect))
            {
                if (stepThreshold != 0)
                {
                    if (intersect.Z > baseZ + stepThreshold)
                        return true;
                }
                else
                    return true;
            }
        }
        return false;
    }

    public bool FindStandableAt(float x, float y, float min_z, float max_z,
                               out float z0, out TriangleType flags, float toonHeight, float toonSize)
    {
        return FindStandableAt1(x, y, min_z, max_z,
            out z0, out flags, toonHeight, toonSize, false,
            TriangleType.Terrain | TriangleType.Water | TriangleType.Model | TriangleType.Object);
    }

    public bool IsInWater(float x, float y, float min_z, float max_z)
    {
        TriangleCollection tc = GetChunkAt(x, y);
        TriangleMatrix tm = tc.GetTriangleMatrix();
        ReadOnlySpan<int> ts = tm.GetAllCloseTo(x, y, 1.0f);

        var tSpan = tc.TrianglesSpan;
        var vSpan = tc.VerteciesSpan;

        Vector3 s0 = new(x, y, min_z);
        Vector3 s1 = new(x, y, max_z);

        foreach (int index in ts)
        {
            TriangleCollection.GetTriangleVertices(tSpan, vSpan, index,
                    out Vector3 v0,
                    out Vector3 v1,
                    out Vector3 v2,
                    out TriangleType t_flags);

            GetTriangleNormal(v0, v1, v2, out _);

            if (SegmentTriangleIntersect(s0, s1, v0, v1, v2, out _) &&
                t_flags.Has(TriangleType.Water))
            {
                return true;
            }
        }
        return false;
    }

    [SkipLocalsInit]
    public int GradiantScore(float x, float y, float range)
    {
        TriangleCollection tc = GetChunkAt(x, y);
        TriangleMatrix tm = tc.GetTriangleMatrix();

        float maxZ = float.MinValue;
        float minZ = float.MaxValue;

        var tSpan = tc.TrianglesSpan;
        var vSpan = tc.VerteciesSpan;

        ReadOnlySpan<int> ts = tm.GetAllCloseTo(x, y, range);
        foreach (int index in ts)
        {
            TriangleCollection.GetTriangleVertices(tSpan, vSpan, index,
                out Vector3 v0,
                out Vector3 v1,
                out Vector3 v2,
                out TriangleType flags);

            if (flags != TriangleType.Terrain)
            {
                continue;
            }

            maxZ = Max4(maxZ, v0.Z, v1.Z, v2.Z);
            minZ = Min4(minZ, v0.Z, v1.Z, v2.Z);
        }
        return (int)(maxZ - minZ);
    }

    [SkipLocalsInit]
    public int GradiantScoreTiered(float x, float y, int gradiantMax)
    {
        TriangleCollection tc = GetChunkAt(x, y);
        TriangleMatrix tm = tc.GetTriangleMatrix();

        var tSpan = tc.TrianglesSpan;
        var vSpan = tc.VerteciesSpan;

        ReadOnlySpan<int> ts = tm.GetAllCloseTo(x, y, 3);

        float maxZ1 = float.MinValue, minZ1 = float.MaxValue;
        float maxZ2 = float.MinValue, minZ2 = float.MaxValue;
        float maxZ3 = float.MinValue, minZ3 = float.MaxValue;

        foreach (int index in ts)
        {
            TriangleCollection.GetTriangleVertices(tSpan, vSpan, index,
                out Vector3 v0,
                out Vector3 v1,
                out Vector3 v2,
                out TriangleType flags);

            if (flags != TriangleType.Terrain)
                continue;

            float triMinX = Min3(v0.X, v1.X, v2.X);
            float triMaxX = Max3(v0.X, v1.X, v2.X);
            float triMinY = Min3(v0.Y, v1.Y, v2.Y);
            float triMaxY = Max3(v0.Y, v1.Y, v2.Y);

            // Tier 3 (range=3) — all triangles qualify (already filtered by GetAllCloseTo)
            maxZ3 = Max4(maxZ3, v0.Z, v1.Z, v2.Z);
            minZ3 = Min4(minZ3, v0.Z, v1.Z, v2.Z);

            // Tier 2 (range=2)
            if (triMaxX >= x - 2 && triMinX <= x + 2 && triMaxY >= y - 2 && triMinY <= y + 2)
            {
                maxZ2 = Max4(maxZ2, v0.Z, v1.Z, v2.Z);
                minZ2 = Min4(minZ2, v0.Z, v1.Z, v2.Z);
            }

            // Tier 1 (range=1)
            if (triMaxX >= x - 1 && triMinX <= x + 1 && triMaxY >= y - 1 && triMinY <= y + 1)
            {
                maxZ1 = Max4(maxZ1, v0.Z, v1.Z, v2.Z);
                minZ1 = Min4(minZ1, v0.Z, v1.Z, v2.Z);
            }
        }

        if ((int)(maxZ1 - minZ1) > gradiantMax) return 128;
        if ((int)(maxZ2 - minZ2) > gradiantMax) return 64;
        if ((int)(maxZ3 - minZ3) > gradiantMax) return 32;
        return 0;
    }

    [SkipLocalsInit]
    public bool IsCloseToType(float x, float y, float z, float range, TriangleType type)
    {
        TriangleCollection tc = GetChunkAt(x, y);
        TriangleMatrix tm = tc.GetTriangleMatrix();

        Vector3 toon = new(x, y, z);
        float halfRange = range * 0.5f;

        var tSpan = tc.TrianglesSpan;
        var vSpan = tc.VerteciesSpan;

        ReadOnlySpan<int> ts = tm.GetAllCloseTo(x, y, range);
        foreach (int index in ts)
        {
            TriangleCollection.GetTriangleVertices(tSpan, vSpan, index,
                out Vector3 v0,
                out Vector3 v1,
                out Vector3 v2,
                out TriangleType flags);

            if (flags.Has(type))
            {
                // ignore the triangle if it is entirely below the toon
                float maxZ = Max3(v0.Z, v1.Z, v2.Z);
                if (maxZ < z)
                    continue;

                float d = PointDistanceToTriangle(toon, v0, v1, v2);
                if (d < halfRange)
                {
                    return true;
                }
            }
        }
        return false;
    }

    [SkipLocalsInit]
    public float ClosestDistanceToType(float x, float y, float z, float range, TriangleType type)
    {
        TriangleCollection tc = GetChunkAt(x, y);
        TriangleMatrix tm = tc.GetTriangleMatrix();

        Vector3 toon = new(x, y, z);

        var tSpan = tc.TrianglesSpan;
        var vSpan = tc.VerteciesSpan;

        float minDist = float.MaxValue;

        ReadOnlySpan<int> ts = tm.GetAllCloseTo(x, y, range);
        foreach (int index in ts)
        {
            TriangleCollection.GetTriangleVertices(tSpan, vSpan, index,
                out Vector3 v0,
                out Vector3 v1,
                out Vector3 v2,
                out TriangleType flags);

            if (!flags.Has(type))
                continue;

            float maxZ = Max3(v0.Z, v1.Z, v2.Z);
            if (maxZ < z)
                continue;

            float d = PointDistanceToTriangle(toon, v0, v1, v2);
            if (d < minDist)
                minDist = d;
        }

        return minDist;
    }

    /// <summary>
    /// Combined closeness distance + gradient scoring in a single triangle query.
    /// Eliminates duplicate GetChunkAt/GetTriangleMatrix/GetAllCloseTo calls
    /// that occur when calling ClosestDistanceToType and GradiantScoreTiered separately.
    /// </summary>
    [SkipLocalsInit]
    public (float closestDist, int gradientScore) CombinedScoring(
        float x, float y, float z,
        float closenessRange, TriangleType closenessMask,
        int gradiantMax)
    {
        TriangleCollection tc = GetChunkAt(x, y);
        TriangleMatrix tm = tc.GetTriangleMatrix();

        var tSpan = tc.TrianglesSpan;
        var vSpan = tc.VerteciesSpan;

        // Query once with the larger range (closenessRange >= 3)
        ReadOnlySpan<int> ts = tm.GetAllCloseTo(x, y, closenessRange);

        Vector3 toon = new(x, y, z);
        float minDist = float.MaxValue;

        float maxZ1 = float.MinValue, minZ1 = float.MaxValue;
        float maxZ2 = float.MinValue, minZ2 = float.MaxValue;
        float maxZ3 = float.MinValue, minZ3 = float.MaxValue;

        foreach (int index in ts)
        {
            TriangleCollection.GetTriangleVertices(tSpan, vSpan, index,
                out Vector3 v0,
                out Vector3 v1,
                out Vector3 v2,
                out TriangleType flags);

            // Closeness scoring (Model|Object triangles)
            if (flags.Has(closenessMask))
            {
                float maxVertZ = Max3(v0.Z, v1.Z, v2.Z);
                if (maxVertZ >= z)
                {
                    // Skip floors/ceilings — only wall-like (mostly vertical) triangles
                    GetTriangleNormal(v0, v1, v2, out Vector3 normal);
                    if (Abs(normal.Z) > 0.7f)
                        continue;

                    float d = PointDistanceToTriangle(toon, v0, v1, v2);
                    if (d < minDist)
                        minDist = d;
                }
            }

            // Gradient scoring (Terrain triangles only, within range 3)
            if (flags == TriangleType.Terrain)
            {
                float triMinX = Min3(v0.X, v1.X, v2.X);
                float triMaxX = Max3(v0.X, v1.X, v2.X);
                float triMinY = Min3(v0.Y, v1.Y, v2.Y);
                float triMaxY = Max3(v0.Y, v1.Y, v2.Y);

                // Only consider triangles within gradient range (3)
                if (triMaxX >= x - 3 && triMinX <= x + 3 && triMaxY >= y - 3 && triMinY <= y + 3)
                {
                    maxZ3 = Max4(maxZ3, v0.Z, v1.Z, v2.Z);
                    minZ3 = Min4(minZ3, v0.Z, v1.Z, v2.Z);

                    if (triMaxX >= x - 2 && triMinX <= x + 2 && triMaxY >= y - 2 && triMinY <= y + 2)
                    {
                        maxZ2 = Max4(maxZ2, v0.Z, v1.Z, v2.Z);
                        minZ2 = Min4(minZ2, v0.Z, v1.Z, v2.Z);
                    }

                    if (triMaxX >= x - 1 && triMinX <= x + 1 && triMaxY >= y - 1 && triMinY <= y + 1)
                    {
                        maxZ1 = Max4(maxZ1, v0.Z, v1.Z, v2.Z);
                        minZ1 = Min4(minZ1, v0.Z, v1.Z, v2.Z);
                    }
                }
            }
        }

        int gradientScore;
        if ((int)(maxZ1 - minZ1) > gradiantMax) gradientScore = 128;
        else if ((int)(maxZ2 - minZ2) > gradiantMax) gradientScore = 64;
        else if ((int)(maxZ3 - minZ3) > gradiantMax) gradientScore = 32;
        else gradientScore = 0;

        return (minDist, gradientScore);
    }

    [SkipLocalsInit]
    public bool LineOfSightExists(Vector3 a, Vector3 b)
    {
        TriangleCollection tc = GetChunkAt(a.X, a.Y);
        TriangleMatrix tm = tc.GetTriangleMatrix();
        ReadOnlySpan<int> ts = tm.GetAllCloseTo(a.X, a.Y, Vector3.Distance(a, b) + 1);

        var tSpan = tc.TrianglesSpan;
        var vSpan = tc.VerteciesSpan;

        Vector3 s0 = a;
        Vector3 s1 = b;

        foreach (int index in ts)
        {
            TriangleCollection.GetTriangleVertices(tSpan, vSpan, index,
                out Vector3 v0,
                out Vector3 v1,
                out Vector3 v2, out _);

            if (SegmentTriangleIntersect(s0, s1, v0, v1, v2, out _))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Check line of sight considering only triangles matching the blocking mask.
    /// Triangles not matching the mask are ignored (transparent to this check).
    /// </summary>
    [SkipLocalsInit]
    public bool LineOfSightExists(Vector3 a, Vector3 b, TriangleType blockingMask)
    {
        TriangleCollection tc = GetChunkAt(a.X, a.Y);
        TriangleMatrix tm = tc.GetTriangleMatrix();
        ReadOnlySpan<int> ts = tm.GetAllCloseTo(a.X, a.Y, Vector3.Distance(a, b) + 1);

        var tSpan = tc.TrianglesSpan;
        var vSpan = tc.VerteciesSpan;

        Vector3 s0 = a;
        Vector3 s1 = b;

        foreach (int index in ts)
        {
            TriangleCollection.GetTriangleVertices(tSpan, vSpan, index,
                out Vector3 v0,
                out Vector3 v1,
                out Vector3 v2,
                out TriangleType flags);

            if (!flags.Has(blockingMask))
                continue;

            if (SegmentTriangleIntersect(s0, s1, v0, v1, v2, out _))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Batch line-of-sight check: performs a single GetAllCloseTo query for the origin,
    /// then checks LOS to multiple targets against the same triangle set.
    /// Avoids redundant dictionary lookups when checking LOS from one origin to many targets.
    /// </summary>
    [SkipLocalsInit]
    public void LineOfSightBatch(
        Vector3 origin,
        ReadOnlySpan<Vector3> targets,
        Span<bool> results,
        float maxRange)
    {
        TriangleCollection tc = GetChunkAt(origin.X, origin.Y);
        TriangleMatrix tm = tc.GetTriangleMatrix();
        ReadOnlySpan<int> ts = tm.GetAllCloseTo(origin.X, origin.Y, maxRange);

        var tSpan = tc.TrianglesSpan;
        var vSpan = tc.VerteciesSpan;

        for (int t = 0; t < targets.Length; t++)
        {
            bool hasLos = true;
            Vector3 target = targets[t];

            foreach (int index in ts)
            {
                TriangleCollection.GetTriangleVertices(tSpan, vSpan, index,
                    out Vector3 v0, out Vector3 v1, out Vector3 v2, out _);

                if (SegmentTriangleIntersect(origin, target, v0, v1, v2, out _))
                {
                    hasLos = false;
                    break;
                }
            }
            results[t] = hasLos;
        }
    }

    [SkipLocalsInit]
    public bool FindStandableAt1(float x, float y, float min_z, float max_z,
                               out float z0, out TriangleType flags,
                               float toonHeight, float toonSize,
                               bool ignoreMaxSlopeAngle, TriangleType allowedFlags)
    {
        TriangleCollection tc = GetChunkAt(x, y);
        TriangleMatrix tm = tc.GetTriangleMatrix();
        ReadOnlySpan<int> ts = tm.GetAllCloseTo(x, y, toonHeight);

        var tSpan = tc.TrianglesSpan;
        var vSpan = tc.VerteciesSpan;

        float hint_z = min_z + (max_z - min_z) * 0.75f; // 75% of the way from min to max

        Vector3 s0 = new(x, y, min_z);
        Vector3 s1 = new(x, y, max_z);

        float best_z = float.MinValue;
        TriangleType best_flags = TriangleType.None;
        float bestDelta = float.MaxValue;

        foreach (int index in ts)
        {
            TriangleCollection.GetTriangleVertices(tSpan, vSpan, index,
                out Vector3 v0,
                out Vector3 v1,
                out Vector3 v2,
                out TriangleType t_flags);

            if (!allowedFlags.Has(t_flags))
            {
                continue;
            }

            GetTriangleNormal(v0, v1, v2, out Vector3 normal);
            if (!ignoreMaxSlopeAngle && normal.Z <= MaxStandableAngleCos)
            {
                continue;
            }

            if (!SegmentTriangleIntersect(s0, s1, v0, v1, v2, out Vector3 intersect))
            {
                continue;
            }

            // Inline IsSpotBlocked check — reuse existing ts/tSpan/vSpan
            Vector3 toon = new(intersect.X, intersect.Y, intersect.Z + toonHeight);
            float halfSize = toonSize * 0.5f;
            bool blocked = false;

            // Only check triangles that could geometrically block at head height
            float blockMinZ = intersect.Z;
            float blockMaxZ = toon.Z + halfSize;

            foreach (int blockIndex in ts)
            {
                if (blockIndex == index)
                    continue;

                TriangleCollection.GetTriangleVertices(tSpan, vSpan, blockIndex,
                    out Vector3 bv0, out Vector3 bv1, out Vector3 bv2, out _);

                // Skip triangles entirely below feet or above head + tolerance
                float triMinZ = Min(bv0.Z, Min(bv1.Z, bv2.Z));
                float triMaxZ = Max(bv0.Z, Max(bv1.Z, bv2.Z));
                if (triMaxZ < blockMinZ || triMinZ > blockMaxZ)
                    continue;

                if (PointDistanceToTriangle(toon, bv0, bv1, bv2) < halfSize)
                {
                    blocked = true;
                    break;
                }
            }

            float delta = Math.Abs(intersect.Z - hint_z);
            if (!blocked && delta <= bestDelta)
            {
                bestDelta = delta;
                best_z = intersect.Z;
                best_flags = t_flags;
            }
        }


        z0 = best_z;
        flags = best_flags;

        const bool nearCliffCheck = false;

        if (nearCliffCheck && best_flags != TriangleType.None)
        {
            Vector3 up, dn;
            up.Z = best_z + toonHeight;
            dn.Z = best_z - toonHeight;

            float minCliffD = toonSize * 0.5f;

            const int size = 4;
            Span<bool> nearCliff = stackalloc bool[size];
            nearCliff.Fill(true);

            ReadOnlySpan<float> dx = [minCliffD, -minCliffD, 0, 0];
            ReadOnlySpan<float> dy = [0, 0, minCliffD, -minCliffD];

            bool allGood;
            foreach (int index in ts)
            {
                TriangleCollection.GetTriangleVertices(tSpan, vSpan, index,
                    out Vector3 v0,
                    out Vector3 v1,
                    out Vector3 v2,
                    out _);

                allGood = true;
                for (int i = 0; i < size; i++)
                {
                    if (nearCliff[i])
                    {
                        up.X = dn.X = x + dx[i];
                        up.Y = dn.Y = y + dy[i];
                        if (SegmentTriangleIntersect(up, dn, v0, v1, v2, out _))
                            nearCliff[i] = false;
                    }
                    allGood &= !nearCliff[i];
                }
                if (allGood)
                    break;
            }

            for (int i = 0; i < size; i++)
            {
                if (nearCliff[i])
                    return false; // too close to cliff
            }
        }

        return best_flags != TriangleType.None;
    }

    [SkipLocalsInit]
    public bool TryGetWallRepulsion(float x, float y, float z, float range, TriangleType type, out Vector3 repulsionDir, out float wallDistance)
    {
        TriangleCollection tc = GetChunkAt(x, y);
        TriangleMatrix tm = tc.GetTriangleMatrix();

        Vector3 toon = new(x, y, z);

        var tSpan = tc.TrianglesSpan;
        var vSpan = tc.VerteciesSpan;

        float minDist = float.MaxValue;
        Vector3 closestPoint = default;

        ReadOnlySpan<int> ts = tm.GetAllCloseTo(x, y, range);
        foreach (int index in ts)
        {
            TriangleCollection.GetTriangleVertices(tSpan, vSpan, index,
                out Vector3 v0,
                out Vector3 v1,
                out Vector3 v2,
                out TriangleType flags);

            if (!flags.Has(type))
                continue;

            float maxZ = Max3(v0.Z, v1.Z, v2.Z);
            if (maxZ < z)
                continue;

            // Skip floors/ceilings — only wall-like (mostly vertical) triangles
            GetTriangleNormal(v0, v1, v2, out Vector3 normal);
            if (Abs(normal.Z) > 0.7f)
                continue;

            Vector3 cp = ClosestPointOnTriangle(toon, v0, v1, v2);
            float d = Vector3.Distance(toon, cp);
            if (d < minDist)
            {
                minDist = d;
                closestPoint = cp;
            }
        }

        if (minDist < range)
        {
            // XY-only repulsion direction (away from wall)
            float dx = x - closestPoint.X;
            float dy = y - closestPoint.Y;
            float len = Sqrt(dx * dx + dy * dy);
            if (len > 1e-6f)
            {
                repulsionDir = new Vector3(dx / len, dy / len, 0);
            }
            else
            {
                repulsionDir = Vector3.UnitX;
            }
            wallDistance = minDist;
            return true;
        }

        repulsionDir = default;
        wallDistance = float.MaxValue;
        return false;
    }

    public (int, float) GetAreaIdAndZ(Vector3 location)
    {
        return supplier.GetAreaIdAndZ(location);
    }
}