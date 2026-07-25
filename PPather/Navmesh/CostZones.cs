#nullable enable
using System;
using System.Collections.Frozen;
using System.Collections.Generic;

using PPather.Graph;
using PPather.Triangles;

using WowTriangles;

using Wmo;

namespace PPather.Navmesh;

/// <summary>
/// Authored routing preferences rasterized onto the 33.33yd MCNK grid so the
/// Detour query filter can price a polygon with a single hash lookup.
///
/// Roads pull routes in (cost multiplier below 1), danger zones push them away
/// (multiplier above 1) or forbid them outright. This is a query-time concern
/// on purpose: editing a zone takes effect on the next path with no rebake and
/// no navmesh cache invalidation.
/// </summary>
public sealed class CostZones
{
    /// <summary>Empty set - no roads, no danger zones, every lookup neutral.</summary>
    public static readonly CostZones Empty = new(FrozenDictionary<long, float>.Empty, FrozenSet<long>.Empty, 1f);

    private readonly FrozenDictionary<long, float> costByChunk;
    private readonly FrozenSet<long> blockedChunks;

    /// <summary>Cost for a chunk with nothing authored on it. See <see cref="OffRoadFactor"/>.</summary>
    private readonly float defaultFactor;

    public bool IsEmpty => costByChunk.Count == 0 && blockedChunks.Count == 0;
    public int CostChunkCount => costByChunk.Count;
    public int BlockedChunkCount => blockedChunks.Count;

    private CostZones(FrozenDictionary<long, float> costByChunk, FrozenSet<long> blockedChunks,
        float defaultFactor)
    {
        this.costByChunk = costByChunk;
        this.blockedChunks = blockedChunks;
        this.defaultFactor = defaultFactor;
    }

    /// <summary>Packs MCNK coordinates into a single lookup key.</summary>
    public static long ChunkKey(float worldX, float worldY)
    {
        MCNKHelper.GetMCNKCoord(worldX, worldY, out int adtX, out int adtY, out int mcnkX, out int mcnkY);
        return ((long)adtX << 24) | ((long)adtY << 16) | ((long)mcnkX << 8) | (uint)mcnkY;
    }

    public bool IsBlocked(float worldX, float worldY)
    {
        return blockedChunks.Count != 0 && blockedChunks.Contains(ChunkKey(worldX, worldY));
    }

    /// <summary>Cost multiplier for a position; 1 where nothing was authored.</summary>
    public float CostFactor(float worldX, float worldY)
    {
        if (costByChunk.Count == 0)
        {
            return 1f;
        }

        return costByChunk.TryGetValue(ChunkKey(worldX, worldY), out float factor) ? factor : defaultFactor;
    }

    /// <summary>
    /// Rasterizes roads and danger zones for one continent. Danger wins over
    /// road where they overlap, and the strongest penalty wins where zones do.
    /// </summary>
    public static CostZones Build(IEnumerable<RoadData> roads, IEnumerable<DangerZoneData> dangerZones,
        float roadFactor = DefaultRoadFactor, float roadCoreHalfWidth = DefaultRoadCoreHalfWidth)
    {
        Dictionary<long, float> cost = [];
        HashSet<long> blocked = [];

        // A road is the 1.0 baseline and everything else is dearer by the road
        // preference, so no chunk is ever cheaper than Detour's heuristic.
        float offRoad = 1f / roadFactor;

        foreach (RoadData road in roads)
        {
            foreach (RoadSegment segment in road.Roads)
            {
                RasterizeRoad(segment, offRoad, cost, roadCoreHalfWidth);
            }
        }

        foreach (DangerZoneData data in dangerZones)
        {
            foreach (CircleDangerZone circle in data.Circles)
            {
                RasterizeBox(circle.CenterX - circle.Radius, circle.CenterY - circle.Radius,
                    circle.CenterX + circle.Radius, circle.CenterY + circle.Radius,
                    circle.Penalty, circle.Mode, offRoad, cost, blocked,
                    (x, y) => Within(x, y, circle));
            }

            foreach (RectangleDangerZone rect in data.Rectangles)
            {
                RasterizeBox(rect.MinX, rect.MinY, rect.MaxX, rect.MaxY,
                    rect.Penalty, rect.Mode, offRoad, cost, blocked, static (_, _) => true);
            }
        }

        return new CostZones(cost.ToFrozenDictionary(), blocked.ToFrozenSet(), offRoad);
    }

    /// <summary>
    /// How much cheaper a road is than open ground. 0.6 means a road costs 60%
    /// of what the same distance off-road costs.
    /// </summary>
    public const float DefaultRoadFactor = 0.6f;

    /// <summary>
    /// Ceiling on an authored penalty so one zone cannot swamp the search.
    /// </summary>
    public const float MaxPenaltyFactor = 25f;

    /// <summary>
    /// Cost multiplier for a chunk with nothing authored on it.
    ///
    /// Detour's A* heuristic is <c>Distance(node, goal) * H_SCALE</c> with
    /// H_SCALE = 0.999, which assumes no traversal is ever cheaper than ~1.0
    /// per unit distance. Pricing roads below 1.0 makes that heuristic
    /// overestimate the remaining cost, the search stops early and long routes
    /// come back truncated - Moonbrook to Goldshire reached 628yd of 2417yd.
    ///
    /// So the road preference is expressed the other way round: a road is the
    /// 1.0 baseline and open ground is dearer by the same ratio. The relative
    /// preference is identical, every cost stays at or above the heuristic, and
    /// long routes complete.
    /// </summary>
    public const float OffRoadFactor = 1f / DefaultRoadFactor;

