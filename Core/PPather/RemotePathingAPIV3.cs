using AnTCP.Client;

using Microsoft.Extensions.Logging;

using PPather;
using PPather.Data;

using SharedLib;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable 162

namespace Core;

public sealed class RemotePathingAPIV3 : IPPather, IDisposable
{
    private const bool debug = false;
    private const int watchdogPollMs = 500;

    private const EMessageType TYPE = EMessageType.PATH;
    private const PathRequestFlags FLAGS = PathRequestFlags.SMOOTH_CATMULLROM | PathRequestFlags.VALIDATE_CPOP;

    private enum EMessageType
    {
        PATH,                   // Generate a simple straight path
        MOVE_ALONG_SURFACE,     // Move an entity by small deltas using pathfinding (usefull to prevent falling off edges...)
        RANDOM_POINT,           // Get a random point on the mesh
        RANDOM_POINT_AROUND,    // Get a random point on the mesh in a circle
        CAST_RAY,               // Cast a movement ray to test for obstacles
        RANDOM_PATH,            // Generate a straight path where the nodes get offsetted by a random value
        EXPLORE_POLY,           // Generate a route to explore the polygon (W.I.P)
        CONFIGURE_FILTER,       // Cpnfigure the clients dtQueryFilter area costs
    }

    private enum PathRequestFlags
    {
        NONE = 0,
        SMOOTH_CHAIKIN = 1 << 0,        // Smooth path using Chaikin Curve
        SMOOTH_CATMULLROM = 1 << 1,     // Smooth path using Catmull-Rom Spline
        SMOOTH_BEZIERCURVE = 1 << 2,    // Smooth path using Bezier Curve
        VALIDATE_CPOP = 1 << 3,         // Validate smoothed path using closestPointOnPoly
        VALIDATE_MAS = 1 << 4,          // Validate smoothed path using moveAlongSurface
    };

    private readonly ILogger<RemotePathingAPIV3> logger;
    private readonly WorldMapAreaDB areaDB;

    private readonly AnTcpClient client;
    private readonly Thread connectionWatchdog;
    private readonly CancellationTokenSource cts;

    private readonly IPathVizualizer pathViz;

    private int uiMap;
    private Vector3[] result = Array.Empty<Vector3>();

    public RemotePathingAPIV3(
        IPathVizualizer pathViz,
        ILogger<RemotePathingAPIV3> logger,
        string ip, int port, WorldMapAreaDB areaDB)
    {
        this.logger = logger;
        this.areaDB = areaDB;
        this.pathViz = pathViz;

        cts = new();

        client = new AnTcpClient(ip, port);
        connectionWatchdog = new Thread(ObserveConnection);
        connectionWatchdog.Start();
    }

    public void Dispose()
    {
        RequestDisconnect();
    }

    public ValueTask DrawLines(List<LineArgs> lineArgs)
    {
        if (pathViz is NoPathVisualizer || result == Array.Empty<Vector3>())
            return ValueTask.CompletedTask;

        StringContent content =
            new(JsonSerializer.Serialize(new DrawMapPathRequest(uiMap, result), pathViz.Options),
            Encoding.UTF8, "application/json");

        pathViz.DrawLines(lineArgs).AsTask().Wait();

        return new(pathViz.Client.PostAsync("DrawMapPath", content));
    }

    public ValueTask DrawSphere(SphereArgs args)
    {
        if (pathViz is NoPathVisualizer)
            return ValueTask.CompletedTask;

        return pathViz.DrawSphere(args);
    }

    public Vector3[] FindMapRoute(int uiMap, Vector3 mapFrom, Vector3 mapTo)
    {
        if (!client.IsConnected ||
            !areaDB.TryGet(uiMap, out WorldMapArea area))
            return result = Array.Empty<Vector3>();

        try
        {
            Vector3 worldFrom = areaDB.ToWorld_FlipXY(uiMap, mapFrom);
            Vector3 worldTo = areaDB.ToWorld_FlipXY(uiMap, mapTo);

            // incase haven't asked a pathfinder for a route this value will be 0
            // that case use the highest location
            if (worldFrom.Z == 0)
            {
                worldFrom.Z = area.LocTop / 2;
                worldTo.Z = area.LocTop / 2;
            }

            if (debug)
                logger.LogDebug("Finding map route from {MapFrom}({WorldFrom}) map {UiMap} to {MapTo}({WorldTo}) map {UiMap2}...", mapFrom, worldFrom, uiMap, mapTo, worldTo, uiMap);

            Vector3[] path = client.Send(
                (byte)TYPE,
                (area.MapID, FLAGS,
                worldFrom.X, worldFrom.Y, worldFrom.Z, worldTo.X, worldTo.Y, worldTo.Z)).AsArray<Vector3>();

            if (path.Length == 1 && path[0] == Vector3.Zero)
                return result = Array.Empty<Vector3>();

            for (int i = 0; i < path.Length; i++)
            {
                if (debug)
                    logger.LogDebug("new float[] {{ {X}f, {Y}f, {Z}f }},", path[i].X, path[i].Y, path[i].Z);

                path[i] = areaDB.ToMap_FlipXY(path[i], area.MapID, uiMap);
            }

            return result = path;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Finding map route from {MapFrom} to {MapTo}", mapFrom, mapTo);
            return result = Array.Empty<Vector3>();
        }
    }

    public Vector3[] FindWorldRoute(int uiMap, bool startIndoors, Vector3 worldFrom, Vector3 worldTo)
    {
        if (!client.IsConnected)
            return result = Array.Empty<Vector3>();

        if (!areaDB.TryGet(uiMap, out WorldMapArea area))
            return result = Array.Empty<Vector3>();

        this.uiMap = uiMap;

        try
        {
            // incase haven't asked a pathfinder for a route this value will be 0
            // that case use the highest location
            if (worldFrom.Z == 0)
            {
                worldFrom.Z = area.LocTop / 2;
                worldTo.Z = area.LocTop / 2;
            }

            if (debug)
                logger.LogDebug("Finding world route from {WorldFrom}({WorldFrom2}) map {UiMap} to {WorldTo}({WorldTo2}) map {UiMap2}...", worldFrom, worldFrom, uiMap, worldTo, worldTo, uiMap);

            Vector3[] path = client.Send(
                (byte)TYPE,
                (area.MapID, FLAGS,
                worldFrom.X, worldFrom.Y, worldFrom.Z, worldTo.X, worldTo.Y, worldTo.Z)).AsArray<Vector3>();

            if (path.Length == 1 && path[0] == Vector3.Zero)
                return result = Array.Empty<Vector3>();

            return result = path;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Finding world route from {WorldFrom} to {WorldTo}", worldFrom, worldTo);
            return result = Array.Empty<Vector3>();
        }
    }


    public bool PingServer()
    {
        using CancellationTokenSource cts = new();
        cts.CancelAfter(watchdogPollMs);

        while (!cts.IsCancellationRequested)
        {
            if (client.IsConnected)
            {
                break;
            }
            cts.Token.WaitHandle.WaitOne(watchdogPollMs / 10);
        }

        return client.IsConnected;
    }

    private void RequestDisconnect()
    {
        cts.Cancel();
        if (client.IsConnected)
        {
            client.Disconnect();
        }
    }

    private void ObserveConnection()
    {
        while (!cts.IsCancellationRequested)
        {
            if (!client.IsConnected)
            {
                try
                {
                    client.Connect();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex.Message);
                    // ignored, will happen when we cant connect
                }
            }

            cts.Token.WaitHandle.WaitOne(watchdogPollMs);
        }
    }
}