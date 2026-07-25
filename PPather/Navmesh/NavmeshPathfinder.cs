#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

using DotRecast.Core;
using DotRecast.Detour;

using Microsoft.Extensions.Logging;

using PPather.Graph;

using SharedLib;

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

    /// <summary>
    /// Upper sanity cap, in tiles, on the configured corridor max-tiles
    /// (<see cref="SharedLib.NavmeshQueryOptions.CorridorMaxTiles"/>). Past this
    /// the load cost outweighs any route a wider band could still reveal.
    /// </summary>
    public const int MaxCorridorTileRadiusCap = 16;

    /// <summary>Widen-and-retry escalation factor: each stall doubles the band half-width.</summary>
    private const int CorridorWidenFactor = 2;

    /// <summary>A second pass clears the other edge at convex boundary corners.</summary>
    private const int EdgeMarginMaxIterations = 2;
    /// <summary>Minimum useful push progress in yards; below this a candidate is treated as blocked.</summary>
    private const float SnapEpsilon = 0.05f;
    private const float MarginEpsilon = 1e-3f;
    /// <summary>Minimum edge-push gain, as a fraction of the margin, for a push to be kept.</summary>
    private const float MinEdgePushGainFraction = 0.1f;
    /// <summary>Below this squared length a (should-be unit) wall normal is treated as degenerate.</summary>
    private const float MinValidNormalLengthSq = 0.5f;
    /// <summary>Reject a pushed candidate whose snap landed on a different floor.</summary>
    private const float MaxSnapVerticalDrift = 2f;

    /// <summary>
    /// How far the caller may have moved horizontally for the previously
    /// resolved surface to still describe the floor it is standing on. Beyond
    /// this the position change is a teleport (hearthstone, flight path, death)
    /// rather than walking, and the remembered height means nothing.
    /// </summary>
    public const float ContinuityRadiusYd = 80f;

    /// <summary>
    /// Column surfaces probed for a reachable one after a stalled search. A
    /// probe is a single poly-path query over already-loaded tiles, so the
    /// budget mainly bounds how many stacked floors a column may hide.
    /// </summary>
    public const int MaxRetargetCandidates = 8;

    /// <summary>
    /// Share of the straight-line distance a stalled attempt must have closed
    /// before its start position is taken as correct. Measured at Stormwind: a
    /// search seeded on the canal floor instead of the level above wandered
    /// 560yd and closed 5.6% of a 2692yd route before giving up.
    /// </summary>
    public const float MinHeadwayFraction = 0.15f;

    /// <summary>
    /// How long a widened band and a recorded stall stay valid. The follower
    /// re-requests every few seconds while it walks, and those requests hit the
    /// same tiles and the same geometry; re-deriving both from scratch each
    /// time cost 2.7-4.0s per request in a Wetlands run.
    /// </summary>
    public static readonly TimeSpan StallMemory = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How far the caller may move before a recorded stall stops describing
    /// their situation. Walking this far can open a route the previous position
    /// could not reach.
    /// </summary>
    public const float StallMemoRadiusYd = 150f;

    private static readonly Vector3 ValidateExtents = new(2f, 4f, 2f);

    private readonly ILogger logger;
    private readonly NavmeshTileCache tiles;
    private readonly DtNavMeshQuery query;
    private readonly WowQueryFilter filter;
    private readonly NavmeshEndpointResolver resolver;

    // Query knobs, resolved once from NavmeshQueryOptions in the constructor.
    private readonly int corridorTileRadius;
    private readonly int maxCorridorTileRadius;
    private readonly bool corridorWidenEnabled;
    private readonly bool skipWidenOnShortRoutes;
    private readonly float pathSpacingYd;

    // Surface the last query resolved the caller onto, and where that was.
    // A walking caller stays on it, which settles "which floor" for positions
    // that arrive without a usable height.
    private Vector3? lastResolvedFrom;

    private readonly List<(long PolyRef, Vector3 RcPos)> retargetCandidates = [];

    // Band half-width the last query settled on, and the destination it was
    // settled for. The follower re-requests the same destination as it walks,
    // and a widen it already paid for left those tiles resident - but an
    // unrelated route should not inherit the wider band and load tiles it has
    // no use for.
    private int stickyCorridorRadius;
    private long stickyEndRef;
    private Vector3 stickyFrom;
    private long stickyStamp;

    // Last search that ran out of room, so an identical repeat can answer from
    // what was already learned instead of re-walking the whole escalation.
    private long stalledEndRef;
    private Vector3 stalledFrom;
    private long stalledStamp;

    public NavmeshStats LastStats;

    public NavmeshTileCache Tiles => tiles;

    /// <summary>
    /// Authored road / danger zones applied while searching. Assigning takes
    /// effect on the next query - no rebake, the navmesh itself is unchanged.
    /// </summary>
    public CostZones Zones
    {
        get => filter.Zones;
        set => filter.Zones = value;
    }

    /// <summary>
    /// Radius in yards that interior waypoints may be displaced by, to keep
    /// repeated trips from retracing an identical line. 0 disables it.
    /// </summary>
    public float JitterYards { get; set; }

    /// <summary>
    /// Seed for the displacement. Null draws a fresh non-deterministic seed per
    /// query; a value makes the same route reproduce the same path, which is
    /// what repeatable measurement needs.
    /// </summary>
    public int? JitterSeed { get; set; }

    /// <summary>
    /// Query-time edge margin in yards: interior funnel corners closer than
    /// this to a boundary wall are pushed inward. One-sided by construction -
    /// a push is only kept when it strictly improves the point's own wall
    /// clearance, so corridors are at most centered and tight doorways are
    /// left unchanged. Query-time only: no rebake, not part of the settings
    /// hash. Seeded from <see cref="SharedLib.NavmeshQueryOptions.EdgeMargin"/>;
    /// 0 disables. Settable per-query for a one-off override.
    /// </summary>
    public float EdgeMarginYards { get; set; }

    public struct NavmeshStats
    {
        public double EnsureMs;
        public double ResolveMs;
        public double FindMs;
        public double SmoothMs;
        public int TilesBaked;
        public int PolyPathLength;
        public int PointCount;
        public int PushedPoints;
        /// <summary>Continuation legs the corridor stitcher ran (1 = no stitching needed).</summary>
        public int Legs;
        /// <summary>Yards between the last path point and the resolved destination.</summary>
        public float EndGapYd;
        /// <summary>Yards the resolver moved the requested destination to put it on the mesh.</summary>
        public float ResolveShiftYd;
        /// <summary>Corridor band half-width in tiles the final attempt used.</summary>
        public int CorridorRadius;
        /// <summary>Times the band was widened after a stalled search.</summary>
        public int Widenings;
        /// <summary>Tiles stitched into the mesh when the query finished.</summary>
        public int ResidentTiles;
        /// <summary>The destination was re-aimed at a reachable surface in the same column.</summary>
        public bool Retargeted;
        /// <summary>Height between the requested destination surface and the one used.</summary>
        public float RetargetDropYd;
        /// <summary>The escalation was skipped because this destination stalled moments ago.</summary>
        public bool StallMemoHit;
    }

    // world == null makes a disk-only pathfinder: it answers over already-baked
    // tiles (path/height queries) but never bakes, so it needs no game files.
    public NavmeshPathfinder(ILogger logger, ChunkedTriangleCollection? world, string cacheDir,
        NavmeshBakeOptions bake, NavmeshQueryOptions queryOptions, float? minWorldZ = null)
    {
        this.logger = logger;

        corridorTileRadius = Math.Clamp(queryOptions.CorridorTiles, 1, MaxCorridorTileRadiusCap);
        maxCorridorTileRadius = Math.Clamp(queryOptions.CorridorMaxTiles, 1, MaxCorridorTileRadiusCap);
        corridorWidenEnabled = queryOptions.CorridorWiden;
        skipWidenOnShortRoutes = queryOptions.SkipWidenShortRoutes;
        pathSpacingYd = queryOptions.PathSpacing;
        EdgeMarginYards = queryOptions.EdgeMargin;
        stickyCorridorRadius = corridorTileRadius;

        tiles = new NavmeshTileCache(logger, world, cacheDir, bake, corridorTileRadius, minWorldZ);
        query = new DtNavMeshQuery(tiles.NavMesh);
        filter = new WowQueryFilter();
        resolver = new NavmeshEndpointResolver(query, filter);
    }

    public void Dispose()
    {
        tiles.Dispose();
    }

    /// <summary>
    /// Walkable-surface height at a world (x, y), independent of any z guess:
    /// probes the nearest poly with a tall vertical search box so a z-less query
    /// still snaps to the surface. Loads the covering tile from disk if baked but
    /// never bakes (so it needs no game files); returns false when no navmesh
    /// covers the point.
    /// </summary>
    public bool TryGetHeight(float worldX, float worldY, out float z)
    {
        z = 0f;
        Vector3 wow = new(worldX, worldY, 0f);

        NavmeshCoords.GetTileIndex(worldX, worldY, out int tx, out int tz);
        if (tiles.IsTileOnDisk(tx, tz))
        {
            tiles.EnsureTilesForSegment(wow, wow);
        }

        // Horizontal box tight to the point; vertical box tall enough to span
        // any WoW column so a z=0 query still lands on the surface.
        Vector3 extents = new(8f, 4096f, 8f);
        Vector3 rc = NavmeshCoords.ToRc(wow);

        filter.BeginQuery();
        DtStatus status = query.FindNearestPoly(rc, extents, filter,
            out long refs, out Vector3 nearest, out _);
        if (!status.Succeeded() || refs == 0)
        {
            return false;
        }

        if (!query.ClosestPointOnPoly(refs, rc, out Vector3 closest, out _).Succeeded())
        {
            closest = nearest;
        }

        z = NavmeshCoords.ToWow(closest).Z;
        return true;
    }

    public Path? FindPath(Vector3 wowFrom, Vector3 wowTo, bool? startIndoors)
    {
        LastStats = default;
        filter.BeginQuery();
        int bakedBefore = tiles.TilesBakedThisSession;

        long t0 = Stopwatch.GetTimestamp();
        tiles.EnsureTilesForSegment(wowFrom, wowTo);
        LastStats.EnsureMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        LastStats.TilesBaked = tiles.TilesBakedThisSession - bakedBefore;

        tiles.Lock.EnterReadLock();
        try
        {
            long t1 = Stopwatch.GetTimestamp();

            if (!resolver.TryResolve(wowFrom, startIndoors, out long startRef, out Vector3 resolvedFrom,
                    ContinuityHint(wowFrom)) ||
                !resolver.TryResolve(wowTo, null, out long endRef, out Vector3 resolvedTo))
            {
                LastStats.ResolveMs = Stopwatch.GetElapsedTime(t1).TotalMilliseconds;
                return null;
            }

            lastResolvedFrom = resolvedFrom;

            LastStats.ResolveMs = Stopwatch.GetElapsedTime(t1).TotalMilliseconds;

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "Navmesh resolve: from ({FromX},{FromY},{FromZ}) -> ({RFromX},{RFromY},{RFromZ}) | " +
                    "to ({ToX},{ToY},{ToZ}) -> ({RToX},{RToY},{RToZ})",
                    wowFrom.X, wowFrom.Y, wowFrom.Z, resolvedFrom.X, resolvedFrom.Y, resolvedFrom.Z,
                    wowTo.X, wowTo.Y, wowTo.Z, resolvedTo.X, resolvedTo.Y, resolvedTo.Z);
            }

            Vector3 rcStart = NavmeshCoords.ToRc(resolvedFrom);
            Vector3 rcEnd = NavmeshCoords.ToRc(resolvedTo);

            long t2 = Stopwatch.GetTimestamp();
            List<long> pointRefs = [];
            List<Vector3>? rcPoints = FindRcPath(startRef, endRef, ref rcStart, ref rcEnd, pointRefs);
            LastStats.FindMs = Stopwatch.GetElapsedTime(t2).TotalMilliseconds;

            // A retarget moves an endpoint onto a surface that is actually
            // connected, so the arrival check has to measure against it - and
            // a corrected start is the better memory for the next query.
            resolvedTo = NavmeshCoords.ToWow(rcEnd);
            lastResolvedFrom = NavmeshCoords.ToWow(rcStart);

            if (rcPoints == null || rcPoints.Count == 0)
            {
                return null;
            }

            if (JitterYards > 0f)
            {
                ApplyJitter(rcPoints, pointRefs, JitterYards, JitterSeed);
            }

            // After jitter so a random displacement can never undo the safety
            // margin; before smoothing so the spline curves through the pushed
            // corners.
            if (EdgeMarginYards > 0f)
            {
                ApplyEdgeMargin(rcPoints, pointRefs, EdgeMarginYards);
            }

            long t3 = Stopwatch.GetTimestamp();
            List<Vector3> smoothed = CatmullRom.Smooth(rcPoints, pathSpacingYd);
            ValidateOnMesh(smoothed, EdgeMarginYards);
            LastStats.SmoothMs = Stopwatch.GetElapsedTime(t3).TotalMilliseconds;
            LastStats.PointCount = smoothed.Count;

            List<Vector3> wowPoints = new(smoothed.Count);
            for (int i = 0; i < smoothed.Count; i++)
            {
                wowPoints.Add(NavmeshCoords.ToWow(smoothed[i]));
            }

            // Measured against the resolved destination, not the raw request:
            // callers routinely pass z=0 or a map-coordinate height, and the
            // resolver moves the target onto the mesh before the search starts.
            // Comparing with the request would report that relocation as a
            // truncated path.
            LastStats.EndGapYd = Vector3.Distance(wowPoints[^1], resolvedTo);
            LastStats.ResolveShiftYd = Vector3.Distance(wowTo, resolvedTo);
            LastStats.ResidentTiles = tiles.ResidentTileCount;

            return new Path(wowPoints);
        }
        finally
        {
            tiles.Lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Height to draw a column pick towards: the surface the caller was last
    /// resolved onto, provided they could have walked from there. Null when the
    /// request carries its own height, or after a jump too large to be walking.
    /// </summary>
    private float? ContinuityHint(Vector3 wowFrom)
    {
        if (wowFrom.Z != 0f || !lastResolvedFrom.HasValue)
        {
            return null;
        }

        Vector3 last = lastResolvedFrom.Value;
        float dx = wowFrom.X - last.X;
        float dy = wowFrom.Y - last.Y;

        return (dx * dx) + (dy * dy) <= ContinuityRadiusYd * ContinuityRadiusYd
            ? last.Z
            : null;
    }

    private List<Vector3>? FindRcPath(long startRef, long endRef, ref Vector3 rcStart, ref Vector3 rcEnd,
        List<long> pointRefs)
    {
        long[] polyBuffer = ArrayPool<long>.Shared.Rent(MaxPolyPath);
        DtStraightPath[] straightBuffer = ArrayPool<DtStraightPath>.Shared.Rent(MaxStraightPath);
        long budgetStart = Stopwatch.GetTimestamp();
        try
        {
            List<Vector3> points = [];

            int corridorRadius = StickyRadius(endRef, rcStart);
            LastStats.CorridorRadius = corridorRadius;
            bool retargeted = false;

            // A repeat of a search that just ran out of room: the tiles and the
            // geometry have not changed, so escalating again would spend
            // seconds re-deriving the same frontier.
            bool memoHit = StallRemembered(endRef, rcStart);

            // Widen-and-restart loop. A* over a partially loaded mesh can settle
            // on the poly closest to the goal inside the loaded band and have
            // nowhere better to go - typical where the real route must first
            // lead away from the target (city canal loops, canyon switchbacks).
            // Continuing from that frontier re-searches the same band and lands
            // on the same poly, so the only way out is a wider band.
            while (true)
            {
                bool stalled = FindRcPathLegs(startRef, endRef, rcStart, rcEnd,
                    points, pointRefs, polyBuffer, straightBuffer, budgetStart);

                if (!stalled)
                {
                    RememberRadius(corridorRadius, endRef, rcStart);
                    ForgetStall();
                    break;
                }

                if (memoHit)
                {
                    LastStats.StallMemoHit = true;
                    break;
                }

                // A destination that arrived without a trustworthy height can
                // sit on a roof or ledge stacked over the floor the caller
                // meant, and no amount of loaded tiles connects to it. Re-aim
                // at the nearest surface in the same column the start can
                // actually reach, before spending anything on a wider band.
                if (!retargeted &&
                    TryRetargetReachable(startRef, rcStart, ref rcEnd, ref endRef,
                        polyBuffer, straightBuffer))
                {
                    retargeted = true;
                    points.Clear();
                    pointRefs.Clear();
                    continue;
                }

                // The caller's own end of the guess is the wrong surface: either
                // nothing was reachable at all, or the stall gave up while
                // barely closer to the destination than the start was. A poly
                // network on the wrong floor produces exactly that - it happily
                // wanders hundreds of yards, just never towards the goal.
                if (!retargeted && MadeNoHeadway(points, rcStart, rcEnd) &&
                    TryRetargetStart(endRef, rcEnd, ref rcStart, ref startRef, polyBuffer))
                {
                    retargeted = true;
                    points.Clear();
                    pointRefs.Clear();
                    continue;
                }

                if (!corridorWidenEnabled || corridorRadius >= maxCorridorTileRadius)
                {
                    RememberStall(endRef, rcStart);
                    break;
                }

                // The band spans this far to either side of the straight line.
                // A route shorter than that is already fully enclosed by loaded
                // tiles, so a stall there is geometry, not residency.
                float bandHalfWidthYd = corridorRadius * NavmeshSettings.TileWorldSize;
                if (skipWidenOnShortRoutes &&
                    Vector3.Distance(rcStart, rcEnd) < bandHalfWidthYd)
                {
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LogDebug(
                            "Navmesh: stalled inside a {Band}yd band on a {Len}yd route - not widening",
                            bandHalfWidthYd, Vector3.Distance(rcStart, rcEnd));
                    }
                    RememberStall(endRef, rcStart);
                    break;
                }


                corridorRadius = Math.Min(maxCorridorTileRadius, corridorRadius * CorridorWidenFactor);
                LastStats.CorridorRadius = corridorRadius;
                LastStats.Widenings++;

                TimeSpan remaining = NavmeshTileCache.CorridorWaitBudget
                    - Stopwatch.GetElapsedTime(budgetStart);

                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                tiles.Lock.ExitReadLock();
                try
                {
                    tiles.EnsureTilesForSegment(
                        NavmeshCoords.ToWow(rcStart), NavmeshCoords.ToWow(rcEnd),
                        remaining, corridorRadius);
                }
                finally
                {
                    tiles.Lock.EnterReadLock();
                }

                // Loading the wider band can evict the tiles the endpoints were
                // resolved against, leaving those refs pointing at freed polys.
                if (!query.IsValidPolyRef(startRef, filter) || !query.IsValidPolyRef(endRef, filter))
                {
                    break;
                }

                // The wider band can offer a better route from the very first
                // poly, so the stub found so far is discarded rather than
                // stitched onto - keeping the detour would bake the dead end
                // into the path.
                points.Clear();
                pointRefs.Clear();
            }

            return points.Count > 0 ? points : null;
        }
        finally
        {
            ArrayPool<long>.Shared.Return(polyBuffer);
            ArrayPool<DtStraightPath>.Shared.Return(straightBuffer);
        }
    }

    /// <summary>
    /// Re-aims a destination the search could not reach at another surface in
    /// the same column, nearest the start's own height first, and keeps the
    /// first one a full poly path connects to.
    ///
    /// The client exposes no height, so a destination derived from map
    /// coordinates is a guess: at Kharanos the guess lands on the cliff 66yd
    /// above the inn, which no route reaches. Which of the stacked surfaces the
    /// caller meant is not recoverable from geometry - the walkable one is the
    /// only answer that can be verified.
    /// </summary>
    /// <summary>
    /// Band half-width to open with. A widen that paid off leaves its tiles
    /// resident, so repeating it costs an ensure that finds everything already
    /// present - far cheaper than rediscovering the same stall from the default.
    /// </summary>
    private int StickyRadius(long endRef, Vector3 rcStart)
    {
        if (stickyStamp != 0 && stickyEndRef == endRef &&
            Stopwatch.GetElapsedTime(stickyStamp) < StallMemory &&
            Distance2D(stickyFrom, rcStart) < StallMemoRadiusYd)
        {
            return Math.Max(corridorTileRadius, stickyCorridorRadius);
        }

        return corridorTileRadius;
    }

    private void RememberRadius(int radius, long endRef, Vector3 rcStart)
    {
        stickyCorridorRadius = radius;
        stickyEndRef = endRef;
        stickyFrom = rcStart;
        stickyStamp = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Whether this destination already defeated a full escalation from about
    /// here, recently enough that nothing can have changed. Walking far enough
    /// invalidates it - the route that was out of reach may now be open.
    /// </summary>
    private bool StallRemembered(long endRef, Vector3 rcStart)
    {
        if (stalledStamp == 0 || stalledEndRef != endRef ||
            Stopwatch.GetElapsedTime(stalledStamp) >= StallMemory)
        {
            return false;
        }

        return Distance2D(stalledFrom, rcStart) < StallMemoRadiusYd;
    }

    private void RememberStall(long endRef, Vector3 rcStart)
    {
        stalledEndRef = endRef;
        stalledFrom = rcStart;
        stalledStamp = Stopwatch.GetTimestamp();
    }

    private void ForgetStall()
    {
        stalledStamp = 0;
    }

    /// <summary>
    /// Whether a stalled attempt got meaningfully closer to the destination.
    /// Distance is measured horizontally: a wrong-floor start is often directly
    /// under or over the right one, and counting that height as progress is
    /// exactly the mistake to avoid.
    /// </summary>
    private static bool MadeNoHeadway(List<Vector3> points, Vector3 rcStart, Vector3 rcEnd)
    {
        if (points.Count == 0)
        {
            return true;
        }

        float startGap = Distance2D(rcStart, rcEnd);
        float reachedGap = Distance2D(points[^1], rcEnd);

        return startGap - reachedGap < startGap * MinHeadwayFraction;
    }

    private static float Distance2D(in Vector3 a, in Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return MathF.Sqrt((dx * dx) + (dz * dz));
    }

    /// <summary>
    /// Mirror of <see cref="TryRetargetReachable"/> for the other end: the
    /// caller's own height is a guess too - the client exposes none - so a
    /// start that reaches nothing at all may simply be sitting on the wrong
    /// surface of its column. Only used when the search produced no path,
    /// where the alternative is returning nothing.
    /// </summary>
    private bool TryRetargetStart(long endRef, Vector3 rcEnd, ref Vector3 rcStart, ref long startRef,
        long[] polyBuffer)
    {
        // Ranked by closeness to the destination's height, not to the start's
        // own: the start height is the guess that just failed, and its column
        // neighbours are the rest of the same wrong structure. The other
        // endpoint is the only independent evidence about which floor was
        // meant - the mirror of what the destination retarget does.
        int found = resolver.CollectColumn(rcStart, rcEnd.Y, retargetCandidates);
        if (found <= 1)
        {
            return false;
        }

        Span<long> polys = polyBuffer.AsSpan(0, MaxPolyPath);
        int probes = 0;

        foreach ((long candidateRef, Vector3 candidatePos) in retargetCandidates)
        {
            if (candidateRef == startRef || candidateRef == 0)
            {
                continue;
            }

            if (++probes > MaxRetargetCandidates)
            {
                break;
            }

            DtStatus status = query.FindPath(candidateRef, endRef, candidatePos, rcEnd,
                filter, polys, out int polyCount, MaxPolyPath);

            if (!status.Succeeded() || polyCount == 0 ||
                status.IsPartial() || polys[polyCount - 1] != endRef)
            {
                continue;
            }

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "Navmesh retarget: start moved {Drop}yd to a surface the destination connects to",
                    candidatePos.Y - rcStart.Y);
            }

            LastStats.RetargetDropYd = MathF.Abs(candidatePos.Y - rcStart.Y);
            LastStats.Retargeted = true;
            rcStart = candidatePos;
            startRef = candidateRef;
            return true;
        }

        return false;
    }

    private bool TryRetargetReachable(long startRef, Vector3 rcStart, ref Vector3 rcEnd, ref long endRef,
        long[] polyBuffer, DtStraightPath[] straightBuffer)
    {
        int found = resolver.CollectColumn(rcEnd, rcStart.Y, retargetCandidates);
        if (found <= 1)
        {
            return false;
        }

        Span<long> polys = polyBuffer.AsSpan(0, MaxPolyPath);
        int probes = 0;

        foreach ((long candidateRef, Vector3 candidatePos) in retargetCandidates)
        {
            if (candidateRef == endRef || candidateRef == 0)
            {
                continue;
            }

            if (++probes > MaxRetargetCandidates)
            {
                break;
            }

            DtStatus status = query.FindPath(startRef, candidateRef, rcStart, candidatePos,
                filter, polys, out int polyCount, MaxPolyPath);

            if (!status.Succeeded() || polyCount == 0 ||
                status.IsPartial() || polys[polyCount - 1] != candidateRef)
            {
                continue;
            }

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "Navmesh retarget: destination moved {Drop}yd to a reachable surface at ({X},{Y},{Z})",
                    candidatePos.Y - rcEnd.Y, candidatePos.X, candidatePos.Y, candidatePos.Z);
            }

            LastStats.RetargetDropYd = MathF.Abs(candidatePos.Y - rcEnd.Y);
            LastStats.Retargeted = true;
            rcEnd = candidatePos;
            endRef = candidateRef;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Runs the corridor stitcher over the currently loaded tiles. Returns true
    /// when the search stalled - it reached a frontier poly it cannot improve on
    /// - which is the caller's cue to widen the loaded band and try again.
    /// </summary>
    private bool FindRcPathLegs(long startRef, long endRef, Vector3 rcStart, Vector3 rcEnd,
        List<Vector3> points, List<long> pointRefs,
        long[] polyBuffer, DtStraightPath[] straightBuffer, long budgetStart)
    {
        long curStartRef = startRef;
        Vector3 curStart = rcStart;

        for (int leg = 0; leg < MaxContinuations; leg++)
        {
            // The initial corridor ensure follows the straight from->to
            // line; the real poly route can curve off it. Each leg re-
            // ensures tiles along the remaining segment from the current
            // frontier, spending whatever is left of the corridor budget.
            if (leg > 0)
            {
                TimeSpan remaining = NavmeshTileCache.CorridorWaitBudget
                    - Stopwatch.GetElapsedTime(budgetStart);

                tiles.Lock.ExitReadLock();
                try
                {
                    tiles.EnsureTilesForSegment(
                        NavmeshCoords.ToWow(curStart), NavmeshCoords.ToWow(rcEnd), remaining,
                        LastStats.CorridorRadius);
                }
                finally
                {
                    tiles.Lock.EnterReadLock();
                }
            }

            Span<long> polys = polyBuffer.AsSpan(0, MaxPolyPath);

            DtStatus status = query.FindPath(curStartRef, endRef, curStart, rcEnd,
                filter, polys, out int polyCount, MaxPolyPath);

            LastStats.Legs = leg + 1;

            if (!status.Succeeded() || polyCount == 0)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug(
                        "Navmesh leg {Leg}: FindPath failed - status partial={Partial} outOfNodes={OutOfNodes} polys={PolyCount}",
                        leg, status.IsPartial(), status.Has(DtStatus.DT_OUT_OF_NODES), polyCount);
                }

                return true; // nothing reachable here - a wider band may connect
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

            long furthest = polys[polyCount - 1];

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "Navmesh leg {Leg}: polys={PolyCount} partial={Partial} outOfNodes={OutOfNodes} " +
                    "furthest={Furthest} legEnd=({X},{Y},{Z}) radius={Radius} resident={Resident}",
                    leg, polyCount, partial, status.Has(DtStatus.DT_OUT_OF_NODES), furthest,
                    legEnd.X, legEnd.Y, legEnd.Z, LastStats.CorridorRadius, tiles.ResidentTileCount);
            }

            // Frontier the search cannot improve on: every reachable poly in the
            // loaded band is further from the goal than the one it started this
            // leg on. Stitching again would re-derive the same answer.
            if (partial && furthest == curStartRef)
            {
                return true;
            }

            DtStatus spStatus = query.FindStraightPath(curStart, legEnd,
                polys.Slice(0, polyCount), polyCount,
                straightBuffer.AsSpan(0, MaxStraightPath), out int straightCount, MaxStraightPath, 0);

            if (!spStatus.Succeeded() || straightCount == 0)
            {
                return points.Count == 0;
            }

            int skip = points.Count > 0 ? 1 : 0; // dedupe stitch joint
            for (int i = skip; i < straightCount; i++)
            {
                points.Add(straightBuffer[i].pos);
                pointRefs.Add(straightBuffer[i].refs);
            }

            if (!partial)
            {
                return false; // reached the destination poly
            }

            curStartRef = furthest;
            curStart = legEnd;
        }

        // Continuation budget spent while still short of the destination.
        return true;
    }

    /// <summary>
    /// Displaces each interior waypoint to a random walkable point within
    /// <paramref name="radius"/> of it, so repeated trips over the same route
    /// do not retrace an identical line.
    ///
    /// Detour picks the replacement itself, weighted by polygon area and passed
    /// through the same query filter, so a jittered point is always still on
    /// walkable mesh - unlike a blind offset, which could land inside geometry.
    /// The Within variant is required: FindRandomPointAroundCircle only uses the
    /// radius to limit which polygons are visited and then samples anywhere in
    /// the chosen polygon - on large merged outdoor polys a "0.5yd" jitter could
    /// throw a waypoint tens of yards, turning the route into a zig-zag spiral.
    /// Endpoints are left alone: they come from the resolver and the caller
    /// expects to arrive at them.
    ///
    /// Runs before the Catmull-Rom pass, so the spline curves through the
    /// displaced points and the result reads as wander rather than zig-zag.
    /// </summary>
    private void ApplyJitter(List<Vector3> rcPoints, List<long> pointRefs, float radius, int? seed)
    {
        IRcRand rand = new RcRand(seed.HasValue ? new Random(seed.Value) : new Random());

        int count = Math.Min(rcPoints.Count, pointRefs.Count);

        for (int i = 1; i < count - 1; i++)
        {
            if (pointRefs[i] == 0)
            {
                continue;
            }

            DtStatus status = query.FindRandomPointWithinCircle(pointRefs[i], rcPoints[i],
                radius, filter, rand, out long randomRef, out Vector3 randomPt);

            if (status.Succeeded() && randomRef != 0)
            {
                rcPoints[i] = randomPt;
                pointRefs[i] = randomRef;
            }
        }
    }

    /// <summary>
    /// CPOP validation: re-projects smoothed interior points onto the mesh so
    /// the spline cannot cut through walls or float off the surface. Endpoints
    /// are excluded - they come straight from the resolver (already on-poly)
    /// and re-projection can drift them past the consumer's arrival radius.
    ///
    /// A point the snap displaced horizontally was off-mesh - a spline bow
    /// that cut a corner - and the snap parks it exactly on the boundary
    /// edge, recreating the cliff hug the corner push removed. Those points
    /// (and only those; on-mesh points get pure vertical detail-height
    /// correction) get the margin re-applied.
    /// </summary>
    private void ValidateOnMesh(List<Vector3> rcPoints, float margin)
    {
        for (int i = 1; i < rcPoints.Count - 1; i++)
        {
            DtStatus status = query.FindNearestPoly(rcPoints[i], ValidateExtents, filter,
                out long refs, out Vector3 nearest, out _);

            if (!status.Succeeded() || refs == 0)
            {
                continue;
            }

            float dx = nearest.X - rcPoints[i].X;
            float dz = nearest.Z - rcPoints[i].Z;
            rcPoints[i] = nearest;

            if (margin > 0f && (dx * dx) + (dz * dz) > SnapEpsilon * SnapEpsilon &&
                TryPushFromEdge(refs, nearest, margin, out Vector3 pushed, out _))
            {
                rcPoints[i] = pushed;
                LastStats.PushedPoints++;
            }
        }
    }

    /// <summary>
    /// Pushes interior funnel corners away from boundary walls. The funnel
    /// algorithm lands corners exactly on (eroded) boundary polygon vertices,
    /// so on a cliff lip the path runs along the drop - a small deviation from
    /// the spline sends the follower over the edge. Endpoints never move
    /// (arrival semantics, resolver-owned). Up to two passes per corner: at a
    /// convex boundary corner the first push clears one edge, the second
    /// clears the other, converging toward the diagonal.
    /// </summary>
    private void ApplyEdgeMargin(List<Vector3> rcPoints, List<long> pointRefs, float margin)
    {
        int count = Math.Min(rcPoints.Count, pointRefs.Count);

        for (int i = 1; i < count - 1; i++)
        {
            if (pointRefs[i] == 0)
            {
                continue;
            }

            Vector3 p = rcPoints[i];
            long r = pointRefs[i];
            bool moved = false;

            for (int iter = 0; iter < EdgeMarginMaxIterations; iter++)
            {
                if (!TryPushFromEdge(r, p, margin, out Vector3 pushed, out long pushedRef))
                {
                    break;
                }

                p = pushed;
                r = pushedRef;
                moved = true;
            }

            if (moved)
            {
                rcPoints[i] = p;
                pointRefs[i] = r;
                LastStats.PushedPoints++;
            }
        }
    }

    /// <summary>
    /// Pushes a point inward when a boundary wall sits closer than
    /// <paramref name="margin"/> - but only when doing so genuinely improves
    /// its clearance. One-sided by construction: a cliff edge (wall on one
    /// side) gets the full margin, a corridor is at most centered between its
    /// walls, and a tight doorway whose point is already near the centerline
    /// is left unchanged - the push must beat the current clearance by a
    /// minimum gain or it is discarded.
    /// </summary>
    private bool TryPushFromEdge(long startRef, in Vector3 point, float margin,
        out Vector3 pushed, out long pushedRef)
    {
        pushed = point;
        pushedRef = startRef;

        // No wall within the radius reports hitDist == maxRadius, so one
        // branch covers both "clear" and "nothing found".
        DtStatus status = query.FindDistanceToWall(startRef, point, margin, filter,
            out float d0, out _, out Vector3 normal);

        if (!status.Succeeded() || d0 >= margin - MarginEpsilon)
        {
            return false;
        }

        // Degenerate zero-length wall edge normalizes to NaN.
        if (!float.IsFinite(normal.X) || !float.IsFinite(normal.Z) ||
            normal.LengthSquared() < MinValidNormalLengthSq)
        {
            return false;
        }

        float minGain = MathF.Max(SnapEpsilon, MinEdgePushGainFraction * margin);

        // Full push along the wall normal. The normal's sign follows polygon
        // winding; when the first direction makes no on-mesh progress (snap
        // clamps the candidate straight back), retry the opposite direction
        // so the pass is winding-proof.
        Vector3 s1 = default;
        long ref1 = 0;
        float pushEff = 0f;

        for (int attempt = 0; ; attempt++)
        {
            Vector3 candidate = point + (normal * (margin - d0));

            if (TrySnap(candidate, out ref1, out s1))
            {
                pushEff = ((s1.X - point.X) * normal.X) + ((s1.Z - point.Z) * normal.Z);
                if (pushEff >= SnapEpsilon)
                {
                    break;
                }
            }

            if (attempt == 1)
            {
                return false; // both directions blocked outright
            }

            normal = -normal;
        }

        status = query.FindDistanceToWall(ref1, s1, margin, filter,
            out float d1, out _, out _);

        if (!status.Succeeded())
        {
            return false;
        }

        if (d1 >= margin - MarginEpsilon)
        {
            // One-sided case (cliff edge): the push fully cleared the wall.
            pushed = s1;
            pushedRef = ref1;
            return true;
        }

        // The candidate is still wall-constrained: a second wall lies along
        // the push axis. point sits d0 from wall A; s1 sits (d0 + pushEff)
        // from A and d1 from the nearest wall B, so the axis corridor width
        // is ~ d0 + pushEff + d1 and its center is (pushEff + d1 - d0) / 2
        // beyond the original point.
        Vector3 best = point;
        long bestRef = startRef;
        float bestClear = d0;

        if (d1 > bestClear)
        {
            best = s1;
            bestRef = ref1;
            bestClear = d1;
        }

        float pushMid = (pushEff + d1 - d0) * 0.5f;
        if (pushMid > SnapEpsilon &&
            TrySnap(point + (normal * pushMid), out long ref2, out Vector3 s2) &&
            query.FindDistanceToWall(ref2, s2, margin, filter,
                out float d2, out _, out _).Succeeded() &&
            d2 > bestClear)
        {
            best = s2;
            bestRef = ref2;
            bestClear = d2;
        }

        if (bestClear > d0 + minGain)
        {
            // Corridor: at most centered between the two walls.
            pushed = best;
            pushedRef = bestRef;
            return true;
        }

        return false; // tight doorway / already centered: leave unchanged
    }

    /// <summary>
    /// Re-seats a pushed candidate on the mesh. The vertical guard rejects a
    /// snap that landed on a different floor near multi-level geometry.
    /// </summary>
    private bool TrySnap(in Vector3 candidate, out long snappedRef, out Vector3 snapped)
    {
        DtStatus status = query.FindNearestPoly(candidate, ValidateExtents, filter,
            out snappedRef, out snapped, out _);

        return status.Succeeded() && snappedRef != 0 &&
            MathF.Abs(snapped.Y - candidate.Y) <= MaxSnapVerticalDrift;
    }
}