    private static bool Within(float x, float y, in CircleDangerZone circle)
    {
        float dx = x - circle.CenterX;
        float dy = y - circle.CenterY;
        return (dx * dx) + (dy * dy) <= circle.Radius * circle.Radius;
    }

    /// <summary>
    /// Default distance from the centerline that still routes at full road cost -
    /// the carriageway itself. Beyond it the cost ramps back up to open ground;
    /// at or above the authored half-width it restores the old flat band.
    /// Configurable via <see cref="SharedLib.NavmeshQueryOptions.RoadCore"/>.
    /// </summary>
    public const float DefaultRoadCoreHalfWidth = 8f;

    /// <summary>
    /// Cost multiplier at distance <paramref name="d"/> from a road centerline.
    ///
    /// A flat "inside the band / outside the band" mask gives the search nothing
    /// to follow: every chunk of a 133yd-wide band costs the same, so a route
    /// wanders anywhere inside it, and off-road terrain is uniformly priced so
    /// there is no signal pointing at the road at all. Ramping the cost with
    /// distance turns the road into an attractor - the gradient both pulls a
    /// route in from open ground and holds it near the centerline once there.
    /// </summary>
    private static float RoadFactor(float d, float influence, float offRoad, float roadCoreHalfWidth)
    {
        if (d <= roadCoreHalfWidth)
        {
            return 1f;
        }

        if (d >= influence)
        {
            return offRoad;
        }

        float t = (d - roadCoreHalfWidth) / (influence - roadCoreHalfWidth);

        // Smoothstep, so joining and leaving a road has no cost cliff.
        t = t * t * (3f - (2f * t));

        return 1f + ((offRoad - 1f) * t);
    }

    private static void RasterizeRoad(in RoadSegment segment, float offRoad, Dictionary<long, float> cost,
        float roadCoreHalfWidth)
    {
        ReadOnlySpan<System.Numerics.Vector2> pts = segment.Points;
        if (pts.Length == 0)
        {
            return;
        }

        // The authored half-width becomes the reach of the attraction rather
        // than the width of a plateau.
        float influence = MathF.Max(segment.HalfWidthWorld, roadCoreHalfWidth + ChunkReader.CHUNKSIZE);

        if (pts.Length == 1)
        {
            MarkLeg(pts[0].X, pts[0].Y, pts[0].X, pts[0].Y, influence, offRoad, cost, roadCoreHalfWidth);
            return;
        }

        for (int i = 0; i + 1 < pts.Length; i++)
        {
            MarkLeg(pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y, influence, offRoad, cost, roadCoreHalfWidth);
        }
    }

    /// <summary>
    /// Prices every chunk within reach of one road leg by its true distance to
    /// that leg, keeping the cheapest where legs or roads overlap.
    /// </summary>
    private static void MarkLeg(float ax, float ay, float bx, float by,
        float influence, float offRoad, Dictionary<long, float> cost, float roadCoreHalfWidth)
    {
        float step = ChunkReader.CHUNKSIZE * 0.5f;

        float minX = MathF.Min(ax, bx) - influence;
        float maxX = MathF.Max(ax, bx) + influence;
        float minY = MathF.Min(ay, by) - influence;
        float maxY = MathF.Max(ay, by) + influence;

        for (float x = minX; x <= maxX; x += step)
        {
            for (float y = minY; y <= maxY; y += step)
            {
                float d = MathF.Sqrt(PointToSegmentDistanceSq(x, y, ax, ay, bx, by));
                if (d >= influence)
                {
                    continue;
                }

                float factor = RoadFactor(d, influence, offRoad, roadCoreHalfWidth);

                long key = ChunkKey(x, y);
                // Cheapest wins, so overlapping roads do not compound.
                if (!cost.TryGetValue(key, out float existing) || factor < existing)
                {
                    cost[key] = factor;
                }
            }
        }
    }

    private static float PointToSegmentDistanceSq(float px, float py,
        float ax, float ay, float bx, float by)
    {
        float abx = bx - ax;
        float aby = by - ay;
        float lenSq = (abx * abx) + (aby * aby);

        float t = lenSq > 0f
            ? Math.Clamp((((px - ax) * abx) + ((py - ay) * aby)) / lenSq, 0f, 1f)
            : 0f;

        float dx = px - (ax + (t * abx));
        float dy = py - (ay + (t * aby));
        return (dx * dx) + (dy * dy);
    }

    private static void RasterizeBox(float minX, float minY, float maxX, float maxY,
        float penalty, DangerZoneMode mode, float offRoad,
        Dictionary<long, float> cost, HashSet<long> blocked,
        Func<float, float, bool> contains)
    {
        float step = ChunkReader.CHUNKSIZE * 0.5f;

        // Authored penalties come from the spot-A* era where they were additive
        // costs; treat them as a multiplier over the off-road baseline and clamp
        // so a single zone cannot dominate the whole search. Staying at or above
        // that baseline keeps every cost admissible for Detour's heuristic.
        float factor = Math.Clamp(offRoad * (1f + (penalty / 100f)), offRoad, MaxPenaltyFactor);

        for (float x = minX; x <= maxX; x += step)
        {
            for (float y = minY; y <= maxY; y += step)
            {
                if (!contains(x, y))
                {
                    continue;
                }

                long key = ChunkKey(x, y);

                if (mode == DangerZoneMode.Block)
                {
                    blocked.Add(key);
                    continue;
                }

                // Danger overrides road preference, and the worst penalty wins.
                if (!cost.TryGetValue(key, out float existing) || factor > existing)
                {
                    cost[key] = factor;
                }
            }
        }
    }
}
