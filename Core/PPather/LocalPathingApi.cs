using Microsoft.Extensions.Logging;

using PPather;
using PPather.Data;
using PPather.Graph;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;

#pragma warning disable 162

namespace Core;

public sealed class LocalPathingApi : IPPather
{
    private const bool debug = false;

    private const SearchStrategy searchStrategy = SearchStrategy.A_Star_With_Model_Avoidance;

    private readonly ILogger<LocalPathingApi> logger;

    private readonly PPatherService service;

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

        service.SetLocations(
            service.ToWorld(uiMap, mapFrom.X, mapFrom.Y, mapFrom.Z),
            service.ToWorld(uiMap, mapTo.X, mapTo.Y));

        Path path = service.DoSearch(searchStrategy);
        if (path == null)
        {
            if (debug)
                logger.LogWarning("Failed to find a path from {MapFrom} to {MapTo} took {ElapsedMs} ms.", mapFrom, mapTo, Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds);

            return Array.Empty<Vector3>();
        }

        if (debug)
            logger.LogDebug("Finding route from {MapFrom} map {UiMap} to {MapTo} took {ElapsedMs} ms.", mapFrom, uiMap, mapTo, Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds);

        if ((DateTime.UtcNow - lastSave).TotalMinutes >= 1)
        {
            service.Save();
            lastSave = DateTime.UtcNow;
        }

        for (int i = 0; i < path.locations.Count; i++)
        {
            path.locations[i] = service.ToLocal(path.locations[i], (int)service.SearchFrom.W, uiMap);
        }
        return path.locations.ToArray();
    }

    public Vector3[] FindWorldRoute(int uiMap, bool startIndoors, Vector3 worldFrom, Vector3 worldTo)
    {
        long timestamp = Stopwatch.GetTimestamp();

        service.SetLocations(
            service.ToWorldZ(uiMap, worldFrom.X, worldFrom.Y, worldFrom.Z, startIndoors),
            service.ToWorldZ(uiMap, worldTo.X, worldTo.Y, worldTo.Z));

        Path path = service.DoSearch(searchStrategy);
        if (path == null)
        {
            if (debug)
                logger.LogWarning("Failed to find a path from {WorldFrom} to {WorldTo} took {ElapsedMs} ms.", worldFrom, worldTo, Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds);

            return Array.Empty<Vector3>();
        }

        if (debug)
            logger.LogDebug("Finding route from {WorldFrom} map {UiMap} to {WorldTo} took {ElapsedMs} ms.", worldFrom, uiMap, worldTo, Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds);

        if ((DateTime.UtcNow - lastSave).TotalMinutes >= 1)
        {
            service.Save();
            lastSave = DateTime.UtcNow;
        }

        return path.locations.ToArray();
    }
}