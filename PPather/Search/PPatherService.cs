using Microsoft.Extensions.Logging;

using PPather.Data;
using PPather.Graph;

using SharedLib;
using SharedLib.Data;

using System;
using System.Collections.Generic;
using static System.Diagnostics.Stopwatch;
using System.Numerics;

using WowTriangles;

namespace PPather;

public sealed class PPatherService
{
    private readonly ILogger<PPatherService> logger;
    private readonly DataConfig dataConfig;
    private readonly WorldMapAreaDB worldMapAreaDB;

    public event Action SearchBegin;
    public event Action<Path> OnPathCreated;
    public event Action<ChunkEventArgs> OnChunkAdded;

    public Action<LinesEventArgs> OnLinesAdded;
    public Action<SphereEventArgs> OnSphereAdded;

    private Search search { get; set; }

    public bool Initialised => search != null;

    public bool IsSearching { get; set; }

    public Vector4 SearchFrom => search.From;
    public Vector4 SearchTo => search.Target;
    public Vector3 ClosestLocation => search?.PathGraph?.ClosestSpot?.Loc ?? Vector3.Zero;
    public Vector3 PeekLocation => search?.PathGraph?.PeekSpot?.Loc ?? Vector3.Zero;

    public HashSet<Vector4> TestPoints => search?.PathGraph?.TestPoints ?? [];

    public HashSet<Vector3> BlockedPoints => search?.PathGraph?.BlockedPoints ?? [];

    public PPatherService(ILogger<PPatherService> logger, DataConfig dataConfig, WorldMapAreaDB worldMapAreaDB)
    {
        this.dataConfig = dataConfig;
        this.logger = logger;
        this.worldMapAreaDB = worldMapAreaDB;
        ContinentDB.Init(worldMapAreaDB.Values);

        MPQSelfTest();
    }

    public void Reset()
    {
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
        var path = search.DoSearch(searchType);
        IsSearching = false;
        OnPathCreated?.Invoke(path);
        return path;
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

    public (int, float) GetAreaIdAndZ(Vector3 location)
    {
        return search.GetAreaIdAndZ(location);
    }
}