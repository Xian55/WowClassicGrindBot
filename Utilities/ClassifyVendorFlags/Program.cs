using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using SharedLib;
using SharedLib.Data;

const int VendorSubtypeMask = (int)(NpcFlags.VendorAmmo | NpcFlags.VendorFood | NpcFlags.VendorPoison | NpcFlags.VendorReagent);
const int VendorServiceMask = (int)NpcFlags.Vendor | VendorSubtypeMask | (int)NpcFlags.Repair;
const int TrainerMask = (int)(NpcFlags.Trainer | NpcFlags.ClassTrainer | NpcFlags.ProfessionTrainer);

// NPCs that require special conditions (quest items, phasing, etc.) to interact with.
// Strip all vendor/repair flags so the bot never attempts to use them.
HashSet<int> excludedVendorEntries =
[
    11278, // Magnus Frostwake — requires "Spectral Essence" from Scholomance quest chain
    11287, // Baker Masterson — requires "Spectral Essence" from Scholomance quest chain
];

bool auditMode = args.Contains("--audit", StringComparer.OrdinalIgnoreCase);

string basePath = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Json", "dbc"));

string[] filePaths =
[
    Path.Combine(basePath, "som", "creatures.json"),
    Path.Combine(basePath, "tbc", "creatures.json"),
    Path.Combine(basePath, "wrath", "creatures.json")
];

// Load all three files
Dictionary<string, JArray> files = new(filePaths.Length);
foreach (string filePath in filePaths)
{
    string label = Path.GetFileName(Path.GetDirectoryName(filePath))!;
    Console.WriteLine($"Loading {label}: {filePath}");
    if (!File.Exists(filePath))
    {
        Console.Error.WriteLine($"  File not found, skipping.");
        continue;
    }

    files[label] = JArray.Parse(File.ReadAllText(filePath));
}

Console.WriteLine();

if (auditMode)
{
    RunAudit(files, basePath);
    return 0;
}

// ── Phase -1: Remap SoM NpcFlags from Classic to TBC+ layout ──
// SoM creatures.json stores NpcFlags in Classic (1.14.x) bit layout.
// Our NpcFlags enum uses TBC+ layout. Remap before any classification.
Console.WriteLine("=== Phase -1: Remap SoM NpcFlags from Classic to TBC+ layout ===");
int remapCount = RemapSomNpcFlags(files);
Console.WriteLine($"Phase -1 remapped: {remapCount}");
Console.WriteLine();

// ── Phase DB: Classify vendors from actual sell data ──
// Load vendoritems.json + items.json per expansion to determine vendor sub-types
// from ground-truth inventory data. This replaces (not merges) sub-type flags.
Console.WriteLine("=== Phase DB: Classify vendors from sell data ===");

// Track all vendors present in vendoritems.json so Phase 1/2/3 skip them —
// their flags are already correct from ground truth.
HashSet<(string, int)> dbClassifiedVendors = [];

