#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading;

using DotRecast.Core;
using DotRecast.Detour;
using DotRecast.Detour.Io;

using Microsoft.Extensions.Logging;

using WowTriangles;

namespace PPather.Navmesh;

/// <summary>
/// Owns one continent's DtNavMesh: on-demand tile ensure (extract -> bake ->
/// add), write-once disk persistence, and LRU residency eviction.
///
/// Tiles are immutable after bake: first visit pays the bake (~1-2s), the
/// disk cache serves every later session in milliseconds.
///
/// Thread-safety: queries take the read lock, AddTile/RemoveTile the write
/// lock (DtNavMesh must not be mutated during a query).
/// </summary>
public sealed class NavmeshTileCache : IDisposable
{
    public const int MaxResidentTiles = 1024;

    private readonly ILogger logger;
    private readonly ChunkedTriangleCollection world;
    private readonly string cacheDir;

    private readonly DtNavMesh navMesh;
    private readonly ReaderWriterLockSlim rwLock = new();

    // dtTile (x,z) -> last-touch stamp for LRU.
    private readonly Dictionary<(int x, int z), long> resident = [];
    private readonly HashSet<(int x, int z)> emptyTiles = [];

    private long touchCounter;

    public DtNavMesh NavMesh => navMesh;
    public ReaderWriterLockSlim Lock => rwLock;

    public int ResidentTileCount => resident.Count;
    public int TilesBakedThisSession { get; private set; }

    public NavmeshTileCache(ILogger logger, ChunkedTriangleCollection world, string cacheDir)
    {
        this.logger = logger;
        this.world = world;
        this.cacheDir = cacheDir;

        Directory.CreateDirectory(cacheDir);

        DtNavMeshParams param = new()
        {
            orig = new Vector3(NavmeshCoords.Origin, 0, NavmeshCoords.Origin),
            tileWidth = NavmeshSettings.TileWorldSize,
            tileHeight = NavmeshSettings.TileWorldSize,
            maxTiles = NavmeshSettings.MaxTiles,
            maxPolys = NavmeshSettings.MaxPolysPerTile,
        };

        navMesh = new DtNavMesh();
        DtStatus status = navMesh.Init(param, NavmeshSettings.VertsPerPoly);
        if (!status.Succeeded())
        {
            throw new InvalidOperationException($"DtNavMesh.Init failed: {status}");
        }
    }

    public void Dispose()
    {
        rwLock.Dispose();
    }

    /// <summary>
    /// Ensures every tile intersecting the from->to segment (plus one tile of
    /// margin around both endpoints) is resident. Synchronous: first-ever
    /// visits pay the bake here, cached tiles load in milliseconds.
    /// </summary>
    public void EnsureTilesForSegment(Vector3 wowFrom, Vector3 wowTo)
    {
        NavmeshCoords.GetTileIndex(wowFrom.X, wowFrom.Y, out int fromX, out int fromZ);
        NavmeshCoords.GetTileIndex(wowTo.X, wowTo.Y, out int toX, out int toZ);

        // Endpoint 3x3 rings first - they gate the endpoint resolution.
        EnsureRing(fromX, fromZ);
        EnsureRing(toX, toZ);

        // Walk the segment at half-tile steps and ensure a 1-tile margin band.
        Vector3 delta = wowTo - wowFrom;
        float length = MathF.Sqrt((delta.X * delta.X) + (delta.Y * delta.Y));
        float step = NavmeshSettings.TileWorldSize * 0.5f;

        for (float d = step; d < length; d += step)
        {
            float t = d / length;
            float x = wowFrom.X + (delta.X * t);
            float y = wowFrom.Y + (delta.Y * t);

            NavmeshCoords.GetTileIndex(x, y, out int tx, out int tz);
            EnsureRing(tx, tz);
        }
    }

    private void EnsureRing(int centerX, int centerZ)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                EnsureTile(centerX + dx, centerZ + dz);
            }
        }
    }

    public void EnsureTile(int tx, int tz)
    {
        if (!NavmeshCoords.IsValidTile(tx, tz))
        {
            return;
        }

        rwLock.EnterUpgradeableReadLock();
        try
        {
            if (resident.TryGetValue((tx, tz), out _))
            {
                resident[(tx, tz)] = ++touchCounter;
                return;
            }

            if (emptyTiles.Contains((tx, tz)))
            {
                return;
            }

            DtMeshData? data = LoadOrBake(tx, tz);

            rwLock.EnterWriteLock();
            try
            {
                if (data == null)
                {
                    emptyTiles.Add((tx, tz));
                    return;
                }

                DtStatus status = navMesh.AddTile(data, 0, 0, out _);
                if (!status.Succeeded())
                {
                    logger.LogWarning("AddTile({TileX},{TileZ}) failed: {Status}", tx, tz, status);
                    emptyTiles.Add((tx, tz));
                    return;
                }

                resident[(tx, tz)] = ++touchCounter;
                EvictIfOverBudget();
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }
        finally
        {
            rwLock.ExitUpgradeableReadLock();
        }
    }

    private DtMeshData? LoadOrBake(int tx, int tz)
    {
        string path = TilePath(tx, tz);

        if (File.Exists(path))
        {
            try
            {
                using FileStream fs = File.OpenRead(path);
                using BinaryReader br = new(fs);
                return new DtMeshDataReader().Read(br, NavmeshSettings.VertsPerPoly);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Corrupt navmesh tile {Path} - rebaking", path);
                File.Delete(path);
            }
        }

        long start = Stopwatch.GetTimestamp();

        TileGeometry geom = TileGeometryExtractor.Extract(world, tx, tz);
        DtMeshData? data = NavmeshTileBuilder.Bake(geom, tx, tz);

        TilesBakedThisSession++;

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Baked tile ({TileX},{TileZ}): {Polys} polys, {Tris} tris, {FailedChunks} failed chunks, {ElapsedMs:F0}ms",
                tx, tz, data?.header.polyCount ?? 0, geom.GroundTriangleCount,
                geom.FailedChunkLoads, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        if (data != null)
        {
            try
            {
                using FileStream fs = File.Create(path);
                using BinaryWriter bw = new(fs);
                new DtMeshDataWriter().Write(bw, data, RcByteOrder.LITTLE_ENDIAN, false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed persisting navmesh tile {Path}", path);
            }
        }

        return data;
    }

    private void EvictIfOverBudget()
    {
        // Called under the write lock.
        while (resident.Count > MaxResidentTiles)
        {
            (int x, int z) oldest = default;
            long oldestTouch = long.MaxValue;

            foreach (((int x, int z) key, long touch) in resident)
            {
                if (touch < oldestTouch)
                {
                    oldestTouch = touch;
                    oldest = key;
                }
            }

            long refs = navMesh.GetTileRefAt(oldest.x, oldest.z, 0);
            if (refs != 0)
            {
                navMesh.RemoveTile(refs);
            }

            resident.Remove(oldest);
        }
    }

    private string TilePath(int tx, int tz)
    {
        return Path.Combine(cacheDir, $"tile_{tx}_{tz}.dnm");
    }
}
