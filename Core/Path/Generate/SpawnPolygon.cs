using PPather.Navmesh;

using System;
using System.Collections.Generic;
using System.Numerics;

// namespace Core, not Core.Path.Generate: a nested `Core.Path` namespace shadows
// System.IO.Path for every file in `namespace Core`, which is most of the assembly.
// The rest of Core/Path/ does the same.
namespace Core;

/// <summary>
/// The area a generated route may roam, derived from where the selected mobs actually
/// stand: a cell occupancy mask at <see cref="AreaGrid.CellSize"/> (33.33 yd), dilated by a
/// configurable padding, with isolated clusters pruned.
///
/// <para><b>Why a mask and not a hull.</b> A bounding box is wrong for the shapes that
/// matter - Northshire Valley's spawns form an L, and a box routes the player through the
/// abbey. A concave hull would fit, but it is fiddly geometry to get right and to debug. A
/// cell mask is concave for free, tests membership with an O(1) set lookup, and lands on the
/// same grid resolution <see cref="AreaGrid"/> already buckets the world into.</para>
/// </summary>
public sealed class SpawnPolygon
{
    private readonly HashSet<long> cells;

    /// <summary>
    /// Cumulative local-density weights over <see cref="Spawns"/>, for anchor selection.
    /// </summary>
    private readonly float[] cumulativeWeight;

    /// <summary>Spawn positions that survived zone and cluster filtering, in world space.</summary>
    public Vector3[] Spawns { get; }

    public float MinWorldX { get; }
    public float MinWorldY { get; }
    public float MaxWorldX { get; }
    public float MaxWorldY { get; }

    public int CellCount => cells.Count;
    public bool IsEmpty => Spawns.Length == 0;

    /// <summary>Spawns discarded as isolated stragglers, for logging.</summary>
    public int PrunedSpawns { get; }

    /// <summary>Distinct spawn clusters kept.</summary>
    public int ClusterCount { get; }

    /// <summary>
    /// Occupied cells with their spawn count and the mean position of the spawns in them,
    /// densest first. The raw material for <see cref="DensityPeaks"/>.
    /// </summary>
    private readonly (Vector3 Centre, int Count)[] cellCentres;

    private SpawnPolygon(HashSet<long> cells, Vector3[] spawns, float[] cumulativeWeight,
        (Vector3 Centre, int Count)[] cellCentres,
        float minX, float minY, float maxX, float maxY, int prunedSpawns, int clusterCount)
    {
        this.cells = cells;
        this.cumulativeWeight = cumulativeWeight;
        this.cellCentres = cellCentres;
        Spawns = spawns;
        MinWorldX = minX;
        MinWorldY = minY;
        MaxWorldX = maxX;
        MaxWorldY = maxY;
        PrunedSpawns = prunedSpawns;
        ClusterCount = clusterCount;
    }

    private static long Key(int cellX, int cellY) =>
        ((long)cellX << 32) | (uint)cellY;

    public bool Contains(float worldX, float worldY)
    {
        AreaGrid.WorldToCell(worldX, worldY, out int cellX, out int cellY);
        return cells.Contains(Key(cellX, cellY));
    }

    /// <summary>
    /// A spawn to anchor a stop on, drawn in proportion to how many other spawns sit around
    /// it.
    ///
    /// <para>Uniform selection treats a lone mob on the far shore exactly like one in the
    /// middle of a pack, so with only a handful of stops the route regularly detoured across
    /// the zone - and across water - to reach it. Weighting by local density puts the stops
    /// where the grinding actually is.</para>
    /// </summary>
    public Vector3 PickAnchor(Random random)
    {
        if (Spawns.Length == 1)
        {
            return Spawns[0];
        }

        float roll = random.NextSingle() * cumulativeWeight[^1];

        int low = 0;
        int high = cumulativeWeight.Length - 1;

        while (low < high)
        {
            int mid = (low + high) / 2;
            if (cumulativeWeight[mid] < roll)
                low = mid + 1;
            else
                high = mid;
        }

        return Spawns[low];
    }

