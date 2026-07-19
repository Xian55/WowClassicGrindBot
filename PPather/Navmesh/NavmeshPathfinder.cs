#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

using DotRecast.Detour;

using Microsoft.Extensions.Logging;

using PPather.Graph;

using WowTriangles;

namespace PPather.Navmesh;

/// <summary>
/// In-process navmesh pathfinding engine for one continent:
/// ensure tiles -> resolve endpoints -> FindPath -> FindStraightPath ->
/// centripetal Catmull-Rom smoothing -> closest-point-on-poly re-validation.
/// Output shape mirrors the RemoteV3 server (SMOOTH_CATMULLROM|VALIDATE_CPOP)
/// that the WASD follower is already tuned for.
/// </summary>
public sealed class NavmeshPathfinder : IDisposable
{
    public const int MaxPolyPath = 4096;
    public const int MaxStraightPath = 512;
    /// <summary>Partial-result continuation budget (stitching from the furthest reached poly).</summary>
    public const int MaxContinuations = 8;

    private static readonly Vector3 ValidateExtents = new(2f, 4f, 2f);

    private readonly ILogger logger;
    private readonly NavmeshTileCache tiles;
    private readonly DtNavMeshQuery query;
    private readonly WowQueryFilter filter;
    private readonly NavmeshEndpointResolver resolver;

    public NavmeshStats LastStats;

    public struct NavmeshStats
    {
        public double EnsureMs;
        public double ResolveMs;
        public double FindMs;
        public double SmoothMs;
        public int TilesBaked;
        public int PolyPathLength;
        public int PointCount;
    }

    public NavmeshPathfinder(ILogger logger, ChunkedTriangleCollection world, string cacheDir)
    {
        this.logger = logger;
        tiles = new NavmeshTileCache(logger, world, cacheDir);
        query = new DtNavMeshQuery(tiles.NavMesh);
        filter = new WowQueryFilter();
        resolver = new NavmeshEndpointResolver(query, filter);
    }

    public void Dispose()
    {
        tiles.Dispose();
    }

    public Path? FindPath(Vector3 wowFrom, Vector3 wowTo, bool? startIndoors)
    {
        LastStats = default;
        int bakedBefore = tiles.TilesBakedThisSession;

        long t0 = Stopwatch.GetTimestamp();
        tiles.EnsureTilesForSegment(wowFrom, wowTo);
        LastStats.EnsureMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        LastStats.TilesBaked = tiles.TilesBakedThisSession - bakedBefore;

        tiles.Lock.EnterReadLock();
        try
        {
            long t1 = Stopwatch.GetTimestamp();

            if (!resolver.TryResolve(wowFrom, startIndoors, out long startRef, out Vector3 resolvedFrom) ||
                !resolver.TryResolve(wowTo, null, out long endRef, out Vector3 resolvedTo))
            {
                LastStats.ResolveMs = Stopwatch.GetElapsedTime(t1).TotalMilliseconds;
                return null;
            }

            LastStats.ResolveMs = Stopwatch.GetElapsedTime(t1).TotalMilliseconds;

            Vector3 rcStart = NavmeshCoords.ToRc(resolvedFrom);
            Vector3 rcEnd = NavmeshCoords.ToRc(resolvedTo);

            long t2 = Stopwatch.GetTimestamp();
            List<Vector3>? rcPoints = FindRcPath(startRef, endRef, rcStart, rcEnd);
            LastStats.FindMs = Stopwatch.GetElapsedTime(t2).TotalMilliseconds;

            if (rcPoints == null || rcPoints.Count == 0)
            {
                return null;
            }

            long t3 = Stopwatch.GetTimestamp();
            List<Vector3> smoothed = CatmullRom.Smooth(rcPoints);
            ValidateOnMesh(smoothed);
            LastStats.SmoothMs = Stopwatch.GetElapsedTime(t3).TotalMilliseconds;
            LastStats.PointCount = smoothed.Count;

            List<Vector3> wowPoints = new(smoothed.Count);
            for (int i = 0; i < smoothed.Count; i++)
            {
                wowPoints.Add(NavmeshCoords.ToWow(smoothed[i]));
            }

            return new Path(wowPoints);
        }
        finally
        {
            tiles.Lock.ExitReadLock();
        }
    }

    private List<Vector3>? FindRcPath(long startRef, long endRef, Vector3 rcStart, Vector3 rcEnd)
    {
        long[] polyBuffer = ArrayPool<long>.Shared.Rent(MaxPolyPath);
        DtStraightPath[] straightBuffer = ArrayPool<DtStraightPath>.Shared.Rent(MaxStraightPath);
        try
        {
            List<Vector3> points = [];

            long curStartRef = startRef;
            Vector3 curStart = rcStart;

            for (int leg = 0; leg < MaxContinuations; leg++)
            {
                Span<long> polys = polyBuffer.AsSpan(0, MaxPolyPath);

                DtStatus status = query.FindPath(curStartRef, endRef, curStart, rcEnd,
                    filter, polys, out int polyCount, MaxPolyPath);

                if (!status.Succeeded() || polyCount == 0)
                {
                    return points.Count > 0 ? points : null;
                }

                LastStats.PolyPathLength += polyCount;

                // A partial poly path ends short of endRef - clamp the straight
                // path target onto the furthest reached poly, stitch, continue.
                Vector3 legEnd = rcEnd;
                bool partial = status.IsPartial() || polys[polyCount - 1] != endRef;
                if (partial &&
                    query.ClosestPointOnPoly(polys[polyCount - 1], rcEnd, out Vector3 clamped, out _).Succeeded())
                {
                    legEnd = clamped;
                }

                DtStatus spStatus = query.FindStraightPath(curStart, legEnd,
                    polys.Slice(0, polyCount), polyCount,
                    straightBuffer.AsSpan(0, MaxStraightPath), out int straightCount, MaxStraightPath, 0);

                if (!spStatus.Succeeded() || straightCount == 0)
                {
                    return points.Count > 0 ? points : null;
                }

                int skip = points.Count > 0 ? 1 : 0; // dedupe stitch joint
                for (int i = skip; i < straightCount; i++)
                {
                    points.Add(straightBuffer[i].pos);
                }

                if (!partial)
                {
                    return points;
                }

                long furthest = polys[polyCount - 1];
                if (furthest == curStartRef)
                {
                    // No forward progress - unreachable with current tiles.
                    return points;
                }

                curStartRef = furthest;
                curStart = legEnd;
            }

            return points;
        }
        finally
        {
            ArrayPool<long>.Shared.Return(polyBuffer);
            ArrayPool<DtStraightPath>.Shared.Return(straightBuffer);
        }
    }

    /// <summary>
    /// CPOP validation: re-projects every smoothed point onto the mesh so the
    /// spline cannot cut through walls or float off the surface.
    /// </summary>
    private void ValidateOnMesh(List<Vector3> rcPoints)
    {
        for (int i = 0; i < rcPoints.Count; i++)
        {
            DtStatus status = query.FindNearestPoly(rcPoints[i], ValidateExtents, filter,
                out long refs, out Vector3 nearest, out _);

            if (status.Succeeded() && refs != 0)
            {
                rcPoints[i] = nearest;
            }
        }
    }
}