int phaseDbCount = 0;
string[] expansions = ["som", "tbc"];
foreach (string expansion in expansions)
{
    string vendorItemsPath = Path.Combine(basePath, expansion, "vendoritems.json");
    string itemsPath = Path.Combine(basePath, expansion, "items.json");

    if (!File.Exists(vendorItemsPath))
    {
        Console.WriteLine($"  [{expansion}] vendoritems.json not found, skipping.");
        continue;
    }

    if (!File.Exists(itemsPath))
    {
        Console.WriteLine($"  [{expansion}] items.json not found, skipping.");
        continue;
    }

    if (!files.TryGetValue(expansion, out JArray? creatures))
        continue;

    // Load vendor items: NPC Entry -> array of item IDs
    Dictionary<string, int[]> vendorItems =
        JsonConvert.DeserializeObject<Dictionary<string, int[]>>(
            File.ReadAllText(vendorItemsPath))!;

    // Load items -> lookup by Entry
    Dictionary<int, Item> itemLookup = JsonConvert.DeserializeObject<List<Item>>(
        File.ReadAllText(itemsPath))!
        .ToDictionary(i => i.Entry);

    // Load food/water ID sets for fallback classification
    string foodsPath = Path.Combine(basePath, expansion, "foods.json");
    string watersPath = Path.Combine(basePath, expansion, "waters.json");

    HashSet<int> foodIds = File.Exists(foodsPath)
        ? JsonConvert.DeserializeObject<HashSet<int>>(File.ReadAllText(foodsPath))!
        : [];

    HashSet<int> waterIds = File.Exists(watersPath)
        ? JsonConvert.DeserializeObject<HashSet<int>>(File.ReadAllText(watersPath))!
        : [];

    Console.WriteLine($"  [{expansion}] Loaded {vendorItems.Count} vendors, {itemLookup.Count} items, {foodIds.Count} foods, {waterIds.Count} waters");

    // Register every vendor in ground-truth set — even ones that don't need
    // flag changes, since "no VendorAmmo" is ground truth too.
    foreach (string entryStr in vendorItems.Keys)
        dbClassifiedVendors.Add((expansion, int.Parse(entryStr)));

    foreach (JObject creature in creatures)
    {
        int npcFlag = creature.Value<int>("NpcFlag");

        int entry = creature.Value<int>("Entry");
        if (!vendorItems.TryGetValue(entry.ToString(), out int[]? itemIds))
            continue;

        int dbSubtypes = ClassifyVendorByItems(itemIds, itemLookup, foodIds, waterIds);
        int currentSubtypes = npcFlag & VendorSubtypeMask;
        bool hasVendorBase = (npcFlag & (int)NpcFlags.Vendor) != 0;

        if (dbSubtypes == currentSubtypes && hasVendorBase)
            continue;

        // Replace sub-type flags with DB ground truth
        int newFlag = (npcFlag & ~VendorSubtypeMask) | dbSubtypes | (int)NpcFlags.Vendor;
        string name = creature.Value<string>("Name") ?? "?";
        string subName = creature.Value<string>("SubName") ?? "";

        int added = dbSubtypes & ~currentSubtypes;
        int removed = currentSubtypes & ~dbSubtypes;
        string detail = "";
        if (added != 0) detail += $"+{(NpcFlags)added}";
        if (removed != 0) detail += (detail.Length > 0 ? " " : "") + $"-{(NpcFlags)removed}";

        Console.WriteLine($"  [{expansion}] [{entry}] {name} \"{subName}\": {npcFlag} -> {newFlag} ({detail})");

        creature["NpcFlag"] = newFlag;
        phaseDbCount++;
    }

    // ── Phase DB Repair: Apply Wowhead repair ground truth ──
    string repairPath = Path.Combine(basePath, expansion, "repair.json");
    if (File.Exists(repairPath))
    {
        HashSet<int> repairNpcIds = new(
            JsonConvert.DeserializeObject<int[]>(File.ReadAllText(repairPath))!);

        Console.WriteLine($"  [{expansion}] Loaded {repairNpcIds.Count} repair NPCs from ground truth");

        foreach (JObject creature in creatures)
        {
            int entry = creature.Value<int>("Entry");
            int npcFlag = creature.Value<int>("NpcFlag");
            bool hasRepair = (npcFlag & (int)NpcFlags.Repair) != 0;
            bool shouldRepair = repairNpcIds.Contains(entry);

            if (shouldRepair && !hasRepair)
            {
                int newFlag = npcFlag | (int)NpcFlags.Repair;
                string name = creature.Value<string>("Name") ?? "?";
                Console.WriteLine($"  [{expansion}] [{entry}] {name}: {npcFlag} -> {newFlag} (+Repair)");
                creature["NpcFlag"] = newFlag;
                phaseDbCount++;
            }
            else if (!shouldRepair && hasRepair)
            {
                int newFlag = npcFlag & ~(int)NpcFlags.Repair;
                string name = creature.Value<string>("Name") ?? "?";
                Console.WriteLine($"  [{expansion}] [{entry}] {name}: {npcFlag} -> {newFlag} (-Repair)");
                creature["NpcFlag"] = newFlag;
                phaseDbCount++;
            }
        }
    }

    // ── Phase DB Trainer: Apply Wowhead trainer ground truth ──
    string trainerPath = Path.Combine(basePath, expansion, "trainer.json");
    string classTrainerPath = Path.Combine(basePath, expansion, "classtrainer.json");
    if (File.Exists(trainerPath))
    {
        HashSet<int> trainerNpcIds = new(
            JsonConvert.DeserializeObject<int[]>(File.ReadAllText(trainerPath))!);

        HashSet<int> classTrainerNpcIds = File.Exists(classTrainerPath)
            ? new(JsonConvert.DeserializeObject<int[]>(File.ReadAllText(classTrainerPath))!)
            : [];

        Console.WriteLine($"  [{expansion}] Loaded {trainerNpcIds.Count} trainers, {classTrainerNpcIds.Count} class trainers from ground truth");

        foreach (JObject creature in creatures)
        {
            int entry = creature.Value<int>("Entry");
            int npcFlag = creature.Value<int>("NpcFlag");
            int currentTrainer = npcFlag & TrainerMask;

            int expectedTrainer;
            if (classTrainerNpcIds.Contains(entry))
                expectedTrainer = (int)(NpcFlags.Trainer | NpcFlags.ClassTrainer);
            else if (trainerNpcIds.Contains(entry))
                expectedTrainer = (int)(NpcFlags.Trainer | NpcFlags.ProfessionTrainer);
            else if (currentTrainer != 0)
                expectedTrainer = 0; // false positive — strip all trainer flags
            else
                continue; // not a trainer, no flags to change

            if (currentTrainer == expectedTrainer)
                continue;

            int newFlag = (npcFlag & ~TrainerMask) | expectedTrainer;
            string name = creature.Value<string>("Name") ?? "?";

            int added = expectedTrainer & ~currentTrainer;
            int removed = currentTrainer & ~expectedTrainer;
            string detail = "";
            if (added != 0) detail += $"+{(NpcFlags)added}";
            if (removed != 0) detail += (detail.Length > 0 ? " " : "") + $"-{(NpcFlags)removed}";

            Console.WriteLine($"  [{expansion}] [{entry}] {name}: {npcFlag} -> {newFlag} ({detail})");
            creature["NpcFlag"] = newFlag;
            phaseDbCount++;
        }
    }

    // ── Phase DB FlightMaster: Apply Wowhead flightmaster ground truth ──
    string flightMasterPath = Path.Combine(basePath, expansion, "flightmaster.json");
    if (File.Exists(flightMasterPath))
    {
        HashSet<int> flightMasterNpcIds = new(
            JsonConvert.DeserializeObject<int[]>(File.ReadAllText(flightMasterPath))!);

        Console.WriteLine($"  [{expansion}] Loaded {flightMasterNpcIds.Count} flightmaster NPCs from ground truth");

        foreach (JObject creature in creatures)
        {
            int entry = creature.Value<int>("Entry");
            int npcFlag = creature.Value<int>("NpcFlag");
            bool hasFlightMaster = (npcFlag & (int)NpcFlags.FlightMaster) != 0;
            bool shouldFlightMaster = flightMasterNpcIds.Contains(entry);

            if (shouldFlightMaster && !hasFlightMaster)
            {
                int newFlag = npcFlag | (int)NpcFlags.FlightMaster;
                string name = creature.Value<string>("Name") ?? "?";
                Console.WriteLine($"  [{expansion}] [{entry}] {name}: {npcFlag} -> {newFlag} (+FlightMaster)");
                creature["NpcFlag"] = newFlag;
                phaseDbCount++;
            }
            else if (!shouldFlightMaster && hasFlightMaster)
            {
                int newFlag = npcFlag & ~(int)NpcFlags.FlightMaster;
                string name = creature.Value<string>("Name") ?? "?";
                Console.WriteLine($"  [{expansion}] [{entry}] {name}: {npcFlag} -> {newFlag} (-FlightMaster)");
                creature["NpcFlag"] = newFlag;
                phaseDbCount++;
            }
        }
    }
}

