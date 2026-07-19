#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using DotRecast.Core;
using DotRecast.Detour;
using DotRecast.Detour.Io;

using Microsoft.Extensions.Logging;

using WowTriangles;

namespace PPather.Navmesh;

/// <summary>
/// Owns one continent's DtNavMesh: on-demand tile bake through a small
/// background worker pool, write-once disk persistence, and LRU residency
/// eviction.
///
/// Tiles are immutable after bake: first visit pays the bake (~1-2s), the
/// disk cache serves every later session in milliseconds.
///
/// Concurrency model:
/// - N bake workers drain urgent-first channels; geometry extraction is
///   serialized by <see cref="extractLock"/> (ChunkedTriangleCollection is not
///   thread-safe), the recast bake itself runs in parallel.
/// - Queries take the read lock; AddTile/RemoveTile take the write lock
///   (DtNavMesh must not be mutated during a query).
/// </summary>
public sealed class NavmeshTileCache : IDisposable
{
    public const int MaxResidentTiles = 1024;

    /// <summary>How long a query waits for its corridor tiles before pathing
    /// with whatever is resident (stitching self-heals on the next request).</summary>
    public static readonly TimeSpan CorridorWaitBudget = TimeSpan.FromSeconds(5);

    private static readonly int WorkerCount = Math.Clamp(Environment.ProcessorCount / 4, 2, 4);

    private readonly ILogger logger;
    private readonly ChunkedTriangleCollection world;
    private readonly string cacheDir;

    private readonly DtNavMesh navMesh;
    private readonly ReaderWriterLockSlim rwLock = new();
    private readonly object extractLock = new();

    private readonly Channel<TileRequest> urgent =
        Channel.CreateUnbounded<TileRequest>(new UnboundedChannelOptions { SingleReader = false });
    private readonly Channel<TileRequest> background =
        Channel.CreateUnbounded<TileRequest>(new UnboundedChannelOptions { SingleReader = false });

    private readonly CancellationTokenSource cts = new();
    private readonly Task[] workers;

    // dtTile (x,z) -> last-touch stamp for LRU. Concurrent: touched by query
    // threads, enumerated under the write lock during eviction.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(int x, int z), long> resident = [];
    // Guarded by stateLock.
    private readonly HashSet<(int x, int z)> emptyTiles = [];
    // Tiles currently queued or baking - avoids duplicate work. Guarded by stateLock.
    private readonly HashSet<(int x, int z)> inFlight = [];
    private readonly object stateLock = new();

    private long touchCounter;

    private readonly record struct TileRequest(int X, int Z, TaskCompletionSource? Done);

    /// <summary>Raised after a tile lands in the mesh (viz). May fire on a worker thread.</summary>
    public Action<int, int, DtMeshData>? NotifyTileAdded;

    /// <summary>Raised after LRU eviction removes a tile (viz cleanup).</summary>
    public Action<int, int>? NotifyTileRemoved;

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

