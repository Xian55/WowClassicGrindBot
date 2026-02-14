using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using SharedLib;
using SharedLib.Data;

const int VendorSubtypeMask = (int)(NpcFlags.VendorAmmo | NpcFlags.VendorFood | NpcFlags.VendorPoison | NpcFlags.VendorReagent);

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

// ── Save ──
int totalUpdated = phaseDbCount + phase0Count + phase1Count + phase2Count + phase3Count + finalStripCount;
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