Console.WriteLine($"Phase DB updated: {phaseDbCount}");
Console.WriteLine();

// ── Phase 0: Strip incorrect vendor sub-type flags ──
// Some DBC entries have erroneous vendor sub-type flags that don't match
// what the NPC actually sells. Strip them before cross-referencing
// so the bad flags don't propagate.
Console.WriteLine("=== Phase 0: Strip incorrect vendor sub-type flags ===");

int phase0Count = StripIncorrectFlags(files);

Console.WriteLine($"Phase 0 updated: {phase0Count}");
Console.WriteLine();

// ── Phase 1: Cross-reference by Entry ID ──
// If the same NPC has vendor sub-type flags in any file, propagate them to all files.
Console.WriteLine("=== Phase 1: Cross-reference by Entry ID ===");

Dictionary<int, int> entrySubtypes = [];
foreach ((string label, JArray creatures) in files)
{
    foreach (JObject creature in creatures)
    {
        int npcFlag = creature.Value<int>("NpcFlag");
        if ((npcFlag & (int)NpcFlags.Vendor) == 0)
            continue;

        int subtypes = npcFlag & VendorSubtypeMask;
        if (subtypes == 0)
            continue;

        int entry = creature.Value<int>("Entry");
        if (entrySubtypes.TryGetValue(entry, out int existing))
            entrySubtypes[entry] = existing | subtypes;
        else
            entrySubtypes[entry] = subtypes;
    }
}

int phase1Count = ApplyFlags(files, entrySubtypes, "Phase1", dbClassifiedVendors);
Console.WriteLine($"Phase 1 updated: {phase1Count}");
Console.WriteLine();

// ── Phase 2: SubName exact-match lookup ──
// Build a map of SubName → union of sub-type flags from all entries with that SubName.
Console.WriteLine("=== Phase 2: SubName exact-match lookup ===");

Dictionary<string, int> subnameSubtypes = [];
foreach ((string label, JArray creatures) in files)
{
    foreach (JObject creature in creatures)
    {
        int npcFlag = creature.Value<int>("NpcFlag");
        if ((npcFlag & (int)NpcFlags.Vendor) == 0)
            continue;

        int subtypes = npcFlag & VendorSubtypeMask;
        if (subtypes == 0)
            continue;

        string subName = creature.Value<string>("SubName") ?? "";
        if (subName.Length == 0)
            continue;

        if (subnameSubtypes.TryGetValue(subName, out int existing))
            subnameSubtypes[subName] = existing | subtypes;
        else
            subnameSubtypes[subName] = subtypes;
    }
}

int phase2Count = ApplyFlagsBySubName(files, subnameSubtypes, "Phase2", dbClassifiedVendors);
Console.WriteLine($"Phase 2 updated: {phase2Count}");
Console.WriteLine();

// ── Phase 3: Keyword-based classification for remaining entries ──
Console.WriteLine("=== Phase 3: Keyword-based classification ===");

int phase3Count = ApplyKeywordClassification(files, dbClassifiedVendors);
Console.WriteLine($"Phase 3 updated: {phase3Count}");
Console.WriteLine();

// ── Final pass: Re-strip incorrect flags that Phase 1/2/3 may have re-introduced ──
// Cross-version Entry ID propagation (Phase 1) can re-add bad flags when the same
// NPC has a different SubName in another version. SubName matching (Phase 2) then
// amplifies the contamination. A final strip ensures the corrections stick.
Console.WriteLine("=== Final pass: Re-strip incorrect vendor sub-type flags ===");

int finalStripCount = StripIncorrectFlags(files);

Console.WriteLine($"Final pass updated: {finalStripCount}");
Console.WriteLine();

// ── Phase Exclude: Strip vendor/repair flags from inaccessible NPCs ──
Console.WriteLine("=== Phase Exclude: Strip vendor/repair flags from inaccessible NPCs ===");

int excludeCount = 0;
foreach ((string label, JArray creatures) in files)
{
    foreach (JObject creature in creatures)
    {
        int entry = creature.Value<int>("Entry");
        if (!excludedVendorEntries.Contains(entry))
            continue;

        int npcFlag = creature.Value<int>("NpcFlag");
        int stripped = npcFlag & VendorServiceMask;
        if (stripped == 0)
            continue;

        int newFlag = npcFlag & ~VendorServiceMask;
        string name = creature.Value<string>("Name") ?? "?";
        Console.WriteLine($"  [{label}] [{entry}] {name}: {npcFlag} -> {newFlag} (-{(NpcFlags)(uint)stripped})");

        creature["NpcFlag"] = newFlag;
        excludeCount++;
    }
}

Console.WriteLine($"Phase Exclude updated: {excludeCount}");
Console.WriteLine();

// ── Save ──
int totalUpdated = remapCount + phaseDbCount + phase0Count + phase1Count + phase2Count + phase3Count + finalStripCount + excludeCount;
Console.WriteLine($"Total updated: {totalUpdated}");

