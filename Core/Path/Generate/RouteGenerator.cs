using Core.Database;

using DotRecast.Core;

using Microsoft.Extensions.Logging;

using PPather;
using PPather.Navmesh;

using SharedLib;
using SharedLib.Data;
using SharedLib.Extensions;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Numerics;

namespace Core;

/// <summary>
/// Builds a grind route from the running client's own NPC spawn data instead of a recorded
/// waypoint file. See docs/generated-routes-design.md.
/// </summary>
public sealed partial class RouteGenerator
{
    /// <summary>
    /// Applied when <see cref="RouteGenSettings.Mobs"/> is empty. Expressed in the same
    /// language a profile would use, so there is one code path and the default is
    /// inspectable rather than hidden in an if-chain.
    /// </summary>
    public const string DefaultMobFilter =
        "Level >= PlayerLevel - 3 && Level <= PlayerLevel + 2 && !Elite";

    /// <summary>
    /// How many spawn anchors are probed to decide which navmesh component the route lives
    /// on. The modal answer wins, so a handful of spawns stranded on a roof or inside a
    /// building cannot drag the whole route onto their island.
    /// </summary>
    private const int ComponentProbeCount = 48;

    /// <summary>Resample budget per stop before giving up on that stop.</summary>
    private const int MaxAttemptsPerStop = 24;

    /// <summary>
    /// Pair budget for the Loop distance matrix. ~200 pairs is a couple of seconds; the
    /// growth is quadratic, so 50 stops would be 1225 pairs and the better part of a minute.
    /// </summary>
    private const int MaxPathedMatrixPairs = 200;

    private readonly ILogger logger;
    private readonly CreatureDB creatureDb;
    private readonly NpcSpawnDB spawnDb;
    private readonly WorldMapAreaDB worldMapAreaDB;
    private readonly PPatherService pather;
    private readonly CreatureRequirementFactory creatureRequirements;
    private readonly IRouteGenPlayer player;
    private readonly FactionTemplateDB factionDB;

    /// <summary>Point-to-point pathing, used to densify the legs between stops.</summary>
    private readonly IPPather routePather;

    /// <summary>
    /// Whether the last <see cref="Sample"/> managed to path its legs. False means the
    /// caller holds bare anchors, which Navigation has to be told about.
    /// </summary>
    public bool LastRouteIsDense { get; private set; }

    /// <summary>
    /// <see cref="CreatureRequirementFactory"/> evaluates through a single mutable cursor,
    /// so two concurrent generations would read each other's rows. The preview page and a
    /// wander lap regeneration are exactly that pair. Generation is rare and takes
    /// milliseconds, so a lock is the honest fix rather than a per-caller instance.
    /// </summary>
    private readonly System.Threading.Lock buildLock = new();

    public RouteGenerator(ILogger logger, CreatureDB creatureDb, NpcSpawnDB spawnDb,
        WorldMapAreaDB worldMapAreaDB, PPatherService pather,
        CreatureRequirementFactory creatureRequirements, IRouteGenPlayer player,
        FactionTemplateDB factionDB, IPPather routePather)
    {
        this.logger = logger;
        this.creatureDb = creatureDb;
        this.spawnDb = spawnDb;
        this.worldMapAreaDB = worldMapAreaDB;
        this.pather = pather;
        this.creatureRequirements = creatureRequirements;
        this.player = player;
        this.factionDB = factionDB;
        this.routePather = routePather;
    }

    /// <summary>
    /// The parts of a generated route that are worth keeping between laps: resolving the
    /// zone, selecting creatures and flooding the navmesh are all lap-invariant, so a
    /// wander regeneration re-samples against this and does no pathfinding at all.
    /// </summary>
    public sealed class Context
    {
        public required RouteTarget Target { get; init; }
        public required SpawnPolygon Polygon { get; init; }
        public required NavmeshConnectivity Connectivity { get; init; }
        public required NavmeshPathfinder Navmesh { get; init; }
        public required int Component { get; init; }
        public required int MatchedCreatures { get; init; }
    }

    /// <summary>
    /// Resolves everything a route needs except the sampling. Returns null - having logged
    /// why - when the zone, the spawn data, the mob filter or the navmesh cannot supply it.
    /// </summary>
    public Context? TryBuildContext(RouteGenSettings settings)
    {
        lock (buildLock)
        {
            return BuildContext(settings);
        }
    }

