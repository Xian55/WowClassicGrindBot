using Microsoft.Extensions.Logging;

using PPather;
using PPather.Data;
using PPather.Graph;

using SharedLib;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;

#pragma warning disable 162

namespace Core;

public sealed class LocalPathingApi : IPPather
{
    public bool PathsAreSmoothed => service.Engine == PathingEngine.Navmesh;

    private const bool debug = false;

    private const SearchStrategy searchStrategy = SearchStrategy.A_Star_With_Model_Avoidance;

    private readonly ILogger<LocalPathingApi> logger;

    private readonly PPatherService service;

    /// <summary>
    /// Serializes the pather's two-call search protocol. Route generation added a second
    /// caller on a different thread; before that the single caller made this unnecessary.
    /// </summary>
    private readonly System.Threading.Lock searchLock = new();

    private DateTime lastSave;

    public LocalPathingApi(ILogger<LocalPathingApi> logger,
        PPatherService service)
    {
        this.logger = logger;
        this.service = service;
    }

    public ValueTask DrawLines(List<LineArgs> lineArgs)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask DrawSphere(SphereArgs args)
    {
        return ValueTask.CompletedTask;
    }

    public Vector3[] FindMapRoute(int uiMap, Vector3 mapFrom, Vector3 mapTo)
    {
        long timestamp = Stopwatch.GetTimestamp();

        Path path;
        float searchFromMapId;

        // See FindWorldRoute. SearchFrom is captured inside the lock too - it belongs to
        // the search that just ran, and another caller's SetLocations would replace it.
        lock (searchLock)
        {
            service.SetLocations(
                service.ToWorld(uiMap, mapFrom.X, mapFrom.Y, mapFrom.Z),
                service.ToWorld(uiMap, mapTo.X, mapTo.Y));

            path = service.DoSearch(searchStrategy);
            searchFromMapId = service.SearchFrom.W;
        }

        if (path == null)
        {
            if (debug)
                logger.LogWarning("Failed to find a path from {MapFrom} to {MapTo} took {ElapsedMs} ms.", mapFrom, mapTo, Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds);

            return Array.Empty<Vector3>();
        }

        if (debug && logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Finding route from {MapFrom} map {UiMap} to {MapTo} took {ElapsedMs} ms.", mapFrom, uiMap, mapTo, Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds);

        if ((DateTime.UtcNow - lastSave).TotalMinutes >= 1)
        {
            service.Save();
            lastSave = DateTime.UtcNow;
        }

        for (int i = 0; i < path.locations.Count; i++)
        {
            path.locations[i] = service.ToLocal(path.locations[i], (int)searchFromMapId, uiMap);
        }
        return path.locations.ToArray();
    }

    public Vector3[] FindWorldRoute(int uiMap, bool startIndoors, Vector3 worldFrom, Vector3 worldTo)
    {
        long timestamp = Stopwatch.GetTimestamp();

        Path path;

        // SetLocations/DoSearch is a stateful two-call protocol on a non-reentrant
        // singleton (see PPather/CLAUDE.md). Until now the only caller was Navigation's
        // PathFinderThread, so it was serialized by accident; route generation runs on the
        // bot thread, which would interleave the two calls and search between the wrong
        // endpoints.
        lock (searchLock)
        {
            service.SetLocations(
                service.ToWorldZ(uiMap, worldFrom.X, worldFrom.Y, worldFrom.Z, startIndoors),
                service.ToWorldZ(uiMap, worldTo.X, worldTo.Y, worldTo.Z));

            path = service.DoSearch(searchStrategy);
        }

        if (path == null)
        {
            if (debug)
                logger.LogWarning("Failed to find a path from {WorldFrom} to {WorldTo} took {ElapsedMs} ms.", worldFrom, worldTo, Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds);

            return Array.Empty<Vector3>();
        }

        if (debug && logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Finding route from {WorldFrom} map {UiMap} to {WorldTo} took {ElapsedMs} ms.", worldFrom, uiMap, worldTo, Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds);

        if ((DateTime.UtcNow - lastSave).TotalMinutes >= 1)
        {
            service.Save();
            lastSave = DateTime.UtcNow;
        }

        return path.locations.ToArray();
    }
}