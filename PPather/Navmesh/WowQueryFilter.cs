#nullable enable
using System.Collections.Generic;
using System.Numerics;

using DotRecast.Detour;

namespace PPather.Navmesh;

/// <summary>
/// Query filter with TrinityCore/AmeisenNavigation semantics:
/// include GROUND | WATER | MAGMA_SLIME, area costs GROUND 1.0 / WATER 1.3 /
/// MAGMA_SLIME 4.0, plus the authored road/danger zones from
/// <see cref="CostZones"/>.
/// </summary>
public sealed class WowQueryFilter : IDtQueryFilter
{
    public const int IncludeFlags =
        NavmeshSettings.FlagGround | NavmeshSettings.FlagWater | NavmeshSettings.FlagMagmaSlime;

    public const float CostGround = 1.0f;
    public const float CostWater = 1.3f;
    public const float CostMagmaSlime = 4.0f;

    /// <summary>Per-area cost table size: a Detour poly area is a 6-bit field, so 64 values.</summary>
    public const int AreaCostTableSize = 64;

    private readonly float[] areaCost;

    /// <summary>
    /// Authored routing preferences. Replaceable at runtime so editing a zone
    /// takes effect on the next query without a rebake.
    ///
    /// Written from arbitrary threads - the file watcher reloads on a timer
    /// thread - so a query never reads this directly. <see cref="BeginQuery"/>
    /// snapshots it into <see cref="active"/>, which keeps one path costed
    /// against one consistent set of zones even if an edit lands mid-search.
    /// CostZones itself is immutable, so the swap cannot tear.
    /// </summary>
    public CostZones Zones { get; set; } = CostZones.Empty;

    /// <summary>The zone set this query is being costed against.</summary>
    private CostZones active = CostZones.Empty;

    /// <summary>
    /// polyRef -> zone factor, valid for one query. Poly centroids never move,
    /// but the cache is cleared per query so zone edits are picked up.
    /// </summary>
    private readonly Dictionary<long, float> factorCache = [];
    private readonly Dictionary<long, bool> blockedCache = [];

    public WowQueryFilter()
    {
        areaCost = new float[AreaCostTableSize];
        for (int i = 0; i < areaCost.Length; i++)
        {
            areaCost[i] = CostGround;
        }

        areaCost[NavmeshSettings.AreaWater] = CostWater;
        areaCost[NavmeshSettings.AreaMagmaSlime] = CostMagmaSlime;
    }

    /// <summary>Drops the per-query memo; call before each path request.</summary>
    public void BeginQuery()
    {
        active = Zones;

        if (factorCache.Count != 0)
        {
            factorCache.Clear();
        }

        if (blockedCache.Count != 0)
        {
            blockedCache.Clear();
        }
    }

    public bool PassFilter(long refs, DtMeshTile tile, DtPoly poly)
    {
        if ((poly.flags & IncludeFlags) == 0)
        {
            return false;
        }

        if (active.BlockedChunkCount == 0)
        {
            return true;
        }

        if (blockedCache.TryGetValue(refs, out bool blocked))
        {
            return !blocked;
        }

        blocked = TryGetCentroid(tile, poly, out float wx, out float wy) && active.IsBlocked(wx, wy);
        blockedCache[refs] = blocked;
        return !blocked;
    }

    public float GetCost(Vector3 pa, Vector3 pb,
        long prevRef, DtMeshTile prevTile, DtPoly prevPoly,
        long curRef, DtMeshTile curTile, DtPoly curPoly,
        long nextRef, DtMeshTile nextTile, DtPoly nextPoly)
    {
        float cost = Vector3.Distance(pa, pb) * areaCost[curPoly.GetArea()];

        if (active.CostChunkCount == 0)
        {
            return cost;
        }

        if (!factorCache.TryGetValue(curRef, out float factor))
        {
            factor = TryGetCentroid(curTile, curPoly, out float wx, out float wy)
                ? active.CostFactor(wx, wy)
                : 1f;

            factorCache[curRef] = factor;
        }

        return cost * factor;
    }

    /// <summary>
    /// Average of the polygon's vertices, converted back to WoW coordinates.
    /// Zones are authored at 33yd MCNK granularity, so the centroid is a fine
    /// stand-in for the whole polygon.
    /// </summary>
    private static bool TryGetCentroid(DtMeshTile tile, DtPoly poly, out float worldX, out float worldY)
    {
        worldX = 0;
        worldY = 0;

        if (tile?.data?.verts == null || poly.vertCount == 0)
        {
            return false;
        }

        float sumX = 0;
        float sumZ = 0;

        for (int i = 0; i < poly.vertCount; i++)
        {
            int v = poly.verts[i] * 3;
            sumX += tile.data.verts[v + 0];
            sumZ += tile.data.verts[v + 2];
        }

        // rc (x = wow Y, z = wow X) -> wow
        float inv = 1f / poly.vertCount;
        worldY = sumX * inv;
        worldX = sumZ * inv;
        return true;
    }
}