    /// <summary>
    /// The densest spots in the polygon, spread at least <paramref name="minSpacingYards"/>
    /// apart, densest first, at most <paramref name="max"/> of them.
    ///
    /// <para>Where <see cref="PickAnchor"/> samples randomly for a wander route, a loop wants
    /// a fixed set of the best places to stand. Greedy selection over cells sorted by spawn
    /// count gives that deterministically, and the spacing rule stops all the stops piling
    /// into one camp.</para>
    /// </summary>
    public List<Vector3> DensityPeaks(float minSpacingYards, int max, float minCellShare,
        out int considered)
    {
        considered = cellCentres.Length;

        if (cellCentres.Length == 0)
        {
            return [];
        }

        // cellCentres is sorted densest-first, so [0] is the busiest cell in the polygon.
        // The floor is expressed relative to it: a thin cell out on the fringe holding one
        // stray mob is what "Density" is supposed to skip, and an absolute count could not
        // tell a sparse zone from a dense one.
        int busiest = cellCentres[0].Count;
        int floor = minCellShare <= 0f
            ? 0
            : Math.Max(1, (int)MathF.Ceiling(busiest * minCellShare));

        List<Vector3> peaks = Select(cellCentres, minSpacingYards, max, floor);

        // A floor that leaves nothing to walk is worse than no floor. Sparse zones can have
        // every cell holding one or two spawns, where any share above zero excludes the lot.
        if (peaks.Count < 2 && floor > 0)
        {
            peaks = Select(cellCentres, minSpacingYards, max, 0);
        }

        return peaks;
    }

    private static List<Vector3> Select((Vector3 Centre, int Count)[] cellCentres,
        float minSpacingYards, int max, int minCount)
    {
        List<Vector3> peaks = new(Math.Min(max, cellCentres.Length));
        float minSpacingSq = minSpacingYards * minSpacingYards;

        foreach ((Vector3 centre, int count) in cellCentres)
        {
            if (peaks.Count >= max)
            {
                break;
            }

            if (count < minCount)
            {
                // Sorted densest-first, so everything after this is thinner still.
                break;
            }

            bool tooClose = false;
            for (int i = 0; i < peaks.Count; i++)
            {
                float dx = peaks[i].X - centre.X;
                float dy = peaks[i].Y - centre.Y;
                if ((dx * dx) + (dy * dy) < minSpacingSq)
                {
                    tooClose = true;
                    break;
                }
            }

            if (!tooClose)
            {
                peaks.Add(centre);
            }
        }

        return peaks;
    }

    /// <summary>
    /// Marks the cell of every spawn, dilates by <paramref name="paddingYards"/> so the route
    /// can breathe around a cluster rather than hugging its exact footprint, then drops
    /// clusters too small to be worth walking to.
    /// </summary>
    public static SpawnPolygon Build(IReadOnlyList<Vector3> spawns, float paddingYards,
        float minClusterShare, float densityBias)
    {
        if (spawns.Count == 0)
        {
            return new SpawnPolygon([], [], [], [], 0f, 0f, 0f, 0f, 0, 0);
        }

        // Sorted so the sampler's "pick spawn N" is the same spawn on every run. The callers
        // build this list by walking dictionaries, and that order is not something to rely on
        // when the seed is supposed to make a route reproducible.
        Vector3[] ordered = [.. spawns];
        Array.Sort(ordered, static (a, b) =>
        {
            int cmp = a.X.CompareTo(b.X);
            if (cmp != 0) return cmp;
            cmp = a.Y.CompareTo(b.Y);
            return cmp != 0 ? cmp : a.Z.CompareTo(b.Z);
        });

        Dictionary<long, int> spawnsPerCell = [];
        foreach (Vector3 p in ordered)
        {
            AreaGrid.WorldToCell(p.X, p.Y, out int cellX, out int cellY);
            long key = Key(cellX, cellY);
            spawnsPerCell.TryGetValue(key, out int count);
            spawnsPerCell[key] = count + 1;
        }

        int radius = (int)MathF.Ceiling(paddingYards / AreaGrid.CellSize);
        HashSet<long> dilated = radius <= 0
            ? [.. spawnsPerCell.Keys]
            : Dilate(spawnsPerCell.Keys, radius);

        (HashSet<long> kept, int clusters) =
            PruneIsolatedClusters(dilated, spawnsPerCell, minClusterShare);

        List<Vector3> survivors = new(ordered.Length);
        foreach (Vector3 p in ordered)
        {
            AreaGrid.WorldToCell(p.X, p.Y, out int cellX, out int cellY);
            if (kept.Contains(Key(cellX, cellY)))
            {
                survivors.Add(p);
            }
        }

        if (survivors.Count == 0)
        {
            // Every cluster fell below the bar - keep everything rather than emit nothing.
            survivors.AddRange(ordered);
            kept = dilated;
        }

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        foreach (Vector3 p in survivors)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }

        float pad = radius * AreaGrid.CellSize;