if (totalUpdated > 0)
{
    foreach (string filePath in filePaths)
    {
        string label = Path.GetFileName(Path.GetDirectoryName(filePath))!;
        if (files.TryGetValue(label, out JArray? creatures))
        {
            File.WriteAllText(filePath, creatures.ToString(Newtonsoft.Json.Formatting.Indented));
            Console.WriteLine($"Written: {filePath}");
        }
    }
}

return 0;

// ── Helper methods ──

int RemapSomNpcFlags(Dictionary<string, JArray> allFiles)
{
    if (!allFiles.TryGetValue("som", out JArray? creatures))
        return 0;

    int count = 0;
    foreach (JObject creature in creatures)
    {
        int orig = creature.Value<int>("NpcFlag");
        if (orig == 0)
            continue;

        int result = 0;

        // Same position: bits 0 (Gossip), 1 (QuestGiver), 4 (Trainer)
        result |= orig & ((1 << 0) | (1 << 1) | (1 << 4));

        // Conditional bits 5-6 based on Trainer (bit 4)
        // When Trainer is set: 5=ClassTrainer, 6=ProfessionTrainer (same as TBC+)
        // When Trainer is not set: 5=SpiritHealer, 6=SpiritGuide (Classic positions)
        bool isTrainer = (orig & (1 << 4)) != 0;
        if (isTrainer)
        {
            result |= orig & ((1 << 5) | (1 << 6));
        }
        else
        {
            if ((orig & (1 << 5)) != 0) result |= 1 << 14;  // SpiritHealer
            if ((orig & (1 << 6)) != 0) result |= 1 << 15;  // SpiritGuide
        }

        // Classic Vendor (bit 2) → merge into bit 7 (TBC+ Vendor)
        bool classicVendor = (orig & (1 << 2)) != 0;
        if (classicVendor)
            result |= 1 << 7;

        // Conditional bits 7-11 based on Classic Vendor bit 2
        if (classicVendor)
        {
            // Keep as-is: 7=Vendor, 8=VendorAmmo, 9=VendorFood,
            // 10=VendorPoison, 11=VendorReagent
            result |= orig & ((1 << 7) | (1 << 8) | (1 << 9) | (1 << 10) | (1 << 11));
        }
        else
        {
            // bit 7 stays (edge case — non-vendor with bit 7 set)
            result |= orig & (1 << 7);
            // 8→17 Banker, 9→18 Petitioner, 10→19 TabardDesigner, 11→20 Battlemaster
            if ((orig & (1 << 8)) != 0) result |= 1 << 17;
            if ((orig & (1 << 9)) != 0) result |= 1 << 18;
            if ((orig & (1 << 10)) != 0) result |= 1 << 19;
            if ((orig & (1 << 11)) != 0) result |= 1 << 20;
        }

        // Unconditional remaps
        if ((orig & (1 << 3)) != 0) result |= 1 << 13;   // FlightMaster
        if ((orig & (1 << 12)) != 0) result |= 1 << 21;  // Auctioneer
        if ((orig & (1 << 13)) != 0) result |= 1 << 22;  // StableMaster
        if ((orig & (1 << 14)) != 0) result |= 1 << 12;  // Repair

        // Bits ≥15: carry over from original (rare, already TBC+ position)
        result |= orig & unchecked((int)0xFFFF8000);

        if (result != orig)
        {
            string name = creature.Value<string>("Name") ?? "?";
            int entry = creature.Value<int>("Entry");
            Console.WriteLine($"  [som] [{entry}] {name}: {orig} -> {result} ({(NpcFlags)orig} -> {(NpcFlags)(uint)result})");

            creature["NpcFlag"] = result;
            count++;
        }
    }

    return count;
}

int ApplyFlags(Dictionary<string, JArray> allFiles, Dictionary<int, int> lookup, string phase,
    HashSet<(string, int)> dbClassifiedVendors)
{
    int count = 0;
    foreach ((string label, JArray creatures) in allFiles)
    {
        foreach (JObject creature in creatures)
        {
            int npcFlag = creature.Value<int>("NpcFlag");
            if ((npcFlag & (int)NpcFlags.Vendor) == 0)
                continue;

            int entry = creature.Value<int>("Entry");

            // Skip vendors whose flags are set from ground-truth vendoritems.json
            if (dbClassifiedVendors.Contains((label, entry)))
                continue;

            if (!lookup.TryGetValue(entry, out int targetSubtypes))
                continue;

            int currentSubtypes = npcFlag & VendorSubtypeMask;
            int missing = targetSubtypes & ~currentSubtypes;
            if (missing == 0)
                continue;

            int newFlag = npcFlag | missing;
            string name = creature.Value<string>("Name") ?? "?";
            string subName = creature.Value<string>("SubName") ?? "";
            Console.WriteLine($"  [{label}] [{entry}] {name} \"{subName}\": {npcFlag} -> {newFlag} (+{(NpcFlags)missing})");

            creature["NpcFlag"] = newFlag;
            count++;
        }
    }

    return count;
}