    private Context? BuildContext(RouteGenSettings settings)
    {
        RouteTarget? target = RouteTarget.Resolve(settings, worldMapAreaDB,
            player.Race, player.UIMapId);

        if (target == null)
        {
            LogNoZone(logger, settings.Zone ?? settings.Subzone ?? "<unset>");
            return null;
        }

        string filterText = string.IsNullOrWhiteSpace(settings.Mobs)
            ? DefaultMobFilter
            : settings.Mobs;

        Requirement filter;
        try
        {
            filter = creatureRequirements.Parse(filterText);
        }
        catch (Exception e)
        {
            LogBadFilter(logger, filterText, e.Message);
            return null;
        }

        List<Vector3> spawns = SelectSpawns(settings, target, filter, out int matched);

        if (spawns.Count == 0)
        {
            LogNoMatches(logger, target.Name, filterText);
            return null;
        }

        SpawnPolygon polygon = SpawnPolygon.Build(spawns, settings.PaddingYards,
            settings.ResolvedMinClusterShare, settings.ResolvedDensityBias);

        if (polygon.PrunedSpawns > 0)
        {
            LogPruned(logger, target.Name, polygon.PrunedSpawns, polygon.ClusterCount,
                settings.Focus);
        }

        NavmeshPathfinder? navmesh = pather.GetQueryNavmeshForMap(target.MapId);
        if (navmesh == null)
        {
            LogNoNavmesh(logger, target.Name, target.MapId);
            return null;
        }

        NavmeshConnectivity? connectivity = pather.BuildConnectivity(target.MapId,
            polygon.MinWorldX, polygon.MinWorldY, polygon.MaxWorldX, polygon.MaxWorldY);

        if (connectivity == null || connectivity.PolyCount == 0)
        {
            LogNoNavmesh(logger, target.Name, target.MapId);
            return null;
        }

        int component = ResolveComponent(polygon, navmesh, connectivity, settings);
        if (component < 0)
        {
            LogNoComponent(logger, target.Name);
            return null;
        }

        // polygon.Spawns, not the pre-prune list: pruning has already dropped the isolated
        // stragglers, and reporting the larger number here contradicts the very next line.
        LogContext(logger, target.Name, matched, polygon.Spawns.Length, polygon.CellCount,
            connectivity.PolyCount, connectivity.ComponentCount);

        return new Context
        {
            Target = target,
            Polygon = polygon,
            Connectivity = connectivity,
            Navmesh = navmesh,
            Component = component,
            MatchedCreatures = matched
        };
    }

    /// <summary>
    /// Samples one route. Cheap by design - the expensive work lives in
    /// <see cref="TryBuildContext"/> - so a wander route can be regenerated every lap.
    /// </summary>
    public Vector3[] Sample(Context context, RouteGenSettings settings, int seed)
    {
        return settings.Mode == RouteGenMode.Loop
            ? SampleLoop(context, settings)
            : SampleWander(context, settings, seed);
    }

    /// <summary>
    /// One closed tour over the densest spots, ordered so a lap covers the area once.
    ///
    /// <para>Takes no seed: a loop is meant to be the same route every session, and every
    /// input here - which cells are densest, how far apart the stops must be, the walking
    /// distance between them - is already fixed by the zone and the mob filter.</para>
    /// </summary>
    private Vector3[] SampleLoop(Context context, RouteGenSettings settings)
    {
        List<Vector3> peaks = context.Polygon.DensityPeaks(
            settings.MinSpacingYards, settings.Stops, settings.ResolvedMinCellShare,
            out int consideredCells);

        LogLoopPeaks(logger, context.Target.Name, peaks.Count, consideredCells,
            settings.Focus, settings.ResolvedMinCellShare);

        // A centroid is an average, so it can land off-mesh, on a roof, or on the far side
        // of a wall even when every spawn that formed it is fine. Same three gates as wander.
        List<Vector3> stops = new(peaks.Count);
        foreach (Vector3 peak in peaks)
        {
            if (TrySampleAccepted(context, settings, peak, peak, out Vector3 stop))
            {
                stops.Add(stop);
            }
        }

        if (stops.Count < 2)
        {
            LogLoopTooFewStops(logger, context.Target.Name, stops.Count, peaks.Count);
            return Densify(context, stops, settings);
        }

        float[,] cost = BuildCostMatrix(context, stops);
        int[] tour = TourSolver.Solve(cost, out float greedyCost, out float tourCost);

        List<Vector3> ordered = new(stops.Count + 1);
        foreach (int index in tour)
        {
            ordered.Add(stops[index]);
        }

        // Close the loop explicitly so the leg home is pathed and walked like any other.
        // PathThereAndBack should be false for this mode - the tour already returns.
        ordered.Add(ordered[0]);

        LogLoopTour(logger, context.Target.Name, stops.Count, greedyCost, tourCost);

        return Densify(context, ordered, settings);
    }

