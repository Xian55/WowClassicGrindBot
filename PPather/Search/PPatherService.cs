using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PPather.Data;
using PPather.Graph;
using PPather.Navmesh;

using SharedLib;
using SharedLib.Data;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static System.Diagnostics.Stopwatch;
using System.Numerics;

using WowTriangles;

namespace PPather;

public sealed class PPatherService : IDisposable
{
    private readonly ILogger<PPatherService> logger;
    private readonly DataConfig dataConfig;
    private readonly WorldMapAreaDB worldMapAreaDB;
    private readonly NavmeshBakeOptions bakeOptions;
    private readonly NavmeshQueryOptions queryOptions;

    public event Action SearchBegin;
    public event Action<Path> OnPathCreated;
    public event Action<ChunkEventArgs> OnChunkAdded;

    /// <summary>Baked navmesh tile landed in the mesh. May fire on a baker thread.</summary>
    public event Action<int, int, DotRecast.Detour.DtMeshData> OnNavmeshTileAdded;

    /// <summary>Navmesh tile evicted (LRU).</summary>
    public event Action<int, int> OnNavmeshTileRemoved;

    public Action<LinesEventArgs> OnLinesAdded;
    public Action<SphereEventArgs> OnSphereAdded;

    private Search search { get; set; }

    private NavmeshPathfinder navmeshPathfinder;
    private float navmeshMapId = -1;
    private bool? lastStartIndoors;

    private readonly Lock areaGridLock = new();
    private readonly Dictionary<string, AreaGrid?> areaGrids = new();
    private readonly Dictionary<string, NavmeshPathfinder?> queryNavmesh = new();

    /// <summary>
    /// Selects the in-process engine. Runtime-settable so the benchmark can
    /// A/B both engines without a server restart.
    /// </summary>
    public PathingEngine Engine { get; set; } = PathingEngine.Navmesh;

    public NavmeshPathfinder NavmeshPathfinder => navmeshPathfinder;

    /// <summary>
    /// Displaces interior waypoints by up to this many yards so repeated trips
    /// over the same route do not retrace an identical line. 0 disables it.
    /// Applied to the next navmesh query.
    /// </summary>
    public float PathJitterYards { get; set; }

    /// <summary>Seed for <see cref="PathJitterYards"/>; null is non-deterministic.</summary>
    public int? PathJitterSeed { get; set; }

    /// <summary>
    /// Per-request edge-margin override in yards. Null uses the engine default
    /// (PPATHER_PATH_EDGE_MARGIN); 0 disables the push for the request.
    /// Applied to the next navmesh query.
    /// </summary>
    public float? PathEdgeMarginYards { get; set; }

    /// <summary>
    /// Re-reads the authored road / danger zone files for the active continent.
    /// Takes effect on the next query - the navmesh itself is untouched, so
    /// there is no rebake and no cache invalidation.
    /// </summary>
    public bool ReloadCostZones()
    {
        if (navmeshPathfinder == null || search == null)
        {
            return false;
        }

        string continent = ContinentDB.IdToName[search.MapId];

        // All or nothing. These files are hand-editable and the watcher can fire
        // mid-write, so a partial read is expected rather than exceptional -
        // applying it would quietly delete the user's authored zones. Keeping
        // the working set and complaining is the safe failure.
        if (!CostZoneLoader.TryLoad(logger, dataConfig.Road, continent, out CostZones loaded,
            roadCoreHalfWidth: queryOptions.RoadCore))
        {
            logger.LogWarning(
                "Cost zones for {Continent} were not reloaded: at least one file is unreadable or invalid. Previous zones stay in effect.",
                continent);
            return false;
        }

        navmeshPathfinder.Zones = loaded;
        return true;
    }

    public bool Initialised => search != null;

    public bool IsSearching { get; set; }

    public Vector4 SearchFrom => search.From;
    public Vector4 SearchTo => search.Target;
    public Vector3 ClosestLocation => search?.PathGraph?.ClosestSpot?.Loc ?? Vector3.Zero;
    public Vector3 PeekLocation => search?.PathGraph?.PeekSpot?.Loc ?? Vector3.Zero;

    public HashSet<Vector4> TestPoints => search?.PathGraph?.TestPoints ?? [];

    public HashSet<Vector3> BlockedPoints => search?.PathGraph?.BlockedPoints ?? [];

    public PPatherService(ILogger<PPatherService> logger, DataConfig dataConfig, WorldMapAreaDB worldMapAreaDB,
        IOptions<NavmeshBakeOptions> bakeOptions, IOptions<NavmeshQueryOptions> queryOptions)
    {
        this.dataConfig = dataConfig;
        this.logger = logger;
        this.worldMapAreaDB = worldMapAreaDB;
        this.bakeOptions = bakeOptions.Value;
        this.queryOptions = queryOptions.Value;
        ContinentDB.Init(worldMapAreaDB.Values);

        MPQSelfTest();
        StartZoneWatcher();
    }

    // --- Authored cost zone hot reload ----------------------------------

    /// <summary>
    /// Coalescing window for zone file edits. One save from the map UI writes a
    /// road file and a danger file, and most editors emit several events per
    /// write, so reloading per event would reload four or five times for one
    /// logical edit.
    /// </summary>
    private const int ZoneReloadDebounceMs = 500;

    private System.IO.FileSystemWatcher? zoneWatcher;
    private Timer? zoneReloadDebounce;

    /// <summary>
    /// Reloads authored zones whenever their files change on disk, whoever
    /// wrote them. The map UI already calls the reload endpoint after saving,
    /// but hand edits, scripts and the DangerZoneGenerator utility do not -
    /// without this they take effect only after a restart.
    ///
    /// Zones are query-time only, so this is a pointer swap: no rebake, no
    /// navmesh invalidation.
    /// </summary>
    private void StartZoneWatcher()
    {
        string root = dataConfig.Road;
        if (!System.IO.Directory.Exists(root))
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Cost zone watcher not started: {Root} does not exist.", root);
            }
            return;
        }

        try
        {
            zoneReloadDebounce = new Timer(_ => ReloadZonesFromDisk(), null, Timeout.Infinite, Timeout.Infinite);

            zoneWatcher = new System.IO.FileSystemWatcher(root, "*.json")
            {
                IncludeSubdirectories = true, // dangerzone/ lives under road/
                NotifyFilter = System.IO.NotifyFilters.LastWrite | System.IO.NotifyFilters.FileName | System.IO.NotifyFilters.Size,
            };

            zoneWatcher.Changed += OnZoneFileChanged;
            zoneWatcher.Created += OnZoneFileChanged;
            zoneWatcher.Deleted += OnZoneFileChanged;
            zoneWatcher.Renamed += OnZoneFileChanged;
            zoneWatcher.Error += (_, e) =>
                logger.LogWarning(e.GetException(), "Cost zone watcher error; edits may need a manual reload.");

            zoneWatcher.EnableRaisingEvents = true;
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Cost zone watcher live on {Root}.", root);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not start the cost zone watcher; use the reload endpoint instead.");
            zoneWatcher?.Dispose();
            zoneWatcher = null;
        }
    }

    private void OnZoneFileChanged(object sender, System.IO.FileSystemEventArgs e) =>
        zoneReloadDebounce?.Change(ZoneReloadDebounceMs, Timeout.Infinite);

    /// <summary>
    /// Timer callback. Must never throw: an escaping exception on a timer
    /// thread takes the process down.
    /// </summary>
    private void ReloadZonesFromDisk()
    {
        try
        {
            if (ReloadCostZones())
            {
                logger.LogInformation("Cost zones reloaded from disk.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Reloading cost zones from disk failed; the previous zones stay in effect.");
        }
    }

    public void Dispose()
    {
        if (zoneWatcher != null)
        {
            zoneWatcher.EnableRaisingEvents = false;
            zoneWatcher.Dispose();
            zoneWatcher = null;
        }

        zoneReloadDebounce?.Dispose();
        zoneReloadDebounce = null;

        lock (areaGridLock)
        {
            foreach (NavmeshPathfinder? nav in queryNavmesh.Values)
            {
                nav?.Dispose();
            }
            queryNavmesh.Clear();
        }
    }

    public void Reset()
    {
        // A bulk bake holds the tile cache and the geometry world for as long as
        // it runs - tearing them down underneath it cancels the job. The Leaflet
        // page calls Reset on every load, so without this a browser refresh
        // silently kills a multi-hour bake.
        if (bakeStatus.Running)
        {
            logger.LogInformation("Reset skipped: a navmesh bake is running.");
            return;
        }

        navmeshPathfinder?.Dispose();
        navmeshPathfinder = null;
        navmeshMapId = -1;

        if (search == null)
            return;

        search.Clear();
        search = null;
    }

    public void Initialise(float mapId)
    {
        if (search != null && mapId == search.MapId)
        {
            return;
        }

        if (search != null && mapId != search.MapId)
        {
            Reset();
        }

        search = new Search(mapId, logger, dataConfig);
        search.PathGraph.triangleWorld.NotifyChunkAdded = ChunkAdded;
    }

    public bool MPQSelfTest()
    {
        string[] mpqFiles = MPQTriangleSupplier.GetArchiveNames(dataConfig);
        if (mpqFiles.Length == 0)
        {
            logger.LogInformation("No MPQ files found, refer to the Readme to download them!");
            return false;
        }

        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("MPQ files exist. {MpqFiles}", string.Join(' ', mpqFiles));

        return true;
    }

    public TriangleCollection GetChunkAt(int grid_x, int grid_y)
    {
        return search.PathGraph.triangleWorld.GetChunkAt(grid_x, grid_y);
    }

    public ChunkedTriangleCollection TriangleWorld => search.PathGraph.triangleWorld;

    public IEnumerable<Spot> GetSpots()
    {
        return search.PathGraph.SpotManager.AllSpots();
    }

    public void ChunkAdded(ChunkEventArgs e)
    {
        OnChunkAdded?.Invoke(e);
    }

    public Vector4[] CreateLocations(LineArgs lines)
    {
        Vector4[] result = new Vector4[lines.Spots.Length];
        Span<Vector4> span = result.AsSpan();

        for (int i = 0; i < span.Length; i++)
        {
            Vector3 spot = lines.Spots[i];
            span[i] = ToWorld(lines.MapId, spot.X, spot.Y, spot.Z);
        }

        return result;
    }

    public Vector4 ToWorld(int uiMap, float mapX, float mapY, float z = 0)
    {
        if (!worldMapAreaDB.TryGet(uiMap, out WorldMapArea wma))
            return Vector4.Zero;

        float worldX = wma.ToWorldX(mapY);
        float worldY = wma.ToWorldY(mapX);

        Initialise(wma.MapID);

        return search.CreateWorldLocation(worldX, worldY, z, wma.MapID, null);
    }

    public Vector4 ToWorldZ(int uiMap, float x, float y, float z, bool? startIndoors = null)
    {
        if (!worldMapAreaDB.TryGet(uiMap, out WorldMapArea wma))
            return Vector4.Zero;

        Initialise(wma.MapID);

        lastStartIndoors = startIndoors;

        return search.CreateWorldLocation(x, y, z, wma.MapID, startIndoors);
    }

    public int GetMapId(int uiMap)
    {
        return worldMapAreaDB.GetMapId(uiMap);
    }

    public Vector3 ToLocal(Vector3 world, float mapId, int uiMapId)
    {
        WorldMapArea wma = worldMapAreaDB.GetWorldMapArea(world.X, world.Y, (int)mapId, uiMapId);
        return new Vector3(wma.ToMapY(world.Y), wma.ToMapX(world.X), world.Z);
    }

    public Path DoSearch(SearchStrategy searchType)
    {
        SearchBegin?.Invoke();
        IsSearching = true;

        Path path = Engine == PathingEngine.Navmesh
            ? NavmeshSearch()
            : search.DoSearch(searchType);

        IsSearching = false;
        OnPathCreated?.Invoke(path);
        return path;
    }

    private Path NavmeshSearch()
    {
        EnsureNavmeshPathfinder();

        navmeshPathfinder.JitterYards = PathJitterYards;
        navmeshPathfinder.JitterSeed = PathJitterSeed;
        navmeshPathfinder.EdgeMarginYards = PathEdgeMarginYards ?? queryOptions.EdgeMargin;

        Vector3 from = search.From.AsVector3();
        Vector3 to = search.Target.AsVector3();

        // Callers that bypass ToWorldZ (raw WorldRoute) pass z=0. The navmesh
        // column scan alone would pick the highest poly - which can be a tree
        // canopy or roof. Seed the height with the spot-geometry surface
        // heuristics (canopy/terrain preference) first; the resolver then only
        // needs its small vertical extents.
        if (from.Z == 0)
        {
            from = search.CreateWorldLocation(from.X, from.Y, 0, (int)search.MapId, lastStartIndoors).AsVector3();
        }

        if (to.Z == 0)
        {
            to = search.CreateWorldLocation(to.X, to.Y, 0, (int)search.MapId, null).AsVector3();
        }

        return navmeshPathfinder.FindPath(from, to, lastStartIndoors);
    }

    /// <summary>
    /// Progress of a bulk navmesh bake. Bakes run on a background thread because
    /// a whole continent is tens of thousands of tiles and takes far longer than
    /// any request should.
    /// </summary>
    public sealed class NavmeshBakeStatus
    {
        public bool Running { get; set; }
        public string Scope { get; set; } = string.Empty;
        public string Continent { get; set; } = string.Empty;
        public int AdtTotal { get; set; }
        public int AdtDone { get; set; }
        public int TilesTotal { get; set; }
        public int TilesDone { get; set; }
        public int TilesAlreadyOnDisk { get; set; }
        public double ElapsedSeconds { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    private readonly NavmeshBakeStatus bakeStatus = new();
    private readonly object bakeLock = new();
    private CancellationTokenSource? bakeCts;
    private Task? bakeTask;

    public NavmeshBakeStatus BakeStatus
    {
        get
        {
            lock (bakeLock)
            {
                return new NavmeshBakeStatus
                {
                    Running = bakeStatus.Running,
                    Scope = bakeStatus.Scope,
                    Continent = bakeStatus.Continent,
                    AdtTotal = bakeStatus.AdtTotal,
                    AdtDone = bakeStatus.AdtDone,
                    TilesTotal = bakeStatus.TilesTotal,
                    TilesDone = bakeStatus.TilesDone,
                    TilesAlreadyOnDisk = bakeStatus.TilesAlreadyOnDisk,
                    ElapsedSeconds = bakeStatus.ElapsedSeconds,
                    Message = bakeStatus.Message
                };
            }
        }
    }

    /// <summary>
    /// Starts a background bake. <paramref name="adt"/> null bakes every ADT the
    /// continent actually has terrain for; the WDT's own grid mask is used, so
    /// the open ocean that makes up most of the 64x64 grid is skipped entirely.
    /// A null continent bakes them all in turn.
    /// </summary>
    public bool StartBake(string? continent, (int x, int y)? adt)
    {
        lock (bakeLock)
        {
            if (bakeStatus.Running)
            {
                return false;
            }

            bakeStatus.Running = true;
            bakeStatus.Scope = adt.HasValue ? $"adt {adt.Value.x},{adt.Value.y}"
                : continent is null ? "all continents" : "continent";
            bakeStatus.Continent = continent ?? string.Empty;
            bakeStatus.AdtTotal = bakeStatus.AdtDone = 0;
            bakeStatus.TilesTotal = bakeStatus.TilesDone = bakeStatus.TilesAlreadyOnDisk = 0;
            bakeStatus.ElapsedSeconds = 0;
            bakeStatus.Message = "starting";
        }

        bakeCts = new CancellationTokenSource();
        CancellationToken token = bakeCts.Token;

        bakeTask = Task.Run(() => BakeWorker(continent, adt, token), token);
        return true;
    }

    public bool CancelBake()
    {
        if (bakeCts is null || !bakeStatus.Running)
        {
            return false;
        }

        bakeCts.Cancel();
        return true;
    }

    private void BakeWorker(string? continent, (int x, int y)? adt, CancellationToken token)
    {
        long start = GetTimestamp();

        try
        {
            List<string> continents = continent is null
                ? [.. WorldContinents]
                : [continent];

            foreach (string name in continents)
            {
                token.ThrowIfCancellationRequested();
                BakeContinent(name, adt, token, start);
            }

            SetBakeMessage("done", start);
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Navmesh bake finished: {Scope} - {TilesDone} tiles baked, {OnDisk} already present, {Elapsed:F0}s",
                    bakeStatus.Continent.Length == 0 ? "all continents" : bakeStatus.Continent,
                    bakeStatus.TilesDone, bakeStatus.TilesAlreadyOnDisk, GetElapsedTime(start).TotalSeconds);
            }
        }
        catch (OperationCanceledException)
        {
            SetBakeMessage("cancelled", start);
            logger.LogWarning("Navmesh bake cancelled after {Elapsed:F0}s ({TilesDone} tiles baked)",
                GetElapsedTime(start).TotalSeconds, bakeStatus.TilesDone);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Navmesh bake failed");
            SetBakeMessage("failed: " + ex.Message, start);
        }
        finally
        {
            lock (bakeLock)
            {
                bakeStatus.Running = false;
                bakeStatus.ElapsedSeconds = GetElapsedTime(start).TotalSeconds;
            }
        }
    }

    private void BakeContinent(string name, (int x, int y)? adt, CancellationToken token, long start)
    {
        float mapId = ContinentDB.IdToName.First(kvp => kvp.Value == name).Key;

        Initialise(mapId);
        EnsureNavmeshPathfinder();

        NavmeshTileCache tiles = navmeshPathfinder!.Tiles;

        List<(int x, int y)> adts = adt.HasValue
            ? [adt.Value]
            : TriangleWorld.ExistingAdts();

        lock (bakeLock)
        {
            bakeStatus.Continent = name;
            bakeStatus.AdtTotal = adts.Count;
            bakeStatus.AdtDone = 0;
        }

        foreach ((int ax, int ay) in adts)
        {
            token.ThrowIfCancellationRequested();

            List<(int tx, int tz)> batch = AdtNavmeshTiles(ax, ay);

            int onDisk = 0;
            foreach ((int tx, int tz) in batch)
            {
                if (tiles.IsTileOnDisk(tx, tz))
                {
                    onDisk++;
                }
            }

            int bakedBefore = tiles.TilesBakedThisSession;

            tiles.BakeBatch(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(batch), token);

            lock (bakeLock)
            {
                bakeStatus.AdtDone++;
                bakeStatus.TilesTotal += batch.Count;

                // Count what actually baked, not what was asked for - tiles
                // already on disk or known empty return immediately.
                bakeStatus.TilesDone += tiles.TilesBakedThisSession - bakedBefore;
                bakeStatus.TilesAlreadyOnDisk += onDisk;
                bakeStatus.ElapsedSeconds = GetElapsedTime(start).TotalSeconds;
                bakeStatus.Message = $"{name} adt {ax},{ay}";
            }
        }

        // A full-continent bake loads every ADT's triangle soup on demand and
        // the geometry cache would otherwise keep it all resident (gigabytes).
        // The tiles are on disk now; drop the geometry so it is not left held.
        tiles.EvictGeometry();
    }

    /// <summary>
    /// Navmesh tiles covering one ADT. Derived from the ADT's world bounds
    /// rather than by scaling indices, because the ADT grid counts down from the
    /// origin while the navmesh grid counts up.
    /// </summary>
    private static List<(int tx, int tz)> AdtNavmeshTiles(int adtX, int adtY)
    {
        ChunkedTriangleCollection.GetAdtWorldBounds(adtX, adtY,
            out float minX, out float minY, out float maxX, out float maxY);

        // Nudge inside the edges so a boundary does not pull in the next ADT.
        const float Inset = 0.5f;
        NavmeshCoords.GetTileIndex(minX + Inset, minY + Inset, out int tx0, out int tz0);
        NavmeshCoords.GetTileIndex(maxX - Inset, maxY - Inset, out int tx1, out int tz1);

        List<(int tx, int tz)> result = [];

        for (int tx = Math.Min(tx0, tx1); tx <= Math.Max(tx0, tx1); tx++)
        {
            for (int tz = Math.Min(tz0, tz1); tz <= Math.Max(tz0, tz1); tz++)
            {
                result.Add((tx, tz));
            }
        }

        return result;
    }

    private void SetBakeMessage(string message, long start)
    {
        lock (bakeLock)
        {
            bakeStatus.Message = message;
            bakeStatus.ElapsedSeconds = GetElapsedTime(start).TotalSeconds;
        }
    }

    /// <summary>
    /// Deletes every cached navmesh tile for a continent, including stale
    /// settings-hash directories from earlier bake parameters. A null continent
    /// clears the lot. Returns bytes freed.
    /// </summary>
    public long ClearNavmeshCache(string? continent)
    {
        if (bakeStatus.Running)
        {
            throw new InvalidOperationException("A bake is running; cancel it first.");
        }

        string root = dataConfig.Navmesh;
        if (!System.IO.Directory.Exists(root))
        {
            return 0;
        }

        // Drop the live mesh first so queries cannot serve tiles that are about
        // to disappear from disk.
        navmeshPathfinder?.Dispose();
        navmeshPathfinder = null;
        navmeshMapId = -1;

        long freed = 0;

        IEnumerable<string> dirs = continent is null
            ? System.IO.Directory.EnumerateDirectories(root)
            : [System.IO.Path.Join(root, continent)];

        foreach (string dir in dirs)
        {
            if (!System.IO.Directory.Exists(dir))
            {
                continue;
            }

            foreach (string file in System.IO.Directory.EnumerateFiles(dir, "*", System.IO.SearchOption.AllDirectories))
            {
                freed += new System.IO.FileInfo(file).Length;
            }

            System.IO.Directory.Delete(dir, recursive: true);
        }

        return freed;
    }

    /// <summary>One baked navmesh tile, with its world footprint.</summary>
    public readonly record struct NavmeshTileInfo(
        int X, int Z, float MinX, float MinY, float MaxX, float MaxY, bool Resident);

    private string NavmeshCacheDir(string continent)
    {
        // The era, not the client name: som, tbc and wrath share one bake.
        string hash = NavmeshSettings.ComputeSettingsHash(
            NavmeshSettings.MeshEra(dataConfig.Exp),
            typeof(NavmeshPathfinder).Assembly.GetName().Version?.ToString() ?? "0",
            bakeOptions.AgentRadius, bakeOptions.AgentMaxClimb, bakeOptions.WalkableSlope,
            bakeOptions.ResolveMinWorldZ(continent));

        return System.IO.Path.Join(dataConfig.Navmesh, continent, hash);
    }

    /// <summary>
    /// Every tile baked to disk for a continent, so the map can show which
    /// ground actually has a navmesh. Reads the cache directly, so it answers
    /// even before the engine has run a query; tiles also stitched into the
    /// live mesh right now are flagged Resident.
    /// </summary>
    public NavmeshTileInfo[] GetNavmeshTiles(string continent)
    {
        string dir = NavmeshCacheDir(continent);
        if (!System.IO.Directory.Exists(dir))
        {
            return [];
        }

        HashSet<(int x, int z)> live = [];
        if (navmeshPathfinder != null)
        {
            foreach ((int x, int z) in navmeshPathfinder.Tiles.ResidentTiles)
            {
                live.Add((x, z));
            }
        }

        List<NavmeshTileInfo> result = [];

        foreach (string file in System.IO.Directory.EnumerateFiles(dir, "tile_*.dnm"))
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(file);

            // tile_{x}_{z}
            string[] parts = name.Split('_');
            if (parts.Length != 3 ||
                !int.TryParse(parts[1], out int tx) ||
                !int.TryParse(parts[2], out int tz))
            {
                continue;
            }

            NavmeshCoords.GetTileWowBounds(tx, tz,
                out float minX, out float minY, out float maxX, out float maxY);

            result.Add(new NavmeshTileInfo(tx, tz, minX, minY, maxX, maxY, live.Contains((tx, tz))));
        }

        return [.. result];
    }

    private void EnsureNavmeshPathfinder()
    {
        if (navmeshPathfinder != null && navmeshMapId == search.MapId)
        {
            return;
        }

        navmeshPathfinder?.Dispose();

        string continent = ContinentDB.IdToName[search.MapId];
        string cacheDir = NavmeshCacheDir(continent);

        navmeshPathfinder = new NavmeshPathfinder(logger, TriangleWorld, cacheDir, bakeOptions, queryOptions,
            bakeOptions.ResolveMinWorldZ(continent));
        navmeshPathfinder.Zones = CostZoneLoader.Load(logger, dataConfig.Road, continent,
            roadCoreHalfWidth: queryOptions.RoadCore);
        navmeshPathfinder.Tiles.NotifyTileAdded =
            (x, z, data) => OnNavmeshTileAdded?.Invoke(x, z, data);
        navmeshPathfinder.Tiles.NotifyTileRemoved =
            (x, z) => OnNavmeshTileRemoved?.Invoke(x, z);
        navmeshMapId = search.MapId;

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Navmesh engine ready for {Continent} - tile cache: {CacheDir}", continent, cacheDir);
        }
    }

    public void Save()
    {
        long timestamp = GetTimestamp();

        search.PathGraph.Save();

        if (logger.IsEnabled(LogLevel.Trace))
            logger.LogTrace("Saved GraphChunks {ElapsedMs} ms", GetElapsedTime(timestamp).TotalMilliseconds);
    }

    public void SetLocations(Vector4 from, Vector4 to)
    {
        Initialise(from.W);

        search.From = from;
        search.Target = to;
    }

    public List<Vector3> GetCurrentSearchPath()
    {
        return search == null || search.PathGraph == null
            ? []
            : search.PathGraph.CurrentSearchPath();
    }

    public float TransformMapToWorld(int uiMapId, Vector3[] path)
    {
        float mapId = -1;

        Span<Vector3> span = path;
        for (int i = 0; i < span.Length; i++)
        {
            Vector3 p = span[i];
            if (p.Z != 0)
            {
                mapId = GetMapId(uiMapId);
                break;
            }

            Vector4 world = ToWorld(uiMapId, p.X, p.Y, p.Z);

            span[i] = world.AsVector3();
            mapId = world.W;
        }

        return mapId;
    }

    public void DrawPath(float mapId, ReadOnlySpan<Vector3> path)
    {
        Vector4 from = new(path[0], mapId);
        Vector4 to = new(path[^1], mapId);

        SetLocations(from, to);

        if (search.PathGraph == null)
        {
            search.CreatePathGraph(mapId);
        }

        List<Spot> spots = new(path.Length);
        for (int i = 0; i < path.Length; i++)
        {
            Spot spot = new(path[i]);
            spots.Add(spot);
            search.PathGraph.CreateSpotsAroundSpot(spot, false, spot);
        }

        OnPathCreated?.Invoke(new(spots));
    }

    /// <summary>
    /// Area id + walkable z at a world (x, y) without touching the game files:
    /// the area id comes from the pre-baked <see cref="AreaGrid"/> and z from the
    /// navmesh when a tile is baked for the point. Opens no MPQ archives, so it
    /// serves a runtime that ships only the navmesh + area-grid artifacts. z is
    /// best-effort - 0 when no navmesh covers the point.
    /// </summary>
    public (int areaId, float z) GetAreaIdAndZ(float mapId, float worldX, float worldY)
    {
        if (!ContinentDB.IdToName.TryGetValue(mapId, out string continent))
        {
            return (0, 0f);
        }

        int areaId = GetAreaGrid(continent)?.GetAreaId(worldX, worldY) ?? 0;

        float z = 0f;
        NavmeshPathfinder? nav = GetQueryNavmesh(continent, mapId);
        if (nav != null && nav.TryGetHeight(worldX, worldY, out float found))
        {
            z = found;
        }

        return (areaId, z);
    }

    /// <summary>
    /// Disk-only navmesh for a continent, used purely to answer height queries
    /// without the game files. Reuses the live pathfinding mesh when it already
    /// covers the continent; otherwise builds a world-less (never-baking) one
    /// over the baked tile cache. Null when nothing is baked for the continent.
    /// </summary>
    private NavmeshPathfinder? GetQueryNavmesh(string continent, float mapId)
    {
        if (navmeshPathfinder != null && navmeshMapId == mapId)
        {
            return navmeshPathfinder;
        }

        lock (areaGridLock)
        {
            if (queryNavmesh.TryGetValue(continent, out NavmeshPathfinder? nav))
            {
                return nav;
            }

            string cacheDir = NavmeshCacheDir(continent);
            if (!System.IO.Directory.Exists(cacheDir))
            {
                queryNavmesh[continent] = null;
                logger.LogWarning(
                    "No baked navmesh for {Continent} ({CacheDir}); z unavailable. Bake it with --bake={Continent}.",
                    continent, cacheDir, continent);
                return null;
            }

            nav = new NavmeshPathfinder(logger, null, cacheDir, bakeOptions, queryOptions,
                bakeOptions.ResolveMinWorldZ(continent));
            nav.Zones = CostZoneLoader.Load(logger, dataConfig.Road, continent,
                roadCoreHalfWidth: queryOptions.RoadCore);
            queryNavmesh[continent] = nav;
            return nav;
        }
    }

    private AreaGrid? GetAreaGrid(string continent)
    {
        lock (areaGridLock)
        {
            if (areaGrids.TryGetValue(continent, out AreaGrid? grid))
            {
                return grid;
            }

            string path = System.IO.Path.Combine(dataConfig.AreaGrid, continent + ".grid");
            grid = AreaGrid.Load(path);
            areaGrids[continent] = grid;

            if (grid == null)
            {
                logger.LogWarning(
                    "Area grid missing for {Continent} ({Path}) - area ids unavailable. Bake it with --bake-area={Continent}.",
                    continent, path, continent);
            }

            return grid;
        }
    }

    private static readonly System.Text.Json.JsonSerializerOptions subZoneJsonOptions = new()
    {
        Converters = { new SharedLib.Converters.Vector3Converter(true) }
    };

    /// <summary>The world continents `--bake-area=all` covers (skips instances).</summary>
    private static readonly string[] WorldContinents =
        ["Azeroth", "Kalimdor", "Expansion01", "Northrend"];

    /// <summary>
    /// Bakes, per continent, from the game files: the standalone area-id grid
    /// (<see cref="DataConfig.AreaGrid"/>) queried at runtime without the client,
    /// and the per-area world bounds (<see cref="DataConfig.Subzones"/>, keyed by
    /// map id) consumed by the WorldMapArea generator to disambiguate overlapping
    /// zones. A null continent bakes all.
    /// </summary>
    public bool BuildAreaGrid(string? continent)
    {
        List<string> continents = continent is null
            ? [.. WorldContinents]
            : [continent];

        foreach (string name in continents)
        {
            if (!ContinentDB.NameToId.TryGetValue(name, out float mapId))
            {
                logger.LogWarning("Area data bake: unknown continent {Continent}", name);
                continue;
            }

            Initialise(mapId);
            (AreaGrid grid, SubZoneArea[] subZones) = search.BuildAreaData();

            string gridPath = System.IO.Path.Combine(dataConfig.AreaGrid, name + ".grid");
            grid.Save(gridPath);

            lock (areaGridLock)
            {
                areaGrids[name] = grid;
            }

            System.IO.Directory.CreateDirectory(dataConfig.Subzones);
            string subZonePath = System.IO.Path.Combine(dataConfig.Subzones, $"{(int)mapId}.json");
            System.IO.File.WriteAllText(subZonePath,
                System.Text.Json.JsonSerializer.Serialize(subZones, subZoneJsonOptions));

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Area data baked for {Continent}: grid {Width}x{Height}, {SubZones} areas -> {GridPath}",
                    name, grid.Width, grid.Height, subZones.Length, gridPath);
            }
        }

        return true;
    }
}