int ApplyFlagsBySubName(Dictionary<string, JArray> allFiles, Dictionary<string, int> lookup, string phase,
    HashSet<(string, int)> dbClassifiedVendors)
{
    int count = 0;
    foreach ((string label, JArray creatures) in allFiles)
    {
        foreach (JObject creature in creatures)
        {
            int npcFlag = creature.Value<int>("NpcFlag");
            if ((npcFlag & (int)NpcFlags.Vendor) == 0)
                continue;

            int entry = creature.Value<int>("Entry");

            // Skip vendors whose flags are set from ground-truth vendoritems.json
            if (dbClassifiedVendors.Contains((label, entry)))
                continue;

            string subName = creature.Value<string>("SubName") ?? "";
            if (subName.Length == 0)
                continue;

            if (!lookup.TryGetValue(subName, out int targetSubtypes))
                continue;

            int currentSubtypes = npcFlag & VendorSubtypeMask;
            int missing = targetSubtypes & ~currentSubtypes;
            if (missing == 0)
                continue;

            int newFlag = npcFlag | missing;
            string name = creature.Value<string>("Name") ?? "?";
            Console.WriteLine($"  [{label}] [{entry}] {name} \"{subName}\": {npcFlag} -> {newFlag} (+{(NpcFlags)missing})");

            creature["NpcFlag"] = newFlag;
            count++;
        }
    }

    return count;
}

int ApplyKeywordClassification(Dictionary<string, JArray> allFiles,
    HashSet<(string, int)> dbClassifiedVendors)
{
    int count = 0;
    foreach ((string label, JArray creatures) in allFiles)
    {
        foreach (JObject creature in creatures)
        {
            int npcFlag = creature.Value<int>("NpcFlag");
            if ((npcFlag & (int)NpcFlags.Vendor) == 0)
                continue;

            int entry = creature.Value<int>("Entry");

            // Skip vendors whose flags are set from ground-truth vendoritems.json
            if (dbClassifiedVendors.Contains((label, entry)))
                continue;

            string subName = creature.Value<string>("SubName") ?? "";
            if (subName.Length == 0)
                continue;

            int addBits = ClassifyByKeywords(subName);
            if (addBits == 0)
                continue;

            int missing = addBits & ~(npcFlag & VendorSubtypeMask);
            if (missing == 0)
                continue;

            int newFlag = npcFlag | missing;
            string name = creature.Value<string>("Name") ?? "?";
            Console.WriteLine($"  [{label}] [{entry}] {name} \"{subName}\": {npcFlag} -> {newFlag} (+{(NpcFlags)missing})");

            creature["NpcFlag"] = newFlag;
            count++;
        }
    }

    return count;
}

int ClassifyByKeywords(string subName)
{
    int bits = 0;

    // VendorFood keywords
    if (ContainsAny(subName,
        "Food", "Drink", "Cook", "Baker", "Barkeep", "Barmaid",
        "Bartender", "Butcher", "Chef", "Fruit", "Fungus",
        "Mushroom", "Cheese", "Meat", "Wine", "Ale ",
        "Ale &", "Ale and", "Brew", "Innkeeper", "Fishmonger",
        "Pie,", "Pie ", "Rations", "Refreshments", "Waitress",
        "Snacks", "Provisioner", "Smokywood"))
    {
        bits |= (int)NpcFlags.VendorFood;
    }

    // VendorAmmo keywords
    if (ContainsAny(subName,
        "Ammo", "Ammunition", "Bowyer", "Fletcher", "Fletching",
        "Gunsmith", "Guns ", "Guns &", "Gun Merchant"))
    {
        bits |= (int)NpcFlags.VendorAmmo;
    }

    // VendorPoison keywords
    if (ContainsAny(subName,
        "Poison"))
    {
        bits |= (int)NpcFlags.VendorPoison;
    }

    // VendorReagent keywords
    if (ContainsAny(subName,
        "Reagent"))
    {
        bits |= (int)NpcFlags.VendorReagent;
    }

    return bits;
}

int StripIncorrectFlags(Dictionary<string, JArray> allFiles)
{
    int count = 0;
    foreach ((string label, JArray creatures) in allFiles)
    {
        foreach (JObject creature in creatures)
        {
            int npcFlag = creature.Value<int>("NpcFlag");
            string subName = creature.Value<string>("SubName") ?? "";

            // Strip VendorAmmo from Innkeepers — they sell food/drink, not ammunition
            if ((npcFlag & (int)NpcFlags.VendorAmmo) != 0
                && subName.Equals("Innkeeper", StringComparison.OrdinalIgnoreCase))
            {
                int newFlag = npcFlag & ~(int)NpcFlags.VendorAmmo;
                int entry = creature.Value<int>("Entry");
                string name = creature.Value<string>("Name") ?? "?";
                Console.WriteLine($"  [{label}] [{entry}] {name} \"{subName}\": {npcFlag} -> {newFlag} (-{nameof(NpcFlags.VendorAmmo)})");

                creature["NpcFlag"] = newFlag;
                npcFlag = newFlag;
                count++;
            }

            // Strip VendorFood from Fishing Supplies vendors — they sell poles/lures, not food
            if ((npcFlag & (int)NpcFlags.VendorFood) != 0
                && subName.Equals("Fishing Supplies", StringComparison.OrdinalIgnoreCase))
            {
                int newFlag = npcFlag & ~(int)NpcFlags.VendorFood;
                int entry = creature.Value<int>("Entry");
                string name = creature.Value<string>("Name") ?? "?";
                Console.WriteLine($"  [{label}] [{entry}] {name} \"{subName}\": {npcFlag} -> {newFlag} (-{nameof(NpcFlags.VendorFood)})");

                creature["NpcFlag"] = newFlag;
                npcFlag = newFlag;
                count++;
            }

            // Strip VendorPoison from reagent-only vendors — pure reagent vendors don't sell poisons.
            // Keep VendorPoison on vendors with "Poison" in SubName (e.g. "Poisons & Reagents").
            if ((npcFlag & (int)NpcFlags.VendorPoison) != 0
                && subName.Contains("Reagent", StringComparison.OrdinalIgnoreCase)
                && !subName.Contains("Poison", StringComparison.OrdinalIgnoreCase))
            {
                int newFlag = npcFlag & ~(int)NpcFlags.VendorPoison;
                int entry = creature.Value<int>("Entry");
                string name = creature.Value<string>("Name") ?? "?";
                Console.WriteLine($"  [{label}] [{entry}] {name} \"{subName}\": {npcFlag} -> {newFlag} (-{nameof(NpcFlags.VendorPoison)})");

                creature["NpcFlag"] = newFlag;
                npcFlag = newFlag;
                count++;
            }
        }
    }

    return count;
}

