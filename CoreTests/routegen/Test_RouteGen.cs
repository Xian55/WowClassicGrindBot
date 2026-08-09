using Core;
using Core.Database;

using Microsoft.Extensions.Logging;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using PPather;

using SharedLib;
using SharedLib.Data;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace CoreTests;

/// <summary>
/// Exercises route generation without a game client: everything but the navmesh reads from
/// <c>Json/</c>, and the navmesh half reads baked tiles. The player is stubbed, which is the
/// whole reason <see cref="IRouteGenPlayer"/> exists.
/// </summary>
internal sealed class Test_RouteGen : IDisposable
{
    private sealed class StubPlayer(int level, UnitRace race, PlayerFaction faction, int uiMapId)
        : IRouteGenPlayer
    {
        public int Level => level;
        public UnitRace Race => race;
        public PlayerFaction Faction => faction;
        public int UIMapId => uiMapId;
    }

    private readonly ILogger logger;
    private readonly PPatherService pather;
    private readonly RouteGenerator generator;
    private readonly CreatureRequirementFactory creatureRequirements;
    private readonly DataConfig dataConfig;

    public Test_RouteGen(ILogger logger, ILoggerFactory loggerFactory,
        int level, string race, string faction, int uiMapId, string exp)
    {
        this.logger = logger;

        // Exp is normally derived from the running client's exe version. There is no client
        // here, so the caller picks which client's data to generate against - which is also
        // what makes "does this profile work on mop" testable at all.
        dataConfig = DataConfig.Load();
        dataConfig.Exp = exp;

        // DataConfig.Root is relative and only resolves from the output directory, but
        // run.ps1 uses `dotnet run`, whose working directory is the project folder - so the
        // same command works one way and dies with "X:\json not found" the other. Anchor on
        // the assembly location instead, as DangerZoneGenerator does.
        if (!Directory.Exists(dataConfig.ExpDbc))
        {
            string? found = FindRepoJson(AppContext.BaseDirectory);
            if (found != null)
            {
                dataConfig.Root = found;
            }
        }

        logger.LogInformation("data root '{Root}' exp '{Exp}'",
            Path.GetFullPath(dataConfig.Root), dataConfig.Exp);

        WorldMapAreaDB worldMapAreaDB = new(dataConfig);
        ContinentDB.Init(worldMapAreaDB.Values);

        CreatureDB creatureDb = new(loggerFactory.CreateLogger<CreatureDB>(), dataConfig);
        FactionTemplateDB factionDb = new(loggerFactory.CreateLogger<FactionTemplateDB>(), dataConfig);
        NpcSpawnDB spawnDb = new(loggerFactory.CreateLogger<NpcSpawnDB>(), dataConfig);

        (NavmeshBakeOptions bake, NavmeshQueryOptions query) = LoadNavmeshOptions();

        pather = new PPatherService(loggerFactory.CreateLogger<PPatherService>(),
            dataConfig, worldMapAreaDB,
            Microsoft.Extensions.Options.Options.Create(bake),
            Microsoft.Extensions.Options.Options.Create(query))
        {
            Engine = PathingEngine.Navmesh
        };

        StubPlayer player = new(level,
            Enum.Parse<UnitRace>(race, true),
            Enum.Parse<PlayerFaction>(faction, true),
            uiMapId);

        creatureRequirements = new CreatureRequirementFactory(logger, player, factionDb);

        LocalPathingApi localPather = new(loggerFactory.CreateLogger<LocalPathingApi>(), pather);

        generator = new RouteGenerator(logger, creatureDb, spawnDb, worldMapAreaDB,
            pather, creatureRequirements, player, factionDb, localPather);
    }

    public void Dispose()
    {
        pather.Dispose();
    }

