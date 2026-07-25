using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using Newtonsoft.Json;

using SharedLib;
using SharedLib.Data;

const int MinSpawnCount = 3;
const float SpawnMultiplier = 10f;
const float LevelDivisor = 60f;
const float EliteBonus = 50f;
const float MinSubzoneArea = 100f;

// Danger while CROSSING a zone scales with encounters per yard traveled -
// spawn density - not with absolute spawn count. Scoring raw head-count let
// sprawling leveling areas (Southern Barrens, 1497 spawns) outscore compact
// elite camps and hit the cost ceiling that should be reserved for the
// genuinely lethal (Silithus hives, Tyr's Hand). Spawn and elite counts are
// therefore normalized to this reference footprint (~316x316 yd, a typical
// camp) before scoring.
const float ReferenceArea = 100_000f;

// The consumer (PPather CostZones.RasterizeBox) turns a penalty into a cost
// multiplier: clamp(offRoad * (1 + penalty/100), offRoad, MaxPenaltyFactor=25).
// That ceiling saturates at penalty ~1400 - every value above it routes
// identically - so the raw danger score is compressed into [MinPenalty,
// MaxPenalty] with a square root, which spreads the crowded low-to-mid range
// while still letting the worst camps approach (but not waste) the ceiling.
const float MinPenalty = 50f;
const float MaxPenalty = 1300f;
const float RawScoreCeiling = 10000f;

// "Road" skips road-corridor subzones (Gold Road etc.) - pricing those up
// fights the road preference the cost system exists to express.
string[] SkipSubzoneKeywords = ["Sea", "Ocean", "Shore", "Coast", "Baradin Bay", "The Cape of Stranglethorn", "Road"];

string exp = "som";
string? continentFilter = null;
string? zoneFilter = null;
bool force = false;
bool dryRun = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--exp" when i + 1 < args.Length:
            exp = args[++i].ToLowerInvariant();
            break;
        case "--continent" when i + 1 < args.Length:
            continentFilter = args[++i];
            break;
        case "--zone" when i + 1 < args.Length:
            zoneFilter = args[++i];
            break;
        case "--force":
            force = true;
            break;
        case "--dry-run":
            dryRun = true;
            break;
    }
}

Console.WriteLine($"DangerZone Generator - exp: {exp}, continent: {continentFilter ?? "all"}, zone: {zoneFilter ?? "all"}, force: {force}, dry-run: {dryRun}");

// DataConfig.Root defaults to "../json" (relative), resolve from utility location
string utilityDir = AppContext.BaseDirectory;
string repoRoot = Path.GetFullPath(Path.Join(utilityDir, "..", "..", "..", "..", ".."));
DataConfig dataConfig = new() { Exp = exp, Root = Path.Join(repoRoot, "Json") };

// Step 1: Load data

string worldMapAreaPath = Path.Join(dataConfig.ExpDbc, "WorldMapArea.json");
WorldMapArea[] worldMapAreas = JsonConvert.DeserializeObject<WorldMapArea[]>(
    File.ReadAllText(worldMapAreaPath))!;

Console.WriteLine($"Loaded {worldMapAreas.Length} WorldMapArea entries");

ContinentDB.Init(worldMapAreas);

string creaturesPath = Path.Join(dataConfig.ExpDbc, "creatures.json");
Creature[] allCreatures = JsonConvert.DeserializeObject<Creature[]>(
    File.ReadAllText(creaturesPath))!;

Dictionary<int, Creature> creatureLookup = new();
foreach (Creature c in allCreatures)
{
    creatureLookup.TryAdd(c.Entry, c);
}

Console.WriteLine($"Loaded {allCreatures.Length} creatures ({creatureLookup.Count} unique)");

// Step 2: Build zone mappings
// subzoneAreaId -> WorldMapArea (subzone entry: ParentAreaId > 0)
Dictionary<int, WorldMapArea> subzoneWma = new();
// parentAreaId -> WorldMapArea (parent zone entry: ParentAreaId == 0, UIMapId > 0)
Dictionary<int, WorldMapArea> parentWma = new();