        return new SpawnPolygon(kept, [.. survivors],
            BuildWeights(survivors, spawnsPerCell, densityBias),
            BuildCellCentres(survivors),
            minX - pad, minY - pad, maxX + pad, maxY + pad,
            ordered.Length - survivors.Count, clusters);
    }

    /// <summary>
    /// Mean spawn position per occupied cell, ordered densest first. Ties break on position
    /// so the order does not depend on how the dictionary happened to enumerate - a loop
    /// route has to be the same one every session.
    /// </summary>
    private static (Vector3 Centre, int Count)[] BuildCellCentres(List<Vector3> spawns)
    {
        Dictionary<long, (Vector3 Sum, int Count)> byCell = [];

        foreach (Vector3 p in spawns)
        {
            AreaGrid.WorldToCell(p.X, p.Y, out int cellX, out int cellY);
            long key = Key(cellX, cellY);

            byCell.TryGetValue(key, out (Vector3 Sum, int Count) acc);
            byCell[key] = (acc.Sum + p, acc.Count + 1);
        }

        (Vector3 Centre, int Count)[] result = new (Vector3, int)[byCell.Count];

        int i = 0;
        foreach ((Vector3 sum, int count) in byCell.Values)
        {
            result[i++] = (sum / count, count);
        }

        Array.Sort(result, static (a, b) =>
        {
            int cmp = b.Count.CompareTo(a.Count);
            if (cmp != 0) return cmp;

            cmp = a.Centre.X.CompareTo(b.Centre.X);
            return cmp != 0 ? cmp : a.Centre.Y.CompareTo(b.Centre.Y);
        });

        return result;
    }

    /// <summary>
    /// Each spawn's weight is how many spawns sit in its own cell and the eight around it -
    /// a cheap local density that needs no radius search.
    /// </summary>
    private static float[] BuildWeights(List<Vector3> spawns, Dictionary<long, int> spawnsPerCell,
        float densityBias)
    {
        float[] cumulative = new float[spawns.Count];
        float running = 0f;

        for (int i = 0; i < spawns.Count; i++)
        {
            AreaGrid.WorldToCell(spawns[i].X, spawns[i].Y, out int cellX, out int cellY);

            int density = 0;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    spawnsPerCell.TryGetValue(Key(cellX + dx, cellY + dy), out int count);
                    density += count;
                }
            }

            // bias 0 -> every spawn weighs the same (pure coverage);
            // 1 -> proportional to density; 2 -> packs dominate.
            running += densityBias <= 0f
                ? 1f
                : MathF.Pow(Math.Max(1, density), densityBias);
            cumulative[i] = running;
        }

        return cumulative;
    }

    /// <summary>
    /// Flood-fills the dilated mask into connected regions and keeps only those holding a
    /// meaningful share of the spawns. Two mobs on the far side of a lake form their own
    /// region; walking there costs more than the kills are worth.
    /// </summary>
    private static (HashSet<long> Cells, int Clusters) PruneIsolatedClusters(
        HashSet<long> dilated, Dictionary<long, int> spawnsPerCell, float minClusterShare)
    {
        Dictionary<long, int> componentOf = [];
        List<int> spawnCounts = [];
        List<List<long>> componentCells = [];

        Stack<long> pending = new();

        foreach (long start in dilated)
        {
            if (componentOf.ContainsKey(start))
                continue;

            int id = spawnCounts.Count;
            spawnCounts.Add(0);
            componentCells.Add([]);

            pending.Push(start);
            componentOf[start] = id;

            while (pending.Count > 0)
            {
                long current = pending.Pop();

                componentCells[id].Add(current);
                spawnsPerCell.TryGetValue(current, out int here);
                spawnCounts[id] += here;

                int cellX = (int)(current >> 32);
                int cellY = (int)(uint)current;

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0)
                            continue;

                        long neighbour = Key(cellX + dx, cellY + dy);
                        if (dilated.Contains(neighbour) && !componentOf.ContainsKey(neighbour))
                        {
                            componentOf[neighbour] = id;
                            pending.Push(neighbour);
                        }
                    }
                }
            }
        }

        int biggest = 0;
        foreach (int count in spawnCounts)
        {
            if (count > biggest) biggest = count;
        }

        float threshold = biggest * minClusterShare;

        HashSet<long> kept = [];
        int clusters = 0;

        for (int id = 0; id < spawnCounts.Count; id++)
        {
            if (spawnCounts[id] < threshold)
                continue;

            clusters++;
            foreach (long cell in componentCells[id])
            {
                kept.Add(cell);
            }
        }

        return (kept, clusters);
    }

    private static HashSet<long> Dilate(IEnumerable<long> seed, int radius)
    {
        HashSet<long> result = [];

        foreach (long key in seed)
        {
            int cellX = (int)(key >> 32);
            int cellY = (int)(uint)key;

            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    result.Add(Key(cellX + dx, cellY + dy));
                }
            }
        }

        return result;
    }
}
