using Microsoft.Extensions.Logging;

using PPather.Data;

using SharedLib.Converters;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Core;

public sealed class RemotePathingAPI : IPPather, IPathVizualizer, IDisposable
{
    private readonly ILogger<RemotePathingAPI> logger;

    private readonly string host = "127.0.0.1";
    private readonly int port = 5001;

    private readonly JsonSerializerOptions options;
    private readonly HttpClient client;

    public HttpClient Client => client;
    public JsonSerializerOptions Options => options;

    private bool pathsAreSmoothed;
    /// <summary>Reflects the remote server's engine (Navmesh =&gt; smoothed dense
    /// paths, so the spline follower can engage). Fetched via GET Capabilities;
    /// stays false until <see cref="QueryCapabilities"/> runs, or if the server
    /// is older and returns 404.</summary>
    public bool PathsAreSmoothed => pathsAreSmoothed;

    private sealed record CapabilitiesResponse(bool PathsAreSmoothed);

    public RemotePathingAPI(ILogger<RemotePathingAPI> logger,
        string host, int port)
    {
        this.logger = logger;
        this.host = host;
        this.port = port;

        options = new()
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new Vector3Converter());
        options.Converters.Add(new Vector4Converter());

        string url = $"http://{host}:{port}/api/PPather/";

        client = new()
        {
            BaseAddress = new Uri(url)
        };
    }

    public void Dispose()
    {
        client.Dispose();
    }

    public async ValueTask DrawLines(List<LineArgs> lineArgs)
    {
        using StringContent content =
            new(JsonSerializer.Serialize(lineArgs, options),
            Encoding.UTF8, "application/json");

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Drawing lines '{MapIds}'...",
                string.Join(", ", lineArgs.Select(l => l.MapId)));

        await client.PostAsync("Drawlines", content);
    }

    public async ValueTask DrawSphere(SphereArgs args)
    {
        using StringContent content =
            new(JsonSerializer.Serialize(args, options),
            Encoding.UTF8, "application/json");

        await client.PostAsync("DrawSphere", content);
    }

    public Vector3[] FindMapRoute(int uiMap, Vector3 mapFrom, Vector3 mapTo)
    {
        try
        {
            //logger.LogDebug($"map {uiMap} | {mapFrom} to {mapTo}");

            string request = $"MapRoute?" +
                $"uimap1={uiMap}&" +
                $"x1={mapFrom.X}&" +
                $"y1={mapFrom.Y}&" +
                $"uimap2={uiMap}&" +
                $"x2={mapTo.X}&" +
                $"y2={mapTo.Y}";

            //long timestamp = Stopwatch.GetTimestamp();

            string response = client.GetStringAsync(request).GetAwaiter().GetResult();

            //logger.LogInformation($"map {uiMap} | {mapFrom} to {mapTo} took " +
            //    $"{Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds}ms");

            return
                JsonSerializer.Deserialize<Vector3[]>(response, options)
                ?? Array.Empty<Vector3>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{MapFrom} to {MapTo}", mapFrom, mapTo);
            return Array.Empty<Vector3>();
        }
    }

    public Vector3[] FindWorldRoute(int uiMap, bool startIndoors, Vector3 worldFrom, Vector3 worldTo)
    {
        try
        {
            //logger.LogDebug($"map {uiMap} | {worldFrom} map {uiMap} to {worldTo}");

            string request =
                $"WorldRoute2?" +
                $"x1={worldFrom.X}&" +
                $"y1={worldFrom.Y}&" +
                $"z1={worldFrom.Z}&" +
                $"x2={worldTo.X}&" +
                $"y2={worldTo.Y}&" +
                $"z2={worldTo.Z}&" +
                $"uimap={uiMap}&" +
                $"startindoors={startIndoors}";

            //long timestamp = Stopwatch.GetTimestamp();

            string response = client.GetStringAsync(request).GetAwaiter().GetResult();

            //logger.LogDebug($"map {uiMap} | {worldFrom} to {worldTo} took " +
            //    $"{Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds}ms");

            return
                JsonSerializer.Deserialize<Vector3[]>(response, options)
                ?? Array.Empty<Vector3>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{WorldFrom} to {WorldTo}", worldFrom, worldTo);
            return Array.Empty<Vector3>();
        }
    }

    public bool PingServer()
    {
        try
        {
            using TcpClient client = new(host, port);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Asks the server whether it produces smoothed (navmesh) paths, so the
    /// client's <see cref="PathsAreSmoothed"/> matches the remote engine and the
    /// spline follower can engage over RemoteV1. A 404 (older server) or any
    /// error resolves to false. Call once after <see cref="PingServer"/>.
    /// </summary>
    public bool QueryCapabilities()
    {
        try
        {
            using HttpResponseMessage res =
                client.GetAsync("Capabilities").GetAwaiter().GetResult();

            if (!res.IsSuccessStatusCode)
            {
                pathsAreSmoothed = false;
                return false;
            }

            string json = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            CapabilitiesResponse? caps =
                JsonSerializer.Deserialize<CapabilitiesResponse>(json, options);
            pathsAreSmoothed = caps?.PathsAreSmoothed ?? false;
            return pathsAreSmoothed;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Capabilities query failed ({Msg}); assuming unsmoothed.", ex.Message);
            pathsAreSmoothed = false;
            return false;
        }
    }
}