int ClassifyVendorByItems(int[] itemIds, Dictionary<int, Item> itemLookup,
    HashSet<int> foodIds, HashSet<int> waterIds)
{
    int bits = 0;
    int poisonReagentCount = 0;

    foreach (int itemId in itemIds)
    {
        if (!itemLookup.TryGetValue(itemId, out Item item))
            continue;

        // Count rogue poison reagent items regardless of ItemClass
        // (SoM: TradeGoods, TBC: Miscellaneous/Reagent)
        if (item.Name.Contains("Dust of Decay", StringComparison.OrdinalIgnoreCase)
            || item.Name.Contains("Essence of Pain", StringComparison.OrdinalIgnoreCase)
            || item.Name.Contains("Essence of Agony", StringComparison.OrdinalIgnoreCase)
            || item.Name.Contains("Deathweed", StringComparison.OrdinalIgnoreCase))
            poisonReagentCount++;

        switch (item.ClassId)
        {
            case ItemClass.Consumable:
                ItemConsumableSubclass consumeSub = (ItemConsumableSubclass)item.SubclassId;
                if (consumeSub == ItemConsumableSubclass.FoodAndDrink
                    || foodIds.Contains(itemId) || waterIds.Contains(itemId))
                    bits |= (int)NpcFlags.VendorFood;
                else if (consumeSub == ItemConsumableSubclass.Consumable
                    && item.Name.Contains("Poison", StringComparison.OrdinalIgnoreCase))
                    bits |= (int)NpcFlags.VendorPoison;
                break;

            case ItemClass.Projectile:
            case ItemClass.Quiver:
                bits |= (int)NpcFlags.VendorAmmo;
                break;

            case ItemClass.Reagent:
                bits |= (int)NpcFlags.VendorReagent;
                break;

            case ItemClass.Miscellaneous:
                if ((ItemMiscellaneousSubclass)item.SubclassId == ItemMiscellaneousSubclass.Reagent)
                    bits |= (int)NpcFlags.VendorReagent;
                break;
        }
    }

    // Only flag VendorPoison if vendor carries 2+ distinct poison reagent items.
    // Generic trade vendors typically stock only Dust of Decay (count=1).
    // True poison suppliers carry all 4 reagents (count=4).
    if (poisonReagentCount >= 2)
        bits |= (int)NpcFlags.VendorPoison;

    return bits;
}

bool ContainsAny(string text, params string[] keywords)
{
    foreach (string keyword in keywords)
    {
        if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            return true;
    }

    return false;
}

bool HasVendorSubName(string subName)
{
    // Food/Drink vendor keywords (from ClassifyByKeywords)
    if (ContainsAny(subName,
        "Food", "Drink", "Cook", "Baker", "Barkeep", "Barmaid",
        "Bartender", "Butcher", "Chef", "Fruit", "Fungus",
        "Mushroom", "Cheese", "Meat", "Wine", "Ale ",
        "Ale &", "Ale and", "Brew", "Innkeeper", "Fishmonger",
        "Pie,", "Pie ", "Rations", "Refreshments", "Waitress",
        "Snacks", "Provisioner", "Smokywood"))
        return true;

    // Ammo vendor keywords (from ClassifyByKeywords)
    if (ContainsAny(subName,
        "Ammo", "Ammunition", "Bowyer", "Fletcher", "Fletching",
        "Gunsmith", "Guns ", "Guns &", "Gun Merchant"))
        return true;

    // Poison/Reagent keywords (from ClassifyByKeywords)
    if (ContainsAny(subName, "Poison", "Reagent"))
        return true;

    // General vendor/merchant keywords
    if (ContainsAny(subName,
        "Vendor", "Merchant", "Supplies", "Goods", "Trader",
        "Dealer", "Shop", "Store", "Armor", "Weapon",
        "Leather", "Cloth", "Mail", "Plate", "Blacksmith",
        "Metalsmith", "Macecrafter", "Swordsmith", "Mining",
        "Engineering", "Tailoring", "Leatherworking", "Herbalism",
        "Alchemy", "Enchanting", "Fishing", "Skinning",
        "Pet", "Mount", "Stable", "Tabard Vendor",
        "Fireworks", "Explosive"))
        return true;

    return false;
}