        workers = new Task[WorkerCount];
        for (int i = 0; i < workers.Length; i++)
        {
            workers[i] = Task.Run(WorkerLoop);
        }
    }

    public void Dispose()
    {
        cts.Cancel();
        urgent.Writer.TryComplete();
        background.Writer.TryComplete();

        try
        {
            Task.WaitAll(workers, TimeSpan.FromSeconds(10));
        }
        catch (AggregateException)
        {
            // cancellation
        }

        cts.Dispose();
        rwLock.Dispose();
    }

    /// <summary>
    /// Ensures the tiles needed by a from->to query: 3x3 endpoint rings are
    /// waited on fully (they gate endpoint resolution); corridor tiles are
    /// waited on up to <see cref="CorridorWaitBudget"/>, after which pathing
    /// proceeds with what is resident and the rest keeps baking behind.
    /// All bakes run on the worker pool, so cold multi-tile ensures
    /// parallelize across workers.
    /// </summary>
    public void EnsureTilesForSegment(Vector3 wowFrom, Vector3 wowTo)
    {
        EnsureTilesForSegment(wowFrom, wowTo, CorridorWaitBudget);
    }

    public void EnsureTilesForSegment(Vector3 wowFrom, Vector3 wowTo, TimeSpan corridorBudget)
    {
        List<Task> endpointWaits = [];
        List<Task> corridorWaits = [];

        RequestEndpointTiles(wowFrom, endpointWaits);
        RequestEndpointTiles(wowTo, endpointWaits);

        NavmeshCoords.GetTileIndex(wowFrom.X, wowFrom.Y, out int fromX, out int fromZ);
        NavmeshCoords.GetTileIndex(wowTo.X, wowTo.Y, out int toX, out int toZ);

        Vector3 delta = wowTo - wowFrom;
        float length = MathF.Sqrt((delta.X * delta.X) + (delta.Y * delta.Y));
        float step = NavmeshSettings.TileWorldSize * 0.5f;

        for (float d = step; d < length; d += step)
        {
            float t = d / length;
            NavmeshCoords.GetTileIndex(
                wowFrom.X + (delta.X * t),
                wowFrom.Y + (delta.Y * t),
                out int tx, out int tz);

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    Request(tx + dx, tz + dz, corridorWaits, urgentQueue: true);
                }
            }
        }

        Task.WaitAll([.. endpointWaits], cts.Token);

        if (corridorWaits.Count > 0 && corridorBudget > TimeSpan.Zero)
        {
            Task.WaitAll([.. corridorWaits], corridorBudget);
        }
    }

    /// <summary>Fire-and-forget low-priority bake (e.g. area warm-up).</summary>
    public void Prefetch(int tx, int tz)
    {
        Request(tx, tz, waits: null, urgentQueue: false);
    }

    /// <summary>
    /// The endpoint resolver searches horizontally +-3yd, so only the tile
    /// containing the point is required - plus a neighbor when the point sits
    /// within this margin of a tile edge. Typical: 1 tile; worst (corner): 4.
    /// </summary>
    public const float EndpointEdgeMargin = 8f;

    private void RequestEndpointTiles(Vector3 wow, List<Task> waits)
    {
        NavmeshCoords.GetTileIndex(wow.X, wow.Y, out int tx, out int tz);
        NavmeshCoords.GetTileWowBounds(tx, tz, out float minX, out float minY, out float maxX, out float maxY);

        // dtTileX spans wow Y, dtTileZ spans wow X (see NavmeshCoords).
        int dTxLo = wow.Y - minY < EndpointEdgeMargin ? -1 : 0;
        int dTxHi = maxY - wow.Y < EndpointEdgeMargin ? 1 : 0;
        int dTzLo = wow.X - minX < EndpointEdgeMargin ? -1 : 0;
        int dTzHi = maxX - wow.X < EndpointEdgeMargin ? 1 : 0;

        for (int dTx = dTxLo; dTx <= dTxHi; dTx++)
        {
            for (int dTz = dTzLo; dTz <= dTzHi; dTz++)
            {
                Request(tx + dTx, tz + dTz, waits, urgentQueue: true);
            }
        }
    }

    private void Request(int tx, int tz, List<Task>? waits, bool urgentQueue)
    {
        if (!NavmeshCoords.IsValidTile(tx, tz))
        {
            return;
        }

        if (resident.ContainsKey((tx, tz)))
        {
            resident[(tx, tz)] = Interlocked.Increment(ref touchCounter);
            return;
        }

        TaskCompletionSource? done = null;

        lock (stateLock)
        {
            if (emptyTiles.Contains((tx, tz)))
            {
                return;
            }

            if (!inFlight.Add((tx, tz)))
            {
                // Already queued or baking; nothing to await per-tile here -
                // duplicate waiters are rare and the budget wait covers them.
                return;
            }

            if (waits != null)
            {
                done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                waits.Add(done.Task);
            }
        }

        TileRequest request = new(tx, tz, done);
        Channel<TileRequest> queue = urgentQueue ? urgent : background;
        queue.Writer.TryWrite(request);
    }

    private async Task WorkerLoop()
    {
        CancellationToken token = cts.Token;

        while (!token.IsCancellationRequested)
        {
            TileRequest request;

            if (urgent.Reader.TryRead(out request) ||
                background.Reader.TryRead(out request))
            {
                ProcessRequest(request);
                continue;
            }

            try
            {
                // Sleep until either queue has work; urgent is preferred on wake.
                Task<bool> urgentWait = urgent.Reader.WaitToReadAsync(token).AsTask();
                Task<bool> backgroundWait = background.Reader.WaitToReadAsync(token).AsTask();
                await Task.WhenAny(urgentWait, backgroundWait);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void ProcessRequest(in TileRequest request)
    {
        (int tx, int tz) = (request.X, request.Z);

        try
        {
            DtMeshData? data = LoadOrBake(tx, tz);

            if (data == null)
            {
                lock (stateLock)
                {
                    emptyTiles.Add((tx, tz));
                }
            }
            else
            {
                rwLock.EnterWriteLock();
                try
                {
                    DtStatus status = navMesh.AddTile(data, 0, 0, out _);
                    if (!status.Succeeded())
                    {
                        logger.LogWarning("AddTile({TileX},{TileZ}) failed: {Status}", tx, tz, status);
                        data = null;
                    }
                    else
                    {
                        resident[(tx, tz)] = Interlocked.Increment(ref touchCounter);
                        EvictIfOverBudget();
                    }
                }
                finally
                {
                    rwLock.ExitWriteLock();
                }

                if (data == null)
                {
                    lock (stateLock)
                    {
                        emptyTiles.Add((tx, tz));
                    }
                }
            }

            if (data != null)
            {
                NotifyTileAdded?.Invoke(tx, tz, data);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Baking tile ({TileX},{TileZ}) failed", tx, tz);
        }
        finally
        {
            lock (stateLock)
            {
                inFlight.Remove((tx, tz));
            }

            request.Done?.TrySetResult();
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

        TileGeometry geom;
        lock (extractLock)
        {
            geom = TileGeometryExtractor.Extract(world, tx, tz);
        }

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

            resident.TryRemove(oldest, out _);
            NotifyTileRemoved?.Invoke(oldest.x, oldest.z);
        }
    }

    private string TilePath(int tx, int tz)
    {
        return Path.Combine(cacheDir, $"tile_{tx}_{tz}.dnm");
    }
}