foreach (WorldMapArea wma in worldMapAreas)
{
    if (wma.ParentAreaId > 0)
    {
        subzoneWma.TryAdd(wma.AreaID, wma);
    }
    else if (wma.UIMapId > 0 && wma.AreaID > 0)
    {
        parentWma.TryAdd(wma.AreaID, wma);
    }
}

int totalGenerated = 0;

// Process each continent (MapID -> Continent name)
foreach (KeyValuePair<float, string> continent in ContinentDB.IdToName)
{
    int mapId = (int)continent.Key;
    string continentName = continent.Value;

    if (continentFilter != null &&
        !continentName.Equals(continentFilter, StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }

    string subzonePath = Path.Join(dataConfig.Subzones, $"{mapId}.json");
    if (!File.Exists(subzonePath))
    {
        Console.WriteLine($"No subzone data for {continentName} (MapID {mapId}), skipping");
        continue;
    }

    SubZoneArea[] subzones = JsonConvert.DeserializeObject<SubZoneArea[]>(
        File.ReadAllText(subzonePath))!;

    Console.WriteLine($"Loaded {subzones.Length} subzones for {continentName}");

    string spawnPath = Path.Join(dataConfig.NpcSpawnLocations, $"{mapId}.json");
    if (!File.Exists(spawnPath))
    {
        Console.WriteLine($"No spawn data for {continentName} (MapID {mapId}), skipping");
        continue;
    }

    // NPC spawn JSON uses lowercase x,y,z — deserialize as SpawnPoint DTO
    Dictionary<string, SpawnPoint[]> spawnLocations = JsonConvert.DeserializeObject<
        Dictionary<string, SpawnPoint[]>>(File.ReadAllText(spawnPath))!;

    Console.WriteLine($"Loaded spawn data for {spawnLocations.Count} NPCs in {continentName}");

    // Filter to hostile-only spawns
    List<(int NpcId, Creature Creature, SpawnPoint[] Spawns)> hostileSpawns = new();
    foreach (KeyValuePair<string, SpawnPoint[]> kvp in spawnLocations)
    {
        if (!int.TryParse(kvp.Key, out int npcId))
            continue;
        if (!creatureLookup.TryGetValue(npcId, out Creature creature))
            continue;

        if (creature.NpcFlag != NpcFlags.None)
            continue;
        if (creature.MinLevel <= 0)
            continue;
        if (creature.Type is CreatureType.Critter or CreatureType.Totem or CreatureType.NonCombatPet)
            continue;

        hostileSpawns.Add((npcId, creature, kvp.Value));
    }

    Console.WriteLine($"  {hostileSpawns.Count} hostile NPC types with spawns");

    // Step 3: Score each subzone
    Dictionary<int, List<RectangleDangerZone>> zoneRectangles = new();

    foreach (SubZoneArea subzone in subzones)
    {
        if (!subzoneWma.TryGetValue(subzone.Id, out WorldMapArea subzoneEntry))
            continue;

        if (!parentWma.TryGetValue(subzoneEntry.ParentAreaId, out WorldMapArea parentEntry))
            continue;

        string subzoneName = subzoneEntry.AreaName;
        if (SkipSubzoneKeywords.Any(k => subzoneName.Contains(k, StringComparison.OrdinalIgnoreCase)))
            continue;

        if (zoneFilter != null &&
            !parentEntry.AreaName.Equals(zoneFilter, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        float sizeX = subzone.Max.X - subzone.Min.X;
        float sizeY = subzone.Max.Y - subzone.Min.Y;
        float subzoneArea = sizeX * sizeY;

        if (subzoneArea < MinSubzoneArea)
            continue;

        int hostileSpawnCount = 0;
        double totalLevel = 0;
        int levelCount = 0;
        int eliteCount = 0;

        foreach ((int npcId, Creature creature, SpawnPoint[] spawns) in hostileSpawns)
        {
            foreach (SpawnPoint spawn in spawns)
            {
                Vector3 pos = new(spawn.x, spawn.y, spawn.z);
                if (subzone.Contains(pos))
                {
                    hostileSpawnCount++;
                    double avgLevel = (creature.MinLevel + creature.MaxLevel) / 2.0;
                    totalLevel += avgLevel;
                    levelCount++;
                    if (creature.Rank > 0)
                        eliteCount++;
                }
            }
        }

        if (hostileSpawnCount < MinSpawnCount)
            continue;

        double density = hostileSpawnCount / (double)subzoneArea;

        // Step 4: Compute penalty — spawn count is the base signal, density is a bonus multiplier
        double avgLevelFinal = levelCount > 0 ? totalLevel / levelCount : 0;

        double effectiveSpawns = density * ReferenceArea;
        double effectiveElites = eliteCount / (double)subzoneArea * ReferenceArea;

        double raw = effectiveSpawns * SpawnMultiplier;
        raw *= 1.0 + avgLevelFinal / LevelDivisor;
        raw += effectiveElites * EliteBonus;

        // Sqrt-compress the open-ended raw score into the range the pathfinder
        // can actually distinguish (see constants above).
        double normalized = Math.Min(raw, RawScoreCeiling) / RawScoreCeiling;
        double penalty = MinPenalty + (MaxPenalty - MinPenalty) * Math.Sqrt(normalized);

        string name = subzoneEntry.AreaName;
        int uiMapId = parentEntry.UIMapId;

        if (!zoneRectangles.TryGetValue(uiMapId, out List<RectangleDangerZone>? list))
        {
            list = new();
            zoneRectangles[uiMapId] = list;
        }

        list.Add(new RectangleDangerZone(
            name,
            subzone.Min.X, subzone.Min.Y,
            subzone.Max.X, subzone.Max.Y,
            (float)Math.Round(penalty, 1)));

        if (dryRun)
        {
            Console.WriteLine($"  [{continentName}] {parentEntry.AreaName}/{name} " +
                $"(UIMapId={uiMapId}) spawns={hostileSpawnCount} " +
                $"density={density:F6} avgLevel={avgLevelFinal:F1} " +
                $"elites={eliteCount} raw={raw:F0} penalty={penalty:F1}");
        }
    }

    // Step 5: Emit output
    string dangerDir = Path.Join(dataConfig.Road, continentName, "dangerzone");

    foreach (KeyValuePair<int, List<RectangleDangerZone>> kvp in zoneRectangles)
    {
        int uiMapId = kvp.Key;
        List<RectangleDangerZone> rectangles = kvp.Value;

        string filePath = Path.Join(dangerDir, $"{uiMapId}.json");

        if (File.Exists(filePath) && !force)
        {
            Console.WriteLine($"  Skipping {continentName}/dangerzone/{uiMapId}.json " +
                $"(already exists, use --force to overwrite)");
            continue;
        }

        DangerZoneData data = new()
        {
            Circles = Array.Empty<CircleDangerZone>(),
            Rectangles = rectangles.ToArray()
        };

        string json = JsonConvert.SerializeObject(data, Formatting.Indented);

        if (dryRun)
        {
            Console.WriteLine($"  Would write {continentName}/dangerzone/{uiMapId}.json " +
                $"({rectangles.Count} rectangles)");
        }
        else
        {
            Directory.CreateDirectory(dangerDir);
            File.WriteAllText(filePath, json);
            Console.WriteLine($"  Wrote {continentName}/dangerzone/{uiMapId}.json " +
                $"({rectangles.Count} rectangles)");
        }

        totalGenerated++;
    }
}

Console.WriteLine($"Done. {totalGenerated} zone files generated.");

// DTO for NPC spawn location JSON (lowercase x,y,z)
file record struct SpawnPoint(float x, float y, float z);

// Mirror PPather.Graph types for JSON serialization compatibility
file readonly record struct CircleDangerZone(
    string Name, float CenterX, float CenterY, float Radius, float Penalty);

file readonly record struct RectangleDangerZone(
    string Name, float MinX, float MinY, float MaxX, float MaxY, float Penalty);

file sealed class DangerZoneData
{
    public CircleDangerZone[] Circles { get; init; } = Array.Empty<CircleDangerZone>();
    public RectangleDangerZone[] Rectangles { get; init; } = Array.Empty<RectangleDangerZone>();
}