    /// <summary>
    /// Nearest ancestor of <paramref name="start"/> holding a <c>Json</c> folder with dbc
    /// data in it. Null when there is none, leaving the configured root in place.
    /// </summary>
    private static string? FindRepoJson(string start)
    {
        DirectoryInfo? dir = new(start);

        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Json");
            if (Directory.Exists(Path.Combine(candidate, "dbc")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private string RepoRoot => Path.GetFullPath(Path.Combine(dataConfig.Root, ".."));

    /// <summary>
    /// Reads the host's navmesh settings rather than using the defaults.
    ///
    /// <para>The bake options feed the tile-cache settings hash, which names the cache
    /// directory - so a harness running on defaults looks in a directory nothing was ever
    /// baked into and reports "no baked navmesh" for a continent that is in fact baked. That
    /// is exactly what happened with Outland: appsettings sets
    /// <c>MinWorldZ.Expansion01 = -700</c>, which changes the hash for that continent alone,
    /// so Azeroth resolved fine and Expansion01 did not.</para>
    /// </summary>
    private (NavmeshBakeOptions Bake, NavmeshQueryOptions Query) LoadNavmeshOptions()
    {
        NavmeshBakeOptions bake = new();
        NavmeshQueryOptions query = new();

        string path = Path.Combine(RepoRoot, "BlazorServer", "appsettings.json");

        if (!File.Exists(path))
        {
            logger.LogWarning(
                "appsettings not found at {Path} - using navmesh defaults, which may point " +
                "at a different tile cache than the one the bot baked.", path);

            return (bake, query);
        }

        try
        {
            JObject root = JObject.Parse(File.ReadAllText(path));
            JToken? navmesh = root["Navmesh"];

            if (navmesh?["Bake"]?.ToObject<NavmeshBakeOptions>() is { } b)
            {
                bake = b;
            }

            if (navmesh?["Query"]?.ToObject<NavmeshQueryOptions>() is { } q)
            {
                query = q;
            }

            logger.LogInformation("navmesh options from {Path}", path);
        }
        catch (Exception e)
        {
            logger.LogWarning("could not read {Path}: {Message} - using defaults",
                path, e.Message);
        }

        return (bake, query);
    }

    /// <summary>
    /// Asserts the creature scope parses what it should and rejects player-scope names.
    /// The two tables sharing a syntax is this feature's sharpest edge, so it is checked
    /// first and explicitly.
    /// </summary>
    public void TestExpressionScope()
    {
        logger.LogInformation("--- expression scope ---");

        string[] shouldParse =
        [
            "Type:Humanoid",
            "Type:Humanoid && Level <= PlayerLevel + 2 && !Elite",
            "(Name:Wolf || Name:Kobold) && Level >= PlayerLevel - 3",
            "Faction:7 && !Name:Defias",
            "NpcId == 299 || NpcId == 6",
            "SpawnCount > 4 && Skinnable",
            RouteGenerator.DefaultMobFilter,
        ];

        foreach (string text in shouldParse)
        {
            try
            {
                creatureRequirements.Parse(text);
                logger.LogInformation("  OK    {Text}", text);
            }
            catch (Exception e)
            {
                logger.LogError("  FAIL  {Text} -> {Message}", text, e.Message);
            }
        }

        // Player-scope names must not resolve here - a misplaced one has to fail loudly
        // rather than silently evaluate against the wrong subject.
        string[] shouldThrow = ["Health% > 50", "Race:Human", "BagFull"];

        foreach (string text in shouldThrow)
        {
            try
            {
                creatureRequirements.Parse(text);
                logger.LogError("  LEAK  {Text} parsed in creature scope but should not", text);
            }
            catch (Exception)
            {
                logger.LogInformation("  OK    {Text} rejected, as intended", text);
            }
        }
    }

    /// <summary>
    /// Generates every <c>Generate</c> path in a class profile, in profile order.
    ///
    /// <para>Reads the same file the bot reads, so what is rendered is what the profile will
    /// actually walk - no retyping filters into CLI flags and hoping they match.</para>
    /// </summary>
    public void TestProfile(string profilePath, int? seed, string? outputDir,
        string format = "svg")
    {
        if (!File.Exists(profilePath))
        {
            logger.LogError("profile not found: {Path}", profilePath);
            return;
        }

        ClassConfiguration? config =
            JsonConvert.DeserializeObject<ClassConfiguration>(File.ReadAllText(profilePath));

        // Deserializing only fills inline Paths. Groups listed in PathsFilenames are pulled in
        // by ClassConfiguration.Initialise, which needs a live service provider - so resolve
        // them directly here, or a profile that keeps its routes in group files reports
        // "no Paths" and renders nothing.
        config?.ResolvePathGroups(dataConfig.Path);

        if (config == null || config.Paths.Length == 0)
        {
            logger.LogError("profile has no Paths: {Path}", profilePath);
            return;
        }

        logger.LogInformation("--- profile {Path}: {Count} path(s) ---",
            profilePath, config.Paths.Length);

        int index = 0;
        foreach (PathSettings path in config.Paths)
        {
            int id = index++;

            if (path.Generate == null)
            {
                logger.LogInformation("[{Id}] {File} - recorded route, skipped",
                    id, path.PathFilename);
                continue;
            }

            logger.LogInformation("--- [{Id}] {Name} ---", id, path.DisplayName);

            Run(path.Generate, seed ?? path.Generate.Seed ?? config.Seed, outputDir,
                $"{id}_{path.DisplayName}", format);
        }
    }

    /// <summary>Generates from CLI arguments and reports on the result.</summary>
    public void TestGenerate(string zone, string? subzone, string mobs, int stops,
        RouteFocus focus = RouteFocus.Density, RouteGenMode mode = RouteGenMode.Wander,
        int? seed = null, string? outputDir = null, string format = "svg")
    {
        RouteGenSettings settings = new()
        {
            Zone = string.IsNullOrEmpty(zone) ? null : zone,
            Subzone = string.IsNullOrEmpty(subzone) ? null : subzone,
            Mobs = mobs,
            Stops = stops,
            Mode = mode,
            Focus = focus
        };

        Run(settings, seed, outputDir, settings.Subzone ?? settings.Zone ?? "route", format);
    }

    /// <summary>Generates from an already-built settings object and reports on it.</summary>
    public void Run(RouteGenSettings settings, int? seed, string? outputDir, string label,
        string format = "svg")
    {
        logger.LogInformation("--- generate: zone={Zone} subzone={Subzone} mobs='{Mobs}' " +
            "mode={Mode} focus={Focus} ---",
            settings.Zone, settings.Subzone, settings.Mobs, settings.Mode, settings.Focus);

        RouteGenerator.Context? context = generator.TryBuildContext(settings);
        if (context == null)
        {
            logger.LogError("generation produced no context - see the reason above");
            return;
        }

        logger.LogInformation(
            "zone={Zone} uiMap={UIMap} map={Map} creatures={Creatures} spawns={Spawns} cells={Cells}",
            context.Target.Name, context.Target.UIMapId, context.Target.MapId,
            context.MatchedCreatures, context.Polygon.Spawns.Length, context.Polygon.CellCount);

        logger.LogInformation("connectivity: {Polys} polys, {Components} components, route on #{Component}",
            context.Connectivity.PolyCount, context.Connectivity.ComponentCount, context.Component);

        // Fixed unless asked: two harness runs should be comparable by default.
        int effectiveSeed = seed ?? 1234;
        logger.LogInformation("seed {Seed}{Note}", effectiveSeed,
            settings.Mode == RouteGenMode.Loop ? " (Loop is deterministic - unused)" : "");

        // Two laps with the same seed must match; different seeds must not. That is the
        // reproducibility contract the whole seed plumbing exists to provide.
        Vector3[] first = generator.Sample(context, settings, RouteSeed.For(effectiveSeed, 0, 0));
        Vector3[] repeat = generator.Sample(context, settings, RouteSeed.For(effectiveSeed, 0, 0));
        Vector3[] nextLap = generator.Sample(context, settings, RouteSeed.For(effectiveSeed, 0, 1));

        logger.LogInformation("sampled {Count} waypoints (repeat identical: {Same}, next lap differs: {Differs})",
            first.Length, Same(first, repeat), !Same(first, nextLap));

        // Printed so two separate processes can be diffed - in-process repeatability says
        // nothing about whether a seed reproduces a route tomorrow.
        logger.LogInformation("hash: spawns {Spawns:X8} route {Route:X8}",
            Hash(context.Polygon.Spawns), Hash(first));

        if (outputDir != null)
        {
            WriteImage(context, settings, first, effectiveSeed, outputDir, label, format);
        }

        ReportSpacing(first);
        ReportVerticalSpread(context, first);
        ReportAreaIds(context);
    }

    private void WriteImage(RouteGenerator.Context context, RouteGenSettings settings,
        Vector3[] route, int seed, string outputDir, string label, string format)
    {
        string continent = ContinentDB.IdToName.TryGetValue(context.Target.MapId, out string c)
            ? c
            : string.Empty;

        string tileDir = continent.Length == 0 ? string.Empty : dataConfig.LeafletFor(continent);

        LeafletTiles.Config? tileConfig = continent.Length == 0
            ? null
            : LeafletTiles.ReadConfig(RepoRoot, DataConfig.ClientEra(dataConfig.Exp), continent);

        bool haveTiles = tileConfig != null && Directory.Exists(tileDir);
        if (!haveTiles)
        {
            logger.LogWarning("no minimap tiles for {Continent} at {Dir} - rendering without a map",
                continent, tileDir);
        }

        string title =
            $"{context.Target.Name} | {settings.Mode} | Focus={settings.Focus} | " +
            $"{settings.Stops} stops | seed {seed} | '{settings.Mobs}'";

        string name = $"{label}_{settings.Mode}";

        if (format == "svg")
        {
            string svg = RouteGenSvg.Render(context, route, StopsOf(route), title,
                haveTiles ? tileDir : null, haveTiles ? tileConfig : null);

            logger.LogInformation("svg: {Path}", RouteGenSvg.Write(outputDir, name, svg));
            return;
        }

        logger.LogInformation("{Format}: {Path}", format,
            RouteGenRaster.Render(context, route, StopsOf(route), title,
                haveTiles ? tileDir : null, haveTiles ? tileConfig : null,
                outputDir, name, format));
    }

    /// <summary>
    /// The emitted route is densified, so the original stops are not recoverable from it.
    /// Sampling every Nth waypoint is enough to show the tour shape in the picture.
    /// </summary>
    private static List<Vector3> StopsOf(Vector3[] route)
    {
        List<Vector3> stops = [];
        int step = Math.Max(1, route.Length / 24);

        for (int i = 0; i < route.Length; i += step)
        {
            stops.Add(route[i]);
        }

        return stops;
    }

    /// <summary>
    /// Distance between consecutive waypoints. The follower brakes at the end of every
    /// spline segment, and a segment ends at every waypoint - so a route much denser than a
    /// recorded one (~17 yd) makes the bot stutter along straight stretches.
    /// </summary>
    private void ReportSpacing(Vector3[] route)
    {
        if (route.Length < 2)
        {
            return;
        }

        float[] gaps = new float[route.Length - 1];
        for (int i = 1; i < route.Length; i++)
        {
            float dx = route[i].X - route[i - 1].X;
            float dy = route[i].Y - route[i - 1].Y;
            gaps[i - 1] = MathF.Sqrt((dx * dx) + (dy * dy));
        }

        Array.Sort(gaps);

        logger.LogInformation("spacing: n={Count} min={Min:F1} p50={Median:F1} max={Max:F1} yd",
            gaps.Length, gaps[0], gaps[gaps.Length / 2], gaps[^1]);
    }

    /// <summary>
    /// Looks for the signature of a rooftop stop.
    ///
    /// <para>This is a proxy, not the gate itself: gate 2 compares a sample against the spawn
    /// that anchored it, which the reporter no longer knows, so this compares against the
    /// XY-nearest spawn instead. A large dz on its own means nothing - on a hillside the
    /// nearest spawn is simply further up the slope. What indicts a stop is a large dz at a
    /// <i>small</i> dxy: ground that close should be at the same height.</para>
    /// </summary>
    private void ReportVerticalSpread(RouteGenerator.Context context, Vector3[] route)
    {
        if (route.Length == 0)
        {
            return;
        }

        const float CloseXY = 12f;
        const float SuspectDz = 6f;

        int suspects = 0;
        float worstDz = 0f;
        float worstAtDxy = 0f;

        for (int i = 0; i < route.Length; i++)
        {
            Vector3 nearest = default;
            float nearestSq = float.MaxValue;

            foreach (Vector3 spawn in context.Polygon.Spawns)
            {
                float dx = spawn.X - route[i].X;
                float dy = spawn.Y - route[i].Y;
                float d = (dx * dx) + (dy * dy);
                if (d < nearestSq)
                {
                    nearestSq = d;
                    nearest = spawn;
                }
            }

            float dxy = MathF.Sqrt(nearestSq);
            float dz = MathF.Abs(route[i].Z - nearest.Z);

            if (dz > worstDz)
            {
                worstDz = dz;
                worstAtDxy = dxy;
            }

            if (dxy < CloseXY && dz > SuspectDz)
            {
                suspects++;
                logger.LogWarning(
                    "  stop [{Index}] is {Dz:F1} yd above a spawn only {Dxy:F1} yd away - possible roof",
                    i, dz, dxy);
            }
        }

        logger.LogInformation(
            "vertical: worst dz {Dz:F2} yd (at dxy {Dxy:F1} yd), {Suspects} suspect stop(s)",
            worstDz, worstAtDxy, suspects);
    }

    /// <summary>
    /// Which area id each accepted spawn actually sits in. A subzone target should show one
    /// id and nothing else; anything else means the zone filter leaked.
    /// </summary>
    private void ReportAreaIds(RouteGenerator.Context context)
    {
        Dictionary<int, int> histogram = [];

        foreach (Vector3 spawn in context.Polygon.Spawns)
        {
            int areaId = pather.GetAreaId(context.Target.MapId, spawn.X, spawn.Y);
            histogram.TryGetValue(areaId, out int count);
            histogram[areaId] = count + 1;
        }

        foreach ((int areaId, int count) in histogram.OrderByDescending(kv => kv.Value))
        {
            logger.LogInformation("  areaId {AreaId}: {Count} spawn(s){Note}",
                areaId, count, areaId == 0 ? "  <- grid had no answer, map-bounds fallback" : "");
        }
    }

    /// <summary>Order-sensitive FNV-1a over the coordinates, for cross-process comparison.</summary>
    private static uint Hash(IReadOnlyList<Vector3> points)
    {
        uint h = 2166136261u;

        for (int i = 0; i < points.Count; i++)
        {
            foreach (float f in new[] { points[i].X, points[i].Y, points[i].Z })
            {
                uint bits = (uint)BitConverter.SingleToInt32Bits(f);
                for (int b = 0; b < 4; b++)
                {
                    h = (h ^ ((bits >> (b * 8)) & 0xFF)) * 16777619u;
                }
            }
        }

        return h;
    }

    private static bool Same(IReadOnlyList<Vector3> a, IReadOnlyList<Vector3> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }
}