    /// <summary>
    /// Walking distance between every pair of stops, symmetric.
    ///
    /// <para>Straight-line distance would order two stops on opposite banks of a river as
    /// neighbours. The pather is the only thing that knows the difference, and the legs it
    /// returns here are the same ones <see cref="Densify"/> will walk, so the tour is
    /// optimised against what actually happens.</para>
    ///
    /// <para>Falls back to euclidean for any pair the pather cannot answer, rather than
    /// treating them as unreachable - gate 3 already established they share a navmesh
    /// component, so a failure here is the pather giving up, not a wall.</para>
    /// </summary>
    private float[,] BuildCostMatrix(Context context, List<Vector3> stops)
    {
        int n = stops.Count;
        float[,] cost = new float[n, n];
        int pathed = 0;

        // Pathing every pair is O(n²) calls: 12 stops is 66 and finishes instantly, 50 is
        // 1225 and takes ~50s - unacceptable when generation happens the first time a path
        // activates mid-session. Past the cap the ordering falls back to straight-line
        // distance, which still removes crossings; every leg the tour actually keeps is
        // pathed by Densify afterwards regardless, so the walked route is unaffected.
        bool pathPairs = n * (n - 1) / 2 <= MaxPathedMatrixPairs;

        if (!pathPairs)
        {
            LogMatrixEuclidean(logger, context.Target.Name, n, MaxPathedMatrixPairs);
        }

        for (int i = 0; i < n; i++)
        {
            for (int k = i + 1; k < n; k++)
            {
                Vector3[] leg = pathPairs
                    ? routePather.FindWorldRoute(context.Target.UIMapId,
                        startIndoors: false, stops[i], stops[k])
                    : [];

                float length;
                if (leg.Length > 1)
                {
                    length = 0f;
                    for (int p = 1; p < leg.Length; p++)
                    {
                        length += leg[p - 1].WorldDistanceXYTo(leg[p]);
                    }

                    pathed++;
                }
                else
                {
                    length = stops[i].WorldDistanceXYTo(stops[k]);
                }

                cost[i, k] = length;
                cost[k, i] = length;
            }
        }

        LogCostMatrix(logger, context.Target.Name, n, pathed, n * (n - 1) / 2);

        return cost;
    }

    private Vector3[] SampleWander(Context context, RouteGenSettings settings, int seed)
    {
        Random random = new(seed);

        float radius = AreaGrid.CellSize;
        float minSpacingSq = settings.MinSpacingYards * settings.MinSpacingYards;

        List<Vector3> accepted = new(settings.Stops);

        for (int stop = 0; stop < settings.Stops; stop++)
        {
            for (int attempt = 0; attempt < MaxAttemptsPerStop; attempt++)
            {
                Vector3 anchor = context.Polygon.PickAnchor(random);

                // The offset is drawn here rather than inside Detour so the seed actually
                // determines the route - see TrySnapWalkable's remarks. sqrt() makes the
                // draw uniform over the disc instead of clustering at the centre.
                float angle = random.NextSingle() * MathF.Tau;
                float distance = MathF.Sqrt(random.NextSingle()) * radius;

                Vector3 hint = new(
                    anchor.X + (MathF.Cos(angle) * distance),
                    anchor.Y + (MathF.Sin(angle) * distance),
                    anchor.Z);

                if (!TrySampleAccepted(context, settings, anchor, hint,
                    out Vector3 candidate))
                {
                    continue;
                }

                if (TooClose(accepted, candidate, minSpacingSq))
                {
                    continue;
                }

                accepted.Add(candidate);
                break;
            }
        }

        if (accepted.Count < settings.Stops)
        {
            LogShortRoute(logger, context.Target.Name, accepted.Count, settings.Stops);
        }

        return Densify(context, accepted, settings);
    }

