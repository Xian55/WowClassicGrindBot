using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using PathingAPI.RateLimit;

using PPather;
using PPather.Data;
using PPather.Graph;

using SharedLib.Data;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;

namespace PathingAPI.Controllers;

[Route("api/[controller]")]
[ApiController]
public sealed class PPatherController : ControllerBase
{
    private readonly PPatherService service;
    private readonly JsonSerializerOptions options;

    private readonly JsonResult emptyVector3;

    private const SearchStrategy eSearch = SearchStrategy.A_Star_With_Model_Avoidance;

    public PPatherController(PPatherService service, JsonSerializerOptions options)
    {
        this.service = service;
        this.options = options;

        emptyVector3 = new JsonResult(Array.Empty<Vector3>(), options);
    }

    /// <summary>
    /// Allows a route to be calculated from one point to another using only minimap coords.
    /// </summary>
    /// <remarks>
    /// uimap1 and uimap2 are the map ids. See [GetBestMapForUnit](https://wow.gamepedia.com/API_C_Map.GetBestMapForUnit)
    ///
    ///     /dump C_Map.GetBestMapForUnit("player")
    ///
    ///     Dump: value=_Map.GetBestMapForUnit("player")
    ///     [1]=1451
    ///
    /// x and y are the map coordinates for the zone (same as the mini map). See [GetPlayerMapPosition](https://wowwiki.fandom.com/wiki/API_GetPlayerMapPosition)
    ///
    ///     local posx, posY = GetPlayerMapPosition("player");
    /// </remarks>
    /// <param name="uimap1" example="1451">from Silithus [uimap id](https://wago.tools/db2/UiMap)</param>
    /// <param name="x1" example="46.8">from x</param>
    /// <param name="y1" example="54.2">from Y</param>
    /// <param name="uimap2" example="1451">to Silithus [uimap id](https://wago.tools/db2/UiMap)</param>
    /// <param name="x2" example="51.2">to x</param>
    /// <param name="y2" example="38.9">to Y</param>
    /// <response code="200">List of <see cref="Vector3"/> minimap coordinates.</response>
    [HttpGet("MapRoute")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Vector3[]))]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [RateLimit]
    public JsonResult MapRoute(int uimap1, float x1, float y1, int uimap2, float x2, float y2)
    {
        service.SetLocations(service.ToWorld(uimap1, x1, y1), service.ToWorld(uimap2, x2, y2));
        Path path = service.DoSearch(eSearch);
        if (path == null)
        {
            return emptyVector3;
        }

        service.Save();

        ArrayPool<Vector3> pool = ArrayPool<Vector3>.Shared;
        var array = pool.Rent(path.locations.Count);

        for (int i = 0; i < path.locations.Count; i++)
        {
            array[i] = service.ToLocal(path.locations[i], (int)service.SearchFrom.W, uimap1);
        }

        pool.Return(array);
        return new JsonResult(new ArraySegment<Vector3>(array, 0, path.locations.Count), options);
    }

    /// <summary>
    /// Allows a route to be calculated from one point to another using world coordinates.
    /// </summary>
    /// <remarks>
    /// Example
    /// 
    /// -896, -3770, 11, (Barrens, Rachet) to -441, -2596, 96, (Barrens, Crossroads, Barrens)
    /// </remarks>
    /// <param name="x1" example="-896">from x</param>
    /// <param name="y1" example="-3770">from Y</param>
    /// <param name="z1" example="11">from Z</param>
    /// <param name="x2" example="-441">to x</param>
    /// <param name="y2" example="-2596">to Y</param>
    /// <param name="z2" example="96">to Z</param>
    /// <param name="mapid" example="1">ContientID ["Azeroth=0", "Kalimdor=1", "Outland/Expansion01=530", "Northrend=571"]</param>
    /// <param name="reverse" example="true">Reverse the start and end points</param>
    /// <response code="200">List of <see cref="Vector3"/> world coordinates.</response>
    [HttpGet("WorldRoute")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Vector3[]))]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [RateLimit]
    public JsonResult WorldRoute(float x1, float y1, float z1, float x2, float y2, float z2, float mapid, bool reverse)
    {
        if (reverse)
        {
            (x1, y1, z1, x2, y2, z2) = (x2, y2, z2, x1, y1, z1);
        }
        service.SetLocations(new(x1, y1, z1, mapid), new(x2, y2, z2, mapid));

        var path = service.DoSearch(eSearch);
        if (path == null)
        {
            return emptyVector3;
        }

        service.Save();

        return new JsonResult(path.locations, options);
    }

    /// <summary>
    /// Allows a route to be calculated from one point to another using world coords.
    /// </summary>
    /// <remarks>
    /// Example
    /// 
    /// -896, -3770, 11, (Barrens, Rachet) to -441, -2596, 96, (Barrens, Crossroads, Barrens)
    /// </remarks>
    /// <param name="x1" example="-896">from x</param>
    /// <param name="y1" example="-3770">from Y</param>
    /// <param name="z1" example="11">from Z</param>
    /// <param name="x2" example="-441">to x</param>
    /// <param name="y2" example="-2596">to Y</param>
    /// <param name="z2" example="96">to Z</param>
    /// <param name="uimap" example="1413">The Barrens [uimap ID](https://wago.tools/db2/UiMap)</param>
    /// <param name="startindoors" example="false">If true, the search will prefer lower - underground values - otherwise the higher ones.</param>
    /// <response code="200">List of <see cref="Vector3"/> world coordinates.</response>
    [HttpGet("WorldRoute2")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Vector3[]))]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [RateLimit]
    public JsonResult WorldRoute2(float x1, float y1, float z1, float x2, float y2, float z2, int uimap, bool? startindoors = null)
    {
        service.SetLocations(
            service.ToWorldZ(uimap, x1, y1, z1, startindoors),
            service.ToWorldZ(uimap, x2, y2, z2, null));

        Path path = service.DoSearch(eSearch);
        if (path == null)
        {
            return emptyVector3;
        }
        service.Save();

        return new JsonResult(path.locations, options);
    }

    /// <summary>
    /// Allows a route to be calculated from one point to another using world coords.
    /// </summary>
    /// <remarks>
    /// Example
    /// 
    /// -896, -3770, 11, (Barrens, Rachet) to -441, -2596, 96, (Barrens, Crossroads, Barrens)
    /// </remarks>
    /// <param name="x1" example="30">from x</param>
    /// <param name="y1" example="73">from Y</param>
    /// <param name="z1" example="0">from Z</param>
    /// <param name="x2" example="42">to x</param>
    /// <param name="y2" example="59">to Y</param>
    /// <param name="z2" example="0">to Z</param>
    /// <param name="uimap" example="1426">The Barrens [uimap ID](https://wago.tools/db2/UiMap)</param>
    /// <response code="200">List of <see cref="Vector3"/> world coordinates.</response>
    [HttpGet("MapToWorldRoute")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Vector3[]))]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [RateLimit]
    public JsonResult MapToWorldRoute(float x1, float y1, float z1, float x2, float y2, float z2, int uimap)
    {
        service.SetLocations(service.ToWorld(uimap, x1, y1, z1), service.ToWorld(uimap, x2, y2, z2));
        Path path = service.DoSearch(eSearch);
        if (path == null)
        {
            return emptyVector3;
        }
        service.Save();

        return new JsonResult(path.locations, options);
    }

    /// <summary>
    /// Draws lines on the landscape.
    /// This endpoint is used by the client to render grind paths on the landscape.
    /// </summary>
    /// <remarks>
    /// This endpoint takes a list of <see cref="LineArgs"/> objects, each representing a line to be drawn.
    /// <see cref="LineArgs.Spots"/> Holds Map coordinates. Not World coordinates!
    /// 
    /// For each line specified in the request, the server creates corresponding locations
    /// which notifies browser UI to add the lines to the landscape.
    /// 
    /// If the server is currently rate-limited, a <see cref="StatusCodeResult"/> with status code 429 (Too Many Requests) will be returned.
    /// </remarks>
    /// <param name="lineArgs">A list of <see cref="LineArgs"/> objects representing the lines to be drawn.</param>
    /// <returns>
    /// An <see cref="IActionResult"/> representing the result of the operation.
    /// If successful, returns a <see cref="StatusCodeResult"/> with status code 202 (Accepted), indicating that the request has been accepted for processing.
    /// </returns>
    [HttpPost("Drawlines")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [RateLimit]
    public IActionResult Drawlines(List<LineArgs> lineArgs)
    {
        for (int i = 0; i < lineArgs.Count; i++)
        {
            LineArgs line = lineArgs[i];
            Vector4[] locations = service.CreateLocations(line);

            service.OnLinesAdded?.Invoke(new LinesEventArgs(line.Name, locations, line.Colour));
        }

        return Accepted();
    }

    /// <summary>
    /// Draws a sphere on the landscape to indicate a player's location.
    /// This endpoint is utilized by the client to visually represent the player's position on the landscape.
    /// </summary>
    /// <remarks>
    /// This endpoint receives a <see cref="SphereArgs"/> object containing information about the sphere to be drawn.
    /// 
    /// <see cref="SphereArgs.Spot"/> Holds Map coordinates. Not World coordinates!
    /// 
    /// If the server is not initialized and ready to handle requests, it returns a <see cref="ProblemDetails"/> response with status code 503 (Service Unavailable).
    /// 
    /// The server then translates the sphere's coordinates into world coordinates using the provided uiMapID and spot coordinates.
    /// Once the location is determined, the server invokes an event to notify the client to render the sphere.
    /// 
    /// If the server is currently rate-limited, a <see cref="StatusCodeResult"/> with status code 429 (Too Many Requests) will be returned.
    /// </remarks>
    /// <param name="sphere">A <see cref="SphereArgs"/> object representing the sphere to be drawn.</param>
    /// <returns>
    /// An <see cref="IActionResult"/> representing the result of the operation.
    /// If successful, returns a <see cref="StatusCodeResult"/> with status code 202 (Accepted), indicating that the request has been accepted for processing.
    /// </returns>
    [HttpPost("DrawSphere")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [RateLimit]
    public IActionResult DrawSphere(SphereArgs sphere)
    {
        if (!service.Initialised)
        {
            return Problem("Not Ready", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        Vector4 location = service.ToWorld(sphere.MapId, sphere.Spot.X, sphere.Spot.Y);
        service.OnSphereAdded?.Invoke(new SphereEventArgs(sphere.Name, location, sphere.Colour));

        return Accepted();
    }

    /// <summary>
    /// Returns true to indicate that the server is listening.
    /// </summary>
    /// <returns></returns>
    [HttpGet("SelfTest")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK, "application/json")]
    public JsonResult SelfTest()
    {
        return new JsonResult(service.MPQSelfTest());
    }

    [HttpPost("DrawPathTest")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [RateLimit]
    public IActionResult DrawPathTest()
    {
        float mapId = ContinentDB.NameToId["Azeroth"]; // Azeroth
        ReadOnlySpan<Vector3> coords =
        [
            new(-5609.00f, -479.00f, 397.49f),
            new(-5609.33f, -444.00f, 405.22f),
            new(-5609.33f, -438.40f, 406.02f),
            new(-5608.80f, -427.73f, 404.69f),
            new(-5608.80f, -426.67f, 404.69f),
            new(-5610.67f, -405.33f, 402.02f),
            new(-5635.20f, -368.00f, 392.15f),
            new(-5645.07f, -362.67f, 385.49f),
            new(-5646.40f, -362.13f, 384.69f),
            new(-5664.27f, -355.73f, 378.29f),
            new(-5696.00f, -362.67f, 366.02f),
            new(-5758.93f, -385.87f, 366.82f),
            new(-5782.00f, -394.00f, 366.09f)
        ];

        service.DrawPath(mapId, coords);

        return Accepted();
    }

    /// <summary>
    /// Draws a path based on continentID and world coordinates.
    /// </summary>
    /// <remarks>
    /// This endpoint allows drawing a path on the map specified by <paramref name="r.mapId"/>.
    /// 
    /// The path is specified by an array of <see cref="Vector3"/> world coordinates.
    /// 
    /// If the server is currently rate limited, a <see cref="StatusCodeResult"/> with status code 429 (Too Many Requests) will be returned.
    /// </remarks>
    /// <param name="r" example="{&#34;mapId&#34;:0, &#34;path&#34;:[{&#34;x&#34;:-6220.71,&#34;y&#34;:347.44037,&#34;z&#34;:384.21396},{&#34;x&#34;:-6214.267,&#34;y&#34;:372.179,&#34;z&#34;:385.83997},{&#34;x&#34;:-6207.5337,&#34;y&#34;:393.90826,&#34;z&#34;:387.28632},{&#34;x&#34;:-6200.808,&#34;y&#34;:415.13522,&#34;z&#34;:388.36853},{&#34;x&#34;:-6194.393,&#34;y&#34;:438.36694,&#34;z&#34;:388.9026},{&#34;x&#34;:-6188.587,&#34;y&#34;:466.1103,&#34;z&#34;:388.70398},{&#34;x&#34;:-6183.6895,&#34;y&#34;:500.87225,&#34;z&#34;:387.58856},{&#34;x&#34;:-6180,&#34;y&#34;:545.1599,&#34;z&#34;:385.372}]}"></param> 
    /// <returns>An <see cref="IActionResult"/> representing the result of the operation. If successful, returns a <see cref="StatusCodeResult"/> with status code 202 (Accepted).</returns>
    [HttpPost("DrawWorldPath")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [RateLimit]
    public IActionResult DrawPath(DrawWorldPathRequest r)
    {
        service.DrawPath(r.mapId, r.path.AsSpan());

        return Accepted();
    }

    /// <summary>
    /// Draws a path on the uiMapId and map coordinates.
    /// </summary>
    /// <remarks>
    /// This endpoint allows drawing a path on the map specified by <paramref name="r.uiMapId"/>.
    /// 
    /// The path is specified by an array of <see cref="Vector3"/> map coordinates.
    /// 
    /// If the server is currently rate limited, a <see cref="StatusCodeResult"/> with status code 429 (Too Many Requests) will be returned.
    /// </remarks>
    /// <param name="r" example="{&#34;uiMapId&#34;:1426, &#34;path&#34;:[{&#34;x&#34;:42.30905,&#34;y&#34;:59.866},{&#34;x&#34;:42.802,&#34;y&#34;:59.483},{&#34;x&#34;:43.40704,&#34;y&#34;:59.327},{&#34;x&#34;:43.69,&#34;y&#34;:58.779},{&#34;x&#34;:43.994,&#34;y&#34;:58.245999999999995},{&#34;x&#34;:44.596999999999994,&#34;y&#34;:58.038999999999994},{&#34;x&#34;:43.96,&#34;y&#34;:58.150999999999996},{&#34;x&#34;:43.585,&#34;y&#34;:58.6861},{&#34;x&#34;:43.04,&#34;y&#34;:58.958999999999996},{&#34;x&#34;:42.561,&#34;y&#34;:59.434},{&#34;x&#34;:41.961,&#34;y&#34;:59.54704},{&#34;x&#34;:41.46,&#34;y&#34;:59.156},{&#34;x&#34;:40.91004,&#34;y&#34;:58.8691},{&#34;x&#34;:40.271,&#34;y&#34;:58.958999999999996},{&#34;x&#34;:39.824,&#34;y&#34;:59.409},{&#34;x&#34;:39.42,&#34;y&#34;:59.915},{&#34;x&#34;:38.999,&#34;y&#34;:60.405},{&#34;x&#34;:38.949,&#34;y&#34;:61.025},{&#34;x&#34;:39.512,&#34;y&#34;:61.27},{&#34;x&#34;:40.11,&#34;y&#34;:61.196},{&#34;x&#34;:40.694,&#34;y&#34;:61.05004},{&#34;x&#34;:41.152,&#34;y&#34;:60.633}]}"></param> 
    /// <returns>An <see cref="IActionResult"/> representing the result of the operation. If successful, returns a <see cref="StatusCodeResult"/> with status code 202 (Accepted).</returns>
    [HttpPost("DrawMapPath")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [RateLimit]
    public IActionResult DrawPath(DrawMapPathRequest r)
    {
        float mapId = service.TransformMapToWorld(r.uiMapId, r.path);

        service.DrawPath(mapId, r.path.AsSpan());

        return Accepted();
    }

    /// <summary>
    /// 
    /// Resets the ppather service, clearing any cached data or state.
    /// 
    /// </summary>
    [HttpPost("Reset")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [RateLimit]
    public IActionResult Reset()
    {
        service.Reset();
        return Accepted();
    }
}