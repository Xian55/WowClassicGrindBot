#nullable enable
using System.Collections.Frozen;
using System.Collections.Generic;

using DotRecast.Detour;

using Microsoft.Extensions.Logging;

using static DotRecast.Detour.DtDetour;

namespace PPather.Navmesh;

/// <summary>
/// Connected components of the navmesh polygon graph, so "can I walk from here to there"
/// is an O(1) lookup instead of a pathfinding query.
///
/// <para><b>Why this exists.</b> On-mesh is not the same as reachable: a rooftop, a ledge
/// and a tree canopy are all walkable polys, merely disconnected from the floor a route
/// lives on. As <see cref="NavmeshEndpointResolver"/> puts it, "only connectivity can tell
/// those apart". Answering that with <c>FindPath</c> would run a full A* plus straight-path,
/// smoothing and edge-margin passes to produce a yes/no - hundreds of times the work of an
/// array read, and unaffordable in a sampler loop that runs every lap.</para>
///
/// <para>Same component means a path provably exists, so this is not an approximation of
/// the reachability test - it is the reachability test. It says nothing about how
/// <i>long</i> that path is.</para>
/// </summary>
public sealed class NavmeshConnectivity
{
    private readonly FrozenDictionary<long, int> componentOf;
    private readonly NavmeshTileCache tiles;

    /// <summary>Number of distinct components found in the scanned tiles.</summary>
    public int ComponentCount { get; }

    /// <summary>Polygons scanned. Zero means nothing was baked for the requested range.</summary>
    public int PolyCount => componentOf.Count;

    private NavmeshConnectivity(FrozenDictionary<long, int> componentOf, int componentCount,
        NavmeshTileCache tiles)
    {
        this.componentOf = componentOf;
        ComponentCount = componentCount;
        this.tiles = tiles;
    }

    /// <summary>
    /// Stable identity for a polygon: its tile's world coordinates plus its index inside
    /// that tile.
    ///
    /// <para>A raw <c>polyRef</c> cannot be used as a key. It encodes a tile <i>slot</i>
    /// index and a salt, both of which change when the LRU evicts a tile and later reloads
    /// it - which happens routinely during sampling. Keying on the ref made lookups start
    /// missing mid-run, which read as "unreachable" and silently changed the route; two runs
    /// with the same seed then disagreed.</para>
    /// </summary>
    private static long Key(int tileX, int tileZ, int polyIndex) =>
        ((long)(tileX & 0xFFFF) << 48) |
        ((long)(tileZ & 0xFFFF) << 32) |
        (uint)polyIndex;

    /// <summary>Component id, or -1 when the poly was not in the scanned range.</summary>
    public int ComponentOf(long polyRef)
    {
        if (polyRef == 0)
        {
            return -1;
        }

        tiles.Lock.EnterReadLock();
        try
        {
            if (!tiles.NavMesh.GetTileAndPolyByRef(polyRef, out DtMeshTile tile, out _)
                .Succeeded() || tile?.data?.header == null)
            {
                return -1;
            }

            long key = Key(tile.data.header.x, tile.data.header.y,
                DtDetour.DecodePolyIdPoly(polyRef));

            return componentOf.TryGetValue(key, out int c) ? c : -1;
        }
        finally
        {
            tiles.Lock.ExitReadLock();
        }
    }

    /// <summary>True only when both polys are known and share a component.</summary>
    public bool SameComponent(long a, long b)
    {
        int ca = ComponentOf(a);
        return ca >= 0 && ca == ComponentOf(b);
    }

    /// <summary>
    /// Floods every polygon in the currently resident tiles whose tile coordinates fall in
    /// the given inclusive range.
    ///
    /// <para>Scoped to a tile range rather than the whole continent because a route only
    /// ever cares about one zone: a ~1000 yd zone is ~60 tiles at
    /// <see cref="NavmeshSettings.TileWorldSize"/>, a few tens of thousands of polys.</para>
    ///
    /// <para>Runs under the cache's read lock. The caller must have ensured the tiles it
    /// cares about are resident first - a tile that is not loaded contributes nothing, and
    /// its absence would read as "unreachable" rather than as an error.</para>
    /// </summary>
    public static NavmeshConnectivity Build(ILogger logger, NavmeshTileCache tiles,
        int minTileX, int minTileZ, int maxTileX, int maxTileZ)
    {
        Dictionary<long, int> component = [];
        Dictionary<long, List<long>> adjacency = [];

        tiles.Lock.EnterReadLock();
        try
        {
            DtNavMesh navMesh = tiles.NavMesh;

            for (int i = 0; i < navMesh.GetMaxTiles(); i++)
            {
                DtMeshTile tile = navMesh.GetTile(i);
                if (tile?.data?.header == null)
                    continue;

                int tx = tile.data.header.x;
                int tz = tile.data.header.y;

                if (tx < minTileX || tx > maxTileX || tz < minTileZ || tz > maxTileZ)
                    continue;

                for (int p = 0; p < tile.data.header.polyCount; p++)
                {
                    long polyKey = Key(tx, tz, p);

                    List<long> neighbours = [];

                    for (int link = tile.data.polys[p].firstLink;
                        link != DT_NULL_LINK;
                        link = tile.links[link].next)
                    {
                        long neighbourRef = tile.links[link].refs;
                        if (neighbourRef == 0)
                        {
                            continue;
                        }

                        // Translate the neighbour into the same stable identity. Links
                        // cross tiles, so this cannot assume the neighbour lives here.
                        if (navMesh.GetTileAndPolyByRef(neighbourRef,
                                out DtMeshTile nTile, out _).Succeeded() &&
                            nTile?.data?.header != null)
                        {
                            neighbours.Add(Key(nTile.data.header.x, nTile.data.header.y,
                                DtDetour.DecodePolyIdPoly(neighbourRef)));
                        }
                    }

                    adjacency[polyKey] = neighbours;
                    component[polyKey] = -1;
                }
            }
        }
        finally
        {
            tiles.Lock.ExitReadLock();
        }

        // Iterative flood fill - the poly graph is wide and shallow, but a recursive walk
        // over tens of thousands of polys is a stack overflow waiting to happen.
        int next = 0;
        Stack<long> pending = new();

        foreach (long start in adjacency.Keys)
        {
            if (component[start] >= 0)
                continue;

            int id = next++;
            pending.Push(start);
            component[start] = id;

            while (pending.Count > 0)
            {
                long current = pending.Pop();

                foreach (long neighbour in adjacency[current])
                {
                    // A link can point outside the scanned range (into a tile we did not
                    // flood). Skipping it is correct: the route stays inside the range.
                    if (!component.TryGetValue(neighbour, out int seen) || seen >= 0)
                        continue;

                    component[neighbour] = id;
                    pending.Push(neighbour);
                }
            }
        }

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug(
                "NavmeshConnectivity: {Polys} polys in tiles [{MinX},{MinZ}]-[{MaxX},{MaxZ}] -> {Components} components",
                component.Count, minTileX, minTileZ, maxTileX, maxTileZ, next);

        return new NavmeshConnectivity(component.ToFrozenDictionary(), next, tiles);
    }
}