    /// <summary>
    /// Replaces the straight hops between stops with the ground the bot would actually
    /// walk, by pathing each consecutive pair and emitting those points as the route.
    ///
    /// <para><b>Why this is not optional.</b> The bot acquires targets by cycling Tab as it
    /// walks, so a 300yd leg between two anchors is dead travel that skips every mob beside
    /// it. A dense route keeps the player next to mobs continuously, which is also the shape
    /// the rest of the bot is tuned for - a recorded route is hundreds of points a few yards
    /// apart.</para>
    ///
    /// <para>A leg the pather cannot answer falls back to the bare stop rather than
    /// dropping it; <see cref="Navigation"/> will path that hop itself at walk time.</para>
    /// </summary>
    /// <summary>
    /// Walks the polyline and emits a point roughly every <paramref name="spacingYards"/>,
    /// interpolating between the navmesh points rather than picking every Nth - point
    /// density along a navmesh path is uneven, so "every Nth" gives uneven spacing.
    ///
    /// <para>The first and last points are always kept, and the shape is preserved because
    /// the emitted points lie on the original line.</para>
    /// </summary>
    private static Vector3[] Resample(List<Vector3> points, float spacingYards)
    {
        if (points.Count < 3 || spacingYards <= 0f)
        {
            return [.. points];
        }

        List<Vector3> result = [points[0]];
        float carried = 0f;

        for (int i = 1; i < points.Count; i++)
        {
            Vector3 from = points[i - 1];
            Vector3 to = points[i];

            float dx = to.X - from.X;
            float dy = to.Y - from.Y;
            float segment = MathF.Sqrt((dx * dx) + (dy * dy));

            if (segment <= 0f)
            {
                continue;
            }

            float travelled = spacingYards - carried;

            while (travelled <= segment)
            {
                float t = travelled / segment;
                result.Add(new Vector3(
                    from.X + (dx * t),
                    from.Y + (dy * t),
                    from.Z + ((to.Z - from.Z) * t)));

                travelled += spacingYards;
            }

            carried = segment - (travelled - spacingYards);
        }

        // The tail is the last stop; the follower must actually arrive there.
        if (result[^1] != points[^1])
        {
            result.Add(points[^1]);
        }

        return [.. result];
    }

    private Vector3[] Densify(Context context, List<Vector3> stops, RouteGenSettings settings)
    {
        LastRouteIsDense = false;

        if (stops.Count < 2)
        {
            return [.. stops];
        }

        List<Vector3> dense = new(stops.Count * 16) { stops[0] };

        int pathed = 0;

        for (int i = 1; i < stops.Count; i++)
        {
            Vector3[] leg = routePather.FindWorldRoute(context.Target.UIMapId,
                startIndoors: false, stops[i - 1], stops[i]);

            if (leg.Length == 0)
            {
                dense.Add(stops[i]);
                continue;
            }

            pathed++;

            // Skip the leg's first point - it is the previous stop, already emitted.
            for (int p = 1; p < leg.Length; p++)
            {
                dense.Add(leg[p]);
            }
        }

        Vector3[] resampled = Resample(dense, settings.WaypointSpacingYards);

        LogDensified(logger, context.Target.Name, stops.Count, dense.Count,
            resampled.Length, pathed, stops.Count - 1);

        LastRouteIsDense = pathed > 0;

        return resampled;
    }

    /// <summary>
    /// The three gates, cheapest first: Detour picks an on-poly point, the height band
    /// rejects a roof or canopy for free, and the component id decides reachability with an
    /// array read rather than a pathfinding query.
    /// </summary>
    private static bool TrySampleAccepted(Context context, RouteGenSettings settings,
        Vector3 anchor, Vector3 hint, out Vector3 candidate)
    {
        candidate = default;

        // Gate 1 - on the mesh by construction.
        if (!context.Navmesh.TrySnapWalkable(hint, out Vector3 sampled, out long polyRef))
        {
            return false;
        }

        // Gate 2 - same floor as the spawn that anchored it. A roof, ledge or canopy poly
        // sits meters above the ground the anchor stands on, and this costs no queries.
        if (MathF.Abs(sampled.Z - anchor.Z) > settings.MaxVerticalDelta)
        {
            return false;
        }

        if (!context.Polygon.Contains(sampled.X, sampled.Y))
        {
            return false;
        }

        // Gate 3 - reachable. Same component means a path provably exists.
        if (context.Connectivity.ComponentOf(polyRef) != context.Component)
        {
            return false;
        }

        candidate = sampled;
        return true;
    }

    private static bool TooClose(List<Vector3> accepted, Vector3 candidate, float minSpacingSq)
    {
        for (int i = 0; i < accepted.Count; i++)
        {
            float dx = accepted[i].X - candidate.X;
            float dy = accepted[i].Y - candidate.Y;
            if ((dx * dx) + (dy * dy) < minSpacingSq)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Which navmesh component the route lives on: the one most of the selected spawns
    /// stand on. Using the player's position instead would fail whenever a route is built
    /// for a zone the player has not travelled to yet.
    /// </summary>
    private static int ResolveComponent(SpawnPolygon polygon, NavmeshPathfinder navmesh,
        NavmeshConnectivity connectivity, RouteGenSettings settings)
    {
        Dictionary<int, int> votes = [];

        // Fixed stride, not a random sample: the component a route lives on must not change
        // between laps, or the whole route would relocate.
        int step = Math.Max(1, polygon.Spawns.Length / ComponentProbeCount);

        for (int i = 0; i < polygon.Spawns.Length; i += step)
        {
            if (!navmesh.TrySnapWalkable(polygon.Spawns[i],
                out Vector3 point, out long polyRef))
            {
                continue;
            }

            if (MathF.Abs(point.Z - polygon.Spawns[i].Z) > settings.MaxVerticalDelta)
            {
                continue;
            }

            int component = connectivity.ComponentOf(polyRef);
            if (component < 0)
            {
                continue;
            }

            votes.TryGetValue(component, out int count);
            votes[component] = count + 1;
        }

        int best = -1;
        int bestVotes = 0;

        foreach ((int component, int count) in votes)
        {
            if (count > bestVotes)
            {
                bestVotes = count;
                best = component;
            }
        }

        return best;
    }

    private List<Vector3> SelectSpawns(RouteGenSettings settings, RouteTarget target,
        Requirement filter, out int matchedCreatures)
    {
        matchedCreatures = 0;

        FrozenDictionary<int, Vector3[]> byEntry = spawnDb.Get((int)target.MapId);
        List<Vector3> result = [];

        if (byEntry.Count == 0)
        {
            return result;
        }

        PlayerFaction faction = player.Faction;
        List<Vector3> inZone = [];

        foreach ((int entry, Vector3[] positions) in byEntry)
        {
            if (!creatureDb.Entries.TryGetValue(entry, out Creature creature))
            {
                continue;
            }

            if (!PassesBaseFilter(creature, faction))
            {
                continue;
            }

            inZone.Clear();
            for (int i = 0; i < positions.Length; i++)
            {
                if (InZone(target, positions[i]))
                {
                    inZone.Add(positions[i]);
                }
            }

            if (inZone.Count == 0)
            {
                continue;
            }

            // SpawnCount is the in-zone count on purpose - a profile asking for
            // "SpawnCount > 10" means "densely present here", not "common somewhere".
            if (!creatureRequirements.Matches(filter, creature, inZone.Count))
            {
                continue;
            }

            matchedCreatures++;
            result.AddRange(inZone);
        }

        return result;
    }

    /// <summary>
    /// Never user-overridable. Mirrors the hostile-spawn filter in DangerZoneGenerator: a
    /// vendor, a critter or a totem is not a grind target however the expression is written.
    /// </summary>
    private bool PassesBaseFilter(in Creature creature, PlayerFaction faction)
    {
        return creature.NpcFlag == NpcFlags.None &&
            creature.MinLevel > 0 &&
            creature.Type is not (CreatureType.Critter or CreatureType.Totem
                or CreatureType.NonCombatPet) &&
            FactionExt.HostileToPlayer(creature, faction, factionDB);
    }

    private bool InZone(RouteTarget target, Vector3 worldPos)
    {
        int areaId = pather.GetAreaId(target.MapId, worldPos.X, worldPos.Y);

        // 0 means the point is outside the baked grid's bounds - or that no grid is baked
        // for this era at all, which is the whole of Kalimdor on precata. Fall back to the
        // zone's own map-coordinate bounds rather than treating it as "not in the zone".
        return areaId != 0
            ? target.AcceptsAreaId(areaId)
            : target.WithinMapBounds(worldPos);
    }

    #region Logging

    [LoggerMessage(
        EventId = 0090,
        Level = LogLevel.Error,
        Message = "[RouteGen] cannot resolve a zone for '{zone}'")]
    static partial void LogNoZone(ILogger logger, string zone);

    [LoggerMessage(
        EventId = 0091,
        Level = LogLevel.Error,
        Message = "[RouteGen] mob filter '{filter}' failed to parse: {reason}")]
    static partial void LogBadFilter(ILogger logger, string filter, string reason);

    [LoggerMessage(
        EventId = 0092,
        Level = LogLevel.Warning,
        Message = "[RouteGen] {zone}: no spawns matched '{filter}'")]
    static partial void LogNoMatches(ILogger logger, string zone, string filter);

    [LoggerMessage(
        EventId = 0093,
        Level = LogLevel.Warning,
        Message = "[RouteGen] {zone}: no baked navmesh for map {mapId} - bake it with --bake=<Continent>")]
    static partial void LogNoNavmesh(ILogger logger, string zone, float mapId);

    [LoggerMessage(
        EventId = 0094,
        Level = LogLevel.Warning,
        Message = "[RouteGen] {zone}: no spawn stands on the navmesh - cannot pick a route component")]
    static partial void LogNoComponent(ILogger logger, string zone);

    [LoggerMessage(
        EventId = 0095,
        Level = LogLevel.Information,
        Message = "[RouteGen] {zone}: {creatures} creature types, {spawns} spawns, {cells} cells, {polys} polys in {components} components")]
    static partial void LogContext(ILogger logger, string zone, int creatures, int spawns,
        int cells, int polys, int components);

    [LoggerMessage(
        EventId = 0096,
        Level = LogLevel.Warning,
        Message = "[RouteGen] {zone}: only {got} of {want} stops sampled")]
    static partial void LogShortRoute(ILogger logger, string zone, int got, int want);

    [LoggerMessage(
        EventId = 0103,
        Level = LogLevel.Information,
        Message = "[RouteGen] {zone}: {stops} stops exceeds the {cap} pathed-pair budget - ordering on straight-line distance")]
    static partial void LogMatrixEuclidean(ILogger logger, string zone, int stops, int cap);

    [LoggerMessage(
        EventId = 0102,
        Level = LogLevel.Information,
        Message = "[RouteGen] {zone}: {peaks} stops from {cells} occupied cells (Focus={focus}, cell floor {share:P0})")]
    static partial void LogLoopPeaks(ILogger logger, string zone, int peaks, int cells,
        RouteFocus focus, float share);

    [LoggerMessage(
        EventId = 0099,
        Level = LogLevel.Information,
        Message = "[RouteGen] {zone}: loop over {stops} stops, tour {greedy:F0} -> {optimised:F0} yd (2-opt)")]
    static partial void LogLoopTour(ILogger logger, string zone, int stops, float greedy, float optimised);

    [LoggerMessage(
        EventId = 0100,
        Level = LogLevel.Warning,
        Message = "[RouteGen] {zone}: only {kept} of {peaks} density peaks are reachable - loop needs at least 2")]
    static partial void LogLoopTooFewStops(ILogger logger, string zone, int kept, int peaks);

    [LoggerMessage(
        EventId = 0101,
        Level = LogLevel.Information,
        Message = "[RouteGen] {zone}: cost matrix {stops}x{stops}, {pathed}/{pairs} pairs pathed")]
    static partial void LogCostMatrix(ILogger logger, string zone, int stops, int pathed, int pairs);

    [LoggerMessage(
        EventId = 0098,
        Level = LogLevel.Information,
        Message = "[RouteGen] {zone}: dropped {pruned} isolated spawn(s), kept {clusters} cluster(s) (Focus={focus})")]
    static partial void LogPruned(ILogger logger, string zone, int pruned, int clusters,
        RouteFocus focus);

    [LoggerMessage(
        EventId = 0097,
        Level = LogLevel.Information,
        Message = "[RouteGen] {zone}: {stops} stops -> {points} path points -> {waypoints} waypoints ({pathed}/{legs} legs pathed)")]
    static partial void LogDensified(ILogger logger, string zone, int stops, int points,
        int waypoints, int pathed, int legs);

    #endregion
}