void RunAudit(Dictionary<string, JArray> allFiles, string dbcBasePath)
{
    NpcFlags[] vendorSubFlags =
    [
        NpcFlags.VendorFood,
        NpcFlags.VendorAmmo,
        NpcFlags.VendorPoison,
        NpcFlags.VendorReagent
    ];

    string[] expansions = ["som", "tbc"];
    foreach (string expansion in expansions)
    {
        string vendorItemsPath = Path.Combine(dbcBasePath, expansion, "vendoritems.json");
        string itemsPath = Path.Combine(dbcBasePath, expansion, "items.json");

        if (!File.Exists(vendorItemsPath))
        {
            Console.WriteLine($"[{expansion}] vendoritems.json not found, skipping audit.");
            continue;
        }

        if (!File.Exists(itemsPath))
        {
            Console.WriteLine($"[{expansion}] items.json not found, skipping audit.");
            continue;
        }

        if (!allFiles.TryGetValue(expansion, out JArray? creatures))
        {
            Console.WriteLine($"[{expansion}] creatures.json not loaded, skipping audit.");
            continue;
        }

        Dictionary<string, int[]> vendorItems =
            JsonConvert.DeserializeObject<Dictionary<string, int[]>>(
                File.ReadAllText(vendorItemsPath))!;

        Dictionary<int, Item> itemLookup = JsonConvert.DeserializeObject<List<Item>>(
            File.ReadAllText(itemsPath))!
            .ToDictionary(i => i.Entry);

        // Load food/water ID sets for fallback classification
        string foodsPath = Path.Combine(dbcBasePath, expansion, "foods.json");
        string watersPath = Path.Combine(dbcBasePath, expansion, "waters.json");

        HashSet<int> foodIds = File.Exists(foodsPath)
            ? JsonConvert.DeserializeObject<HashSet<int>>(File.ReadAllText(foodsPath))!
            : [];

        HashSet<int> waterIds = File.Exists(watersPath)
            ? JsonConvert.DeserializeObject<HashSet<int>>(File.ReadAllText(watersPath))!
            : [];

        Dictionary<int, JObject> creatureLookup = creatures
            .Cast<JObject>()
            .ToDictionary(c => c.Value<int>("Entry"));

        Console.WriteLine($"=== Audit: {expansion} ===");

        int audited = 0;
        int okCount = 0;
        int mismatchCount = 0;
        int notInCreatures = 0;
        Dictionary<string, int> plusCounts = new()
        {
            ["Vendor"] = 0,
            ["VendorFood"] = 0,
            ["VendorAmmo"] = 0,
            ["VendorPoison"] = 0,
            ["VendorReagent"] = 0
        };
        Dictionary<string, int> minusCounts = new()
        {
            ["VendorFood"] = 0,
            ["VendorAmmo"] = 0,
            ["VendorPoison"] = 0,
            ["VendorReagent"] = 0
        };

        foreach ((string entryStr, int[] itemIds) in vendorItems)
        {
            int entry = int.Parse(entryStr);
            audited++;

            if (!creatureLookup.TryGetValue(entry, out JObject? creature))
            {
                Console.WriteLine($"[{entry}] NOT IN creatures.json");
                notInCreatures++;
                continue;
            }

            int npcFlag = creature.Value<int>("NpcFlag");
            string name = creature.Value<string>("Name") ?? "?";
            string subName = creature.Value<string>("SubName") ?? "";
            string subNameDisplay = subName.Length > 0 ? $" \"{subName}\"" : "";

            NpcFlags decoded = (NpcFlags)npcFlag;
            Console.WriteLine($"[{entry}] {name}{subNameDisplay}: NpcFlag={npcFlag} ({decoded})");

            string breakdown = FormatItemBreakdown(itemIds, itemLookup);
            Console.WriteLine($"  Items: {breakdown}");

            int expectedSubtypes = ClassifyVendorByItems(itemIds, itemLookup, foodIds, waterIds);
            int currentSubtypes = npcFlag & VendorSubtypeMask;
            bool hasVendorBase = (npcFlag & (int)NpcFlags.Vendor) != 0;

            string expectedStr = expectedSubtypes != 0 ? ((NpcFlags)expectedSubtypes).ToString() : "(none)";
            string currentStr = currentSubtypes != 0 ? ((NpcFlags)currentSubtypes).ToString() : "(none)";
            Console.Write($"  Expected: {expectedStr}  Current: {currentStr}");

            // Determine mismatches
            List<string> issues = [];

            // Check base Vendor flag: if NPC sells items, it should have Vendor
            if (!hasVendorBase)
            {
                issues.Add("+Vendor");
                plusCounts["Vendor"]++;
            }

            // Check sub-type flags
            foreach (NpcFlags flag in vendorSubFlags)
            {
                int flagVal = (int)flag;
                bool expected = (expectedSubtypes & flagVal) != 0;
                bool current = (currentSubtypes & flagVal) != 0;

                if (expected && !current)
                {
                    string flagName = flag.ToString();
                    issues.Add($"+{flagName}");
                    plusCounts[flagName]++;
                }
                else if (!expected && current)
                {
                    string flagName = flag.ToString();
                    issues.Add($"-{flagName}");
                    minusCounts[flagName]++;
                }
            }

            if (issues.Count == 0)
            {
                Console.WriteLine("  -> OK");
                okCount++;
            }
            else
            {
                Console.WriteLine($"  -> MISMATCH: {string.Join(", ", issues)}");
                mismatchCount++;
            }

            Console.WriteLine();
        }

        Console.WriteLine($"Audit summary [{expansion}]:");
        Console.WriteLine($"  Vendors audited: {audited}");
        Console.WriteLine($"  OK: {okCount}");
        Console.WriteLine($"  Mismatches: {mismatchCount}");
        Console.WriteLine($"    +Vendor (base flag missing): {plusCounts["Vendor"]}");

        foreach (NpcFlags flag in vendorSubFlags)
        {
            string flagName = flag.ToString();
            Console.WriteLine($"    +{flagName}: {plusCounts[flagName]}  -{flagName}: {minusCounts[flagName]}");
        }

        Console.WriteLine($"  Not in creatures.json: {notInCreatures}");
        Console.WriteLine();

        // === False Vendor Heuristic Report ===
        List<(int entry, string name, string subName, int itemCount, string itemNames)> highSuspicion = [];
        List<(int entry, string name, string subName, int itemCount, string itemNames)> mediumSuspicion = [];
        List<(int entry, string name, string subName, int itemCount, string itemNames)> lowSuspicion = [];

        foreach ((string entryStr, int[] itemIds) in vendorItems)
        {
            int entry = int.Parse(entryStr);

            if (!creatureLookup.TryGetValue(entry, out JObject? creature))
                continue;

            string name = creature.Value<string>("Name") ?? "?";
            string subName = creature.Value<string>("SubName") ?? "";

            bool singleItem = itemIds.Length == 1;
            bool vendorSubName = subName.Length == 0 || HasVendorSubName(subName);

            // Format item names for display
            string itemNames = string.Join(", ", itemIds
                .Select(id => itemLookup.TryGetValue(id, out Item item) ? item.Name : $"#{id}")
                .Take(5));
            if (itemIds.Length > 5)
                itemNames += $" (+{itemIds.Length - 5} more)";

            if (singleItem && !vendorSubName)
                highSuspicion.Add((entry, name, subName, itemIds.Length, itemNames));
            else if (!singleItem && !vendorSubName)
                mediumSuspicion.Add((entry, name, subName, itemIds.Length, itemNames));
            else if (singleItem && vendorSubName)
                lowSuspicion.Add((entry, name, subName, itemIds.Length, itemNames));
        }

        Console.WriteLine($"=== False Vendor Heuristic Report [{expansion}] ===");
        Console.WriteLine();

        if (highSuspicion.Count > 0)
        {
            Console.WriteLine("--- HIGH SUSPICION (single-item + non-vendor SubName) ---");
            foreach ((int entry, string name, string subName, int itemCount, string itemNames) in highSuspicion)
                Console.WriteLine($"  [{entry}] {name} \"{subName}\" | {itemCount} item: {itemNames}");
            Console.WriteLine();
        }

        if (mediumSuspicion.Count > 0)
        {
            Console.WriteLine("--- MEDIUM SUSPICION (multi-item + non-vendor SubName) ---");
            foreach ((int entry, string name, string subName, int itemCount, string itemNames) in mediumSuspicion)
                Console.WriteLine($"  [{entry}] {name} \"{subName}\" | {itemCount} items: {itemNames}");
            Console.WriteLine();
        }

        if (lowSuspicion.Count > 0)
        {
            Console.WriteLine("--- LOW SUSPICION (single-item + vendor SubName) ---");
            foreach ((int entry, string name, string subName, int itemCount, string itemNames) in lowSuspicion)
                Console.WriteLine($"  [{entry}] {name} \"{subName}\" | {itemCount} item: {itemNames}");
            Console.WriteLine();
        }

        Console.WriteLine("Summary:");
        Console.WriteLine($"  HIGH: {highSuspicion.Count}  MEDIUM: {mediumSuspicion.Count}  LOW: {lowSuspicion.Count}");
        Console.WriteLine();
    }
}

string FormatItemBreakdown(int[] itemIds, Dictionary<int, Item> itemLookup)
{
    Dictionary<(ItemClass, int), int> groups = [];
    foreach (int itemId in itemIds)
    {
        if (!itemLookup.TryGetValue(itemId, out Item item))
            continue;

        (ItemClass, int) key = (item.ClassId, item.SubclassId);
        if (groups.TryGetValue(key, out int count))
            groups[key] = count + 1;
        else
            groups[key] = 1;
    }

    return string.Join(", ", groups
        .OrderByDescending(g => g.Value)
        .Select(g => $"{FormatSubclassName(g.Key.Item1, g.Key.Item2)}: {g.Value}"));
}

string FormatSubclassName(ItemClass classId, int subclassId)
{
    string className = classId.ToString();
    string subName = classId switch
    {
        ItemClass.Consumable => Enum.IsDefined((ItemConsumableSubclass)subclassId)
            ? ((ItemConsumableSubclass)subclassId).ToString() : subclassId.ToString(),
        ItemClass.Container => Enum.IsDefined((ItemContainerSubclass)subclassId)
            ? ((ItemContainerSubclass)subclassId).ToString() : subclassId.ToString(),
        ItemClass.Weapon => Enum.IsDefined((ItemWeaponSubclass)subclassId)
            ? ((ItemWeaponSubclass)subclassId).ToString() : subclassId.ToString(),
        ItemClass.Gem => Enum.IsDefined((ItemGemSubclass)subclassId)
            ? ((ItemGemSubclass)subclassId).ToString() : subclassId.ToString(),
        ItemClass.Armor => Enum.IsDefined((ItemArmorSubclass)subclassId)
            ? ((ItemArmorSubclass)subclassId).ToString() : subclassId.ToString(),
        ItemClass.Projectile => Enum.IsDefined((ItemProjectileSubclass)subclassId)
            ? ((ItemProjectileSubclass)subclassId).ToString() : subclassId.ToString(),
        ItemClass.TradeGoods => Enum.IsDefined((ItemTradeGoodsSubclass)subclassId)
            ? ((ItemTradeGoodsSubclass)subclassId).ToString() : subclassId.ToString(),
        ItemClass.Recipe => Enum.IsDefined((ItemRecipeSubclass)subclassId)
            ? ((ItemRecipeSubclass)subclassId).ToString() : subclassId.ToString(),
        ItemClass.Quiver => Enum.IsDefined((ItemQuiverSubclass)subclassId)
            ? ((ItemQuiverSubclass)subclassId).ToString() : subclassId.ToString(),
        ItemClass.Miscellaneous => Enum.IsDefined((ItemMiscellaneousSubclass)subclassId)
            ? ((ItemMiscellaneousSubclass)subclassId).ToString() : subclassId.ToString(),
        _ => subclassId.ToString()
    };

    return $"{className}/{subName}";
}
