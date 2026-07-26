using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using PathingAPI.RateLimit;

using PPather;
using PPather.Data;
using PPather.Graph;
using PPather.Navmesh;

using SharedLib;
using SharedLib.Data;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;

namespace PathingAPI.Controllers;

[Route("api/[controller]")]
[ApiController]
public sealed class PPatherController : ControllerBase
{
    private readonly PPatherService service;
    private readonly JsonSerializerOptions options;

    private readonly JsonResult emptyVector3;

    private const SearchStrategy eSearch = SearchStrategy.A_Star_With_Model_Avoidance;

    private const double BytesPerMegabyte = 1024d * 1024d;

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
    /// <param name="edgeMargin">Edge margin override in yards; omit for the engine default, 0 disables the push.</param>
    /// <response code="200">List of <see cref="Vector3"/> minimap coordinates.</response>
    [HttpGet("MapRoute")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Vector3[]))]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [RateLimit]
    public JsonResult MapRoute(int uimap1, float x1, float y1, int uimap2, float x2, float y2,
        float? edgeMargin = null)
    {
        service.PathEdgeMarginYards = edgeMargin;

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
    /// Starts a background navmesh bake. Only ADTs the continent actually has
    /// terrain for are visited, so the open ocean filling most of the 64x64 grid
    /// costs nothing. One ADT is seconds; a continent is tens of minutes.
    /// </summary>
    /// <param name="continent" example="Azeroth">Continent; omit to bake every continent</param>
    /// <param name="adtX" example="30">ADT grid X; omit to bake the whole continent</param>
    /// <param name="adtY" example="49">ADT grid Y</param>
    /// <response code="200">Bake started.</response>
    /// <response code="409">A bake is already running.</response>
    [HttpPost("Bake")]
    public IActionResult Bake(string continent = null, int? adtX = null, int? adtY = null)
    {
        (int x, int y)? adt = adtX.HasValue && adtY.HasValue ? (adtX.Value, adtY.Value) : null;

        if (adt.HasValue && string.IsNullOrWhiteSpace(continent))
        {
            return BadRequest("continent is required when baking a single ADT");
        }

        return service.StartBake(continent, adt)
            ? Ok(service.BakeStatus)
            : Conflict(service.BakeStatus);
    }

    /// <summary>
    /// One-time extraction of the standalone spatial area-id grid(s) from the
    /// game files into DataConfig.AreaGrid, so GetAreaIdAndZ can answer without
    /// them. Runs in the background; needs the client archives present.
    /// </summary>
    /// <param name="continent" example="Northrend">Continent; omit to bake every continent</param>
    /// <response code="200">Extraction started.</response>
    [HttpPost("AreaGrid")]
    public IActionResult BakeAreaGrid(string continent = null)
    {
        string? target = string.IsNullOrWhiteSpace(continent) || continent is "all" or "*"
            ? null
            : continent;

        _ = Task.Run(() => service.BuildAreaGrid(target));
        return Ok($"Area-grid extraction started: {target ?? "all continents"}");
    }

    /// <summary>Progress of the running (or last) bake.</summary>
    [HttpGet("Bake/Status")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PPatherService.NavmeshBakeStatus))]
    public JsonResult BakeStatus()
    {
        return new JsonResult(service.BakeStatus);
    }

    /// <summary>Cancels the running bake. Tiles already written stay on disk.</summary>
    [HttpPost("Bake/Cancel")]
    public IActionResult BakeCancel()
    {
        return service.CancelBake() ? Ok(service.BakeStatus) : Conflict("No bake is running.");
    }

    /// <summary>
    /// Deletes cached navmesh tiles, including stale settings-hash directories
    /// from earlier bake parameters.
    /// </summary>
    /// <param name="continent" example="Azeroth">Continent; omit to clear every continent</param>
    /// <response code="200">Bytes freed.</response>
    /// <response code="409">A bake is running.</response>
    [HttpDelete("Bake/Cache")]
    public IActionResult BakeClear(string continent = null)
    {
        try
        {
            long freed = service.ClearNavmeshCache(continent);
            return Ok(new { freedBytes = freed, freedMB = freed / BytesPerMegabyte });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    /// <summary>
    /// Navmesh tiles baked to disk for a continent, with their world bounds, so
    /// the map can show which ground actually has a navmesh.
    /// </summary>
    /// <param name="continent" example="Azeroth">Continent name</param>
    /// <response code="200">Baked tiles; Resident marks the ones live in the mesh now.</response>
    [HttpGet("NavmeshTiles")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PPatherService.NavmeshTileInfo[]))]
    public JsonResult NavmeshTiles(string continent)
    {
        return new JsonResult(service.GetNavmeshTiles(continent));
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
    /// <param name="jitter" example="0">Randomly offsets each corner by up to this many yards, so repeated runs of the same route do not retrace one line. 0 disables it.</param>
    /// <param name="seed" example="1234">Seeds the jitter so a route is reproducible; omit for a random seed.</param>
    /// <param name="edgeMargin">Edge margin override in yards; omit for the engine default, 0 disables the push.</param>
    /// <response code="200">List of <see cref="Vector3"/> world coordinates.</response>
    [HttpGet("WorldRoute")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Vector3[]))]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [RateLimit]
    public JsonResult WorldRoute(float x1, float y1, float z1, float x2, float y2, float z2, float mapid, bool reverse,
        float jitter = 0f, int? seed = null, float? edgeMargin = null)
    {
        if (reverse)
        {
            (x1, y1, z1, x2, y2, z2) = (x2, y2, z2, x1, y1, z1);
        }

        service.PathJitterYards = jitter;
        service.PathJitterSeed = seed;
        service.PathEdgeMarginYards = edgeMargin;

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
    /// <summary>
    /// Whether this server can actually answer route requests, and why. For the navmesh
    /// engine that means baked tiles for the active era - game archives are optional, so
    /// the old MPQ-only check reported a failure on every tiles-only or CASC install.
    /// </summary>
    [HttpGet("SelfTest")]
    [ProducesResponseType(typeof(PPatherService.SelfTestReport), StatusCodes.Status200OK, "application/json")]
    public JsonResult SelfTest()
    {
        return new JsonResult(service.SelfTest(), options);
    }

    /// <summary>
    /// Describes this server's pathfinding capabilities so remote clients can
    /// adapt (e.g. smoothed paths are cheap to re-request instead of patching
    /// partial routes client-side). Absent on older servers - treat 404 as
    /// all-capabilities-false.
    /// </summary>
    [HttpGet("Capabilities")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public JsonResult Capabilities()
    {
        return new JsonResult(new CapabilitiesResponse(
            service.Engine == PathingEngine.Navmesh));
    }

    public sealed record CapabilitiesResponse(bool PathsAreSmoothed);

    /// <summary>Returns the active in-process pathfinding engine.</summary>
    [HttpGet("Engine")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public JsonResult GetEngine()
    {
        return new JsonResult(service.Engine.ToString());
    }

    /// <summary>
    /// Switches the in-process pathfinding engine at runtime (benchmark A/B
    /// without a server restart) and resets pathfinder state.
    /// </summary>
    /// <param name="engine" example="Navmesh">SpotAStar | Navmesh</param>
    [HttpPost("Engine")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [RateLimit]
    public IActionResult SetEngine(string engine)
    {
        if (!Enum.TryParse(engine, ignoreCase: true, out PathingEngine parsed))
        {
            return BadRequest($"Unknown engine '{engine}'");
        }

        service.Reset();
        service.Engine = parsed;
        return new JsonResult(parsed.ToString());
    }

    /// <summary>Timing breakdown of the most recent navmesh query.</summary>
    [HttpGet("Stats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public JsonResult Stats()
    {
        NavmeshPathfinder.NavmeshStats stats =
            service.NavmeshPathfinder?.LastStats ?? default;

        PPather.Navmesh.CostZones zones =
            service.NavmeshPathfinder?.Zones ?? PPather.Navmesh.CostZones.Empty;

        return new JsonResult(new StatsResponse(
            service.Engine.ToString(),
            stats.EnsureMs, stats.ResolveMs, stats.FindMs, stats.SmoothMs,
            stats.TilesBaked, stats.PolyPathLength, stats.PointCount,
            stats.PushedPoints, stats.Legs, stats.EndGapYd, stats.ResolveShiftYd,
            stats.CorridorRadius, stats.Widenings, stats.ResidentTiles,
            stats.Retargeted, stats.RetargetDropYd, stats.StallMemoHit,
            zones.CostChunkCount, zones.BlockedChunkCount));
    }

    public sealed record StatsResponse(
        string Engine,
        double EnsureMs, double ResolveMs, double FindMs, double SmoothMs,
        int TilesBaked, int PolyPathLength, int PointCount,
        int PushedPoints, int Legs, float EndGapYd, float ResolveShiftYd,
        int CorridorRadius, int Widenings, int ResidentTiles,
        bool Retargeted, float RetargetDropYd, bool StallMemoHit,
        int PricedChunks, int BlockedChunks);

    /// <summary>
    /// Re-reads the authored road / danger zone files for the active continent.
    /// Applies to the next query; the navmesh is untouched so nothing rebakes.
    /// </summary>
    [HttpPost("ReloadCostZones")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [RateLimit]
    public IActionResult ReloadCostZones()
    {
        if (!service.ReloadCostZones())
        {
            return Problem("Navmesh engine not initialised", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        PPather.Navmesh.CostZones zones = service.NavmeshPathfinder!.Zones;
        return new JsonResult(new { zones.CostChunkCount, zones.BlockedChunkCount });
    }

    /// <summary>
    /// Debug/diagnostic: bakes one DotRecast navmesh tile at the given world
    /// position and reports geometry extraction + bake statistics. This is the
    /// go/no-go probe for the navmesh engine's on-demand baking latency; it
    /// does not persist or register the tile anywhere yet.
    /// </summary>
    /// <param name="x" example="-8898">world X</param>
    /// <param name="y" example="-117">world Y</param>
    /// <param name="mapid" example="0">ContientID ["Azeroth=0", "Kalimdor=1", "Outland/Expansion01=530", "Northrend=571"]</param>
    [HttpGet("BakeTile")]
    [ProducesResponseType(typeof(BakeTileResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [RateLimit]
    public IActionResult BakeTile(float x, float y, float mapid)
    {
        // Ensures the continent (Search/PathGraph/triangle world) is initialised.
        service.SetLocations(new(x, y, 0, mapid), new(x, y, 0, mapid));

        // Returned directly, not wrapped in a JsonResult: wrapping serialises the
        // ProblemDetails into a 200 body instead of setting the status code.
        if (service.TriangleWorld == null)
        {
            return Problem(
                "This continent has no geometry loaded - the client archives are absent or do not " +
                "contain it. Tile baking needs them; path and height queries do not.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        NavmeshCoords.GetTileIndex(x, y, out int tileX, out int tileZ);

        long extractStart = System.Diagnostics.Stopwatch.GetTimestamp();
        TileGeometry geom = TileGeometryExtractor.Extract(service.TriangleWorld, tileX, tileZ);
        double extractMs = System.Diagnostics.Stopwatch.GetElapsedTime(extractStart).TotalMilliseconds;

        long bakeStart = System.Diagnostics.Stopwatch.GetTimestamp();
        DotRecast.Detour.DtMeshData data = NavmeshTileBuilder.Bake(geom, tileX, tileZ);
        double bakeMs = System.Diagnostics.Stopwatch.GetElapsedTime(bakeStart).TotalMilliseconds;

        return new JsonResult(new BakeTileResponse(
            data != null,
            tileX, tileZ,
            geom.GroundTriangleCount, geom.LiquidTriangleCount,
            geom.FailedChunkLoads,
            data?.header.polyCount ?? 0,
            data?.header.vertCount ?? 0,
            extractMs, bakeMs));
    }

    public sealed record BakeTileResponse(
        bool Success,
        int TileX, int TileZ,
        int GroundTriangles, int LiquidTriangles,
        int FailedChunkLoads,
        int PolyCount, int VertCount,
        double ExtractMs, double BakeMs);

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