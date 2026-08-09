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

    /// <summary>
    /// Geometry-backed search (MPQ triangle world + PathGraph). Null in a
    /// disk-only run: see <see cref="geometryUnavailable"/>.
    /// </summary>
    private Search? search { get; set; }

    private NavmeshPathfinder navmeshPathfinder;
    private float navmeshMapId = -1;
    private bool? lastStartIndoors;

    /// <summary>
    /// Continent of the active query, tracked here rather than read off
    /// <see cref="search"/> so a disk-only run still knows where it is.
    /// </summary>
    private float activeMapId = -1;

    private Vector4 activeFrom;
    private Vector4 activeTarget;

    /// <summary>
    /// Continents whose geometry could not be opened - no client archives
    /// installed, or archives that do not carry this continent (a wrath client
    /// has no Pandaria). The navmesh answers from baked tiles alone, so this is
    /// a normal operating mode, not a failure: post-Cata clients are tens of GB
    /// and there is no reason to keep one installed just to path over tiles that
    /// are already baked. Remembered per continent so a missing client costs one
    /// archive-open attempt instead of one per query.
    /// </summary>
    private readonly HashSet<float> geometryUnavailable = [];

    /// <summary>True when this continent is pathing off baked tiles with no game files.</summary>
    public bool DiskOnly => search == null && activeMapId >= 0;

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
        if (navmeshPathfinder == null || activeMapId < 0)
        {
            return false;
        }

        string continent = ContinentDB.IdToName[activeMapId];

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

    /// <summary>A continent is active and queries can be answered - with or without geometry.</summary>
    public bool Initialised => activeMapId >= 0;

    public bool IsSearching { get; set; }

    public Vector4 SearchFrom => activeFrom;
    public Vector4 SearchTo => activeTarget;
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
        activeMapId = -1;

        if (search == null)
            return;

        search.Clear();
        search = null;
    }

    public void Initialise(float mapId)
    {
        if (activeMapId == mapId && (search != null || geometryUnavailable.Contains(mapId)))
        {
            return;
        }

        if (activeMapId >= 0 && activeMapId != mapId)
        {
            Reset();
        }

        activeMapId = mapId;

        if (geometryUnavailable.Contains(mapId))
        {
            return;
        }

        try
        {
            search = new Search(mapId, logger, dataConfig);
            search.PathGraph.triangleWorld.NotifyChunkAdded = ChunkAdded;
        }
        catch (Exception e) when (Engine == PathingEngine.Navmesh)
        {
            // Disk-only fallback. The navmesh engine reads baked tiles straight
            // off disk and never needs the client, so a missing or wrong-era
            // archive set is recoverable here - but only for Navmesh: SpotAStar
            // walks the triangle world itself and has nothing to fall back to,
            // hence the `when` filter rethrowing for it.
            geometryUnavailable.Add(mapId);
            search = null;

            logger.LogWarning(
                "No usable geometry for {Continent} ({Message}). Pathing disk-only over baked navmesh tiles; " +
                "baking and SpotAStar are unavailable for this continent.",
                ContinentDB.IdToName.TryGetValue(mapId, out string? name) ? name : mapId.ToString(),
                e.Message);
        }
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

    /// <summary>Baked navmesh tiles on disk for one continent of the active era.</summary>
    public readonly record struct NavmeshCoverage(string Continent, string Hash, string CacheDir, int Tiles);

    /// <summary>
    /// What <see cref="SelfTest"/> found. <see cref="Ok"/> is the single yes/no; the rest
    /// is there so a failure says which era and which directory were looked at, instead of
    /// a bare false.
    /// </summary>
    public sealed record SelfTestReport(
        bool Ok, string Engine, string Exp, string Era, string Detail,
        bool MpqPresent, int TotalTiles, NavmeshCoverage[] Continents);

    /// <summary>
    /// Per-continent baked tile counts for the active era. Counts files only - never loads
    /// a mesh - so it is cheap enough for a health endpoint.
    /// </summary>
    public NavmeshCoverage[] GetNavmeshCoverage()
    {
        List<string> continents = KnownWorldContinents();
        NavmeshCoverage[] result = new NavmeshCoverage[continents.Count];

        for (int i = 0; i < continents.Count; i++)
        {
            string dir = NavmeshCacheDir(continents[i]);
            int tiles = System.IO.Directory.Exists(dir)
                ? System.IO.Directory.EnumerateFiles(dir, "*.dnm").Count()
                : 0;

            result[i] = new(continents[i], System.IO.Path.GetFileName(dir), dir, tiles);
        }

        return result;
    }

    /// <summary>
    /// Can this install actually path? The answer depends on the engine, which is why it
    /// is not <see cref="MPQSelfTest"/> any more: the navmesh engine reads baked tiles and
    /// treats game archives as optional, so on a tiles-only install (or any CASC client)
    /// the MPQ check reports a failure that does not exist. SpotAStar has no tile cache to
    /// fall back on, so for it the archives really are the answer.
    /// </summary>
    public SelfTestReport SelfTest()
    {
        string era = DataConfig.ClientEra(dataConfig.Exp);
        bool mpq = MPQTriangleSupplier.GetArchiveNames(dataConfig).Length > 0;

        if (Engine != PathingEngine.Navmesh)
        {
            return new(mpq, Engine.ToString(), dataConfig.Exp, era,
                mpq
                    ? "SpotAStar: game archives present."
                    : $"SpotAStar needs game archives in {dataConfig.MPQ}; none found.",
                mpq, 0, []);
        }

        NavmeshCoverage[] coverage = GetNavmeshCoverage();
        int total = 0;
        for (int i = 0; i < coverage.Length; i++)
        {
            total += coverage[i].Tiles;
        }

        if (total == 0)
        {
            string detail =
                $"No baked navmesh tiles for era '{era}' under {dataConfig.Navmesh}. " +
                $"Download them (scripts/download-navmesh.ps1 -Era {era}) or bake with --bake=all.";

            logger.LogWarning("Navmesh self-test FAILED: {Detail}", detail);
            return new(false, Engine.ToString(), dataConfig.Exp, era, detail, mpq, 0, coverage);
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            foreach (NavmeshCoverage c in coverage)
            {
                logger.LogInformation("Navmesh {Era}/{Continent}/{Hash}: {Tiles} tiles",
                    era, c.Continent, c.Hash, c.Tiles);
            }
        }

        int missing = 0;
        for (int i = 0; i < coverage.Length; i++)
        {
            if (coverage[i].Tiles == 0)
                missing++;
        }

        return new(true, Engine.ToString(), dataConfig.Exp, era,
            missing == 0
                ? $"{total} tiles across {coverage.Length} continent(s)."
                : $"{total} tiles; {missing} of {coverage.Length} continent(s) have none baked.",
            mpq, total, coverage);
    }

    public TriangleCollection GetChunkAt(int grid_x, int grid_y)
    {
        return RequireGeometry().PathGraph.triangleWorld.GetChunkAt(grid_x, grid_y);
    }

    /// <summary>Triangle world of the active continent; null in a disk-only run.</summary>
    public ChunkedTriangleCollection? TriangleWorld => search?.PathGraph.triangleWorld;

    public IEnumerable<Spot> GetSpots()
    {
        return search == null ? [] : search.PathGraph.SpotManager.AllSpots();
    }

    /// <summary>
    /// The geometry-backed search, or a clear failure. For the operations that
    /// genuinely cannot work off baked tiles alone - baking, SpotAStar, spot
    /// graph authoring - so they report the missing client instead of throwing
    /// NullReferenceException somewhere deeper.
    /// </summary>
    private Search RequireGeometry() =>
        search ?? throw new InvalidOperationException(
            $"This operation needs the game archives for " +
            $"{(ContinentDB.IdToName.TryGetValue(activeMapId, out string? n) ? n : activeMapId.ToString())}, " +
            $"which are not available in {dataConfig.MPQ}. Baked navmesh tiles alone " +
            $"support path and height queries, not baking or SpotAStar.");

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

        return search?.CreateWorldLocation(worldX, worldY, z, wma.MapID, null)
            ?? NavmeshWorldLocation(worldX, worldY, z, wma.MapID);
    }

    public Vector4 ToWorldZ(int uiMap, float x, float y, float z, bool? startIndoors = null)
    {
        if (!worldMapAreaDB.TryGet(uiMap, out WorldMapArea wma))
            return Vector4.Zero;

        Initialise(wma.MapID);

        lastStartIndoors = startIndoors;

        return search?.CreateWorldLocation(x, y, z, wma.MapID, startIndoors)
            ?? NavmeshWorldLocation(x, y, z, wma.MapID);
    }

    /// <summary>
    /// Surface height from the baked navmesh, for when there is no triangle world
    /// to run <see cref="Search.CreateWorldLocation"/> against. Coarser than the
    /// geometry path - it cannot apply the canopy/indoor heuristics, only the
    /// walkable surface the navmesh already encodes - which is the right trade
    /// when the alternative is no answer at all. A non-zero caller z is kept as
    /// given, matching the geometry path's treatment of an explicit height.
    /// </summary>
    private Vector4 NavmeshWorldLocation(float worldX, float worldY, float z, float mapId)
    {
        if (z != 0)
        {
            return new(worldX, worldY, z, mapId);
        }

        string continent = ContinentDB.IdToName[mapId];
        NavmeshPathfinder? nav = GetQueryNavmesh(continent, mapId);

        return nav != null && nav.TryGetHeight(worldX, worldY, out float found)
            ? new(worldX, worldY, found, mapId)
            : new(worldX, worldY, 0, mapId);
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
            : RequireGeometry().DoSearch(searchType);

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

        Vector3 from = activeFrom.AsVector3();
        Vector3 to = activeTarget.AsVector3();

        // Callers that bypass ToWorldZ (raw WorldRoute) pass z=0. The navmesh
        // column scan alone would pick the highest poly - which can be a tree
        // canopy or roof. Seed the height with the spot-geometry surface
        // heuristics (canopy/terrain preference) first; the resolver then only
        // needs its small vertical extents. Disk-only runs have no geometry to
        // ask, so they fall back to the navmesh surface via TryGetHeight.
        if (from.Z == 0)
        {
            from = search?.CreateWorldLocation(from.X, from.Y, 0, (int)activeMapId, lastStartIndoors).AsVector3()
                ?? NavmeshWorldLocation(from.X, from.Y, 0, activeMapId).AsVector3();
        }

        if (to.Z == 0)
        {
            to = search?.CreateWorldLocation(to.X, to.Y, 0, (int)activeMapId, null).AsVector3()
                ?? NavmeshWorldLocation(to.X, to.Y, 0, activeMapId).AsVector3();
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
                ? KnownWorldContinents()
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

        // Baking reads ADT geometry, so unlike querying it cannot run off the
        // tile cache. Fail here with the reason rather than inside the loop.
        _ = RequireGeometry();

        EnsureNavmeshPathfinder();

        NavmeshTileCache tiles = navmeshPathfinder!.Tiles;

        List<(int x, int y)> adts = adt.HasValue
            ? [adt.Value]
            : TriangleWorld!.ExistingAdts();

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
        if (navmeshPathfinder != null && navmeshMapId == activeMapId)
        {
            return;
        }

        navmeshPathfinder?.Dispose();

        string continent = ContinentDB.IdToName[activeMapId];
        string cacheDir = NavmeshCacheDir(continent);

        navmeshPathfinder = new NavmeshPathfinder(logger, TriangleWorld, cacheDir, bakeOptions, queryOptions,
            bakeOptions.ResolveMinWorldZ(continent));
        navmeshPathfinder.Zones = CostZoneLoader.Load(logger, dataConfig.Road, continent,
            roadCoreHalfWidth: queryOptions.RoadCore);
        navmeshPathfinder.Tiles.NotifyTileAdded =
            (x, z, data) => OnNavmeshTileAdded?.Invoke(x, z, data);
        navmeshPathfinder.Tiles.NotifyTileRemoved =
            (x, z) => OnNavmeshTileRemoved?.Invoke(x, z);
        navmeshMapId = activeMapId;

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Navmesh engine ready for {Continent} ({Mode}) - tile cache: {CacheDir}",
                continent, search == null ? "disk-only, no game files" : "geometry available", cacheDir);
        }
    }

    public void Save()
    {
        // Nothing to persist without a PathGraph - the navmesh tile cache is
        // already on disk.
        if (search == null)
        {
            return;
        }

        long timestamp = GetTimestamp();

        search.PathGraph.Save();

        if (logger.IsEnabled(LogLevel.Trace))
            logger.LogTrace("Saved GraphChunks {ElapsedMs} ms", GetElapsedTime(timestamp).TotalMilliseconds);
    }

    public void SetLocations(Vector4 from, Vector4 to)
    {
        Initialise(from.W);

        activeFrom = from;
        activeTarget = to;

        if (search != null)
        {
            search.From = from;
            search.Target = to;
        }
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

        Search geometry = RequireGeometry();

        if (geometry.PathGraph == null)
        {
            geometry.CreatePathGraph(mapId);
        }

        List<Spot> spots = new(path.Length);
        for (int i = 0; i < path.Length; i++)
        {
            Spot spot = new(path[i]);
            spots.Add(spot);
            geometry.PathGraph.CreateSpotsAroundSpot(spot, false, spot);
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
    /// Area id at a world (x, y) from the pre-baked grid alone. The cheap half of
    /// <see cref="GetAreaIdAndZ"/> - route generation tests thousands of points for zone
    /// membership and has no use for the height query that goes with it.
    /// </summary>
    public int GetAreaId(float mapId, float worldX, float worldY)
    {
        return ContinentDB.IdToName.TryGetValue(mapId, out string continent)
            ? GetAreaGrid(continent)?.GetAreaId(worldX, worldY) ?? 0
            : 0;
    }

    /// <summary>
    /// The disk-only navmesh for a map, for callers that want to sample or query it
    /// directly (route generation). Null when nothing is baked for the continent.
    ///
    /// <para>Deliberately the query mesh rather than <see cref="NavmeshPathfinder"/>: it
    /// never bakes and never touches the live search state, so a generator can run without
    /// disturbing - or being disturbed by - the follower's own requests.</para>
    /// </summary>
    public NavmeshPathfinder? GetQueryNavmeshForMap(float mapId)
    {
        return ContinentDB.IdToName.TryGetValue(mapId, out string continent)
            ? GetQueryNavmesh(continent, mapId)
            : null;
    }

    /// <summary>
    /// Connected components of the navmesh polygons covering a world-space AABB, so a
    /// caller can answer "is this point reachable from that one" with an array read instead
    /// of a pathfinding query. See <see cref="NavmeshConnectivity"/>.
    ///
    /// <para>Loads the covering tiles first - a tile that is not resident contributes no
    /// polygons, and its absence would read as "unreachable" rather than as missing data.</para>
    /// </summary>
    public NavmeshConnectivity? BuildConnectivity(float mapId,
        float minWorldX, float minWorldY, float maxWorldX, float maxWorldY)
    {
        NavmeshPathfinder? nav = GetQueryNavmeshForMap(mapId);
        if (nav == null)
            return null;

        NavmeshCoords.GetTileIndex(minWorldX, minWorldY, out int tx0, out int tz0);
        NavmeshCoords.GetTileIndex(maxWorldX, maxWorldY, out int tx1, out int tz1);

        // World x/y descend as detour tile indices ascend, so the corners can arrive in
        // either order; normalise rather than assume. One tile of skirt keeps a component
        // that only connects through the fringe from looking severed.
        int minTileX = System.Math.Min(tx0, tx1) - 1;
        int maxTileX = System.Math.Max(tx0, tx1) + 1;
        int minTileZ = System.Math.Min(tz0, tz1) - 1;
        int maxTileZ = System.Math.Max(tz0, tz1) + 1;

        for (int tx = minTileX; tx <= maxTileX; tx++)
        {
            for (int tz = minTileZ; tz <= maxTileZ; tz++)
            {
                if (!NavmeshCoords.IsValidTile(tx, tz) || !nav.Tiles.IsTileOnDisk(tx, tz))
                    continue;

                NavmeshCoords.GetTileWowBounds(tx, tz,
                    out float bMinX, out float bMinY, out float bMaxX, out float bMaxY);

                // Guarded by IsTileOnDisk above, so this only stitches in what is already
                // baked - it never triggers a bake, which would need the game archives and
                // would be wildly out of budget here.
                Vector3 centre = new((bMinX + bMaxX) / 2f, (bMinY + bMaxY) / 2f, 0f);
                nav.Tiles.EnsureTilesForSegment(centre, centre);
            }
        }

        return NavmeshConnectivity.Build(logger, nav.Tiles,
            minTileX, minTileZ, maxTileX, maxTileZ);
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

    /// <summary>
    /// The world continents an "all" bake covers (skips instances). Superset
    /// across every supported client - HawaiiMainLand is Pandaria, which only a
    /// Mists client has - so it is filtered per client by
    /// <see cref="KnownWorldContinents"/> rather than used directly.
    /// </summary>
    private static readonly string[] WorldContinents =
    [
        "Azeroth", "Kalimdor", "Expansion01", "Northrend",
        "HawaiiMainLand",    // 870 Pandaria                    - Mists
        "Deephome",          // 646 Deepholm                    - Cataclysm
        "LostIsles",         // 648 The Lost Isles + Kezan      - Cataclysm (goblin start)
        "Gilneas2",          // 654 Gilneas                     - Cataclysm (worgen start)
        "MaelstromZone",     // 730 The Maelstrom               - Cataclysm
        "NewRaceStartZone",  // 860 The Wandering Isle          - Mists (pandaren start)
    ];

    /// <summary>
    /// The subset of <see cref="WorldContinents"/> the loaded client actually
    /// has. Without this an "all" bake throws on the first continent the client
    /// does not know - Pandaria on anything pre-Mists, and equally Northrend on a
    /// vanilla client.
    /// </summary>
    private static List<string> KnownWorldContinents()
    {
        List<string> known = new(WorldContinents.Length);

        foreach (string name in WorldContinents)
        {
            if (ContinentDB.NameToId.ContainsKey(name))
                known.Add(name);
        }

        return known;
    }

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
            ? KnownWorldContinents()
            : [continent];

        foreach (string name in continents)
        {
            if (!ContinentDB.NameToId.TryGetValue(name, out float mapId))
            {
                logger.LogWarning("Area data bake: unknown continent {Continent}", name);
                continue;
            }

            Initialise(mapId);

            if (search == null)
            {
                // Area data comes from ADT area ids, not from the navmesh, so
                // there is nothing to extract without the client archives.
                logger.LogWarning(
                    "Area data bake skipped for {Continent}: no game archives in {MPQ}.",
                    name, dataConfig.MPQ);
                continue;
            }

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