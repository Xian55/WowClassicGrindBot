using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;

using Newtonsoft.Json.Linq;

using SharedLib.Data;

const int TrainerSubtypeMask = (int)(NpcFlags.ClassTrainer | NpcFlags.ProfessionTrainer);

SearchValues<string> classKeywords = SearchValues.Create(
[
    "Warrior", "Paladin", "Hunter", "Rogue", "Priest",
    "Shaman", "Mage", "Warlock", "Druid",
    "Deathknight", "Death Knight", "Portal"
], StringComparison.OrdinalIgnoreCase);

SearchValues<string> professionKeywords = SearchValues.Create(
[
    "Alchemy", "Alchemist",
    "Blacksmith", "Armorsmith", "Weaponsmith",
    "Cooking",
    "Enchanting",
    "Engineering", "Gnome Engineer", "Goblin Engineer",
    "First Aid",
    "Fishing",
    "Herbalism",
    "Leatherworking", "Dragonscale", "Elemental Leatherworking", "Tribal Leatherworking",
    "Mining",
    "Skinning", "Skinner",
    "Tailor", "Tailoring",
    "Jewelcraft",
    "Inscription"
], StringComparison.OrdinalIgnoreCase);

SearchValues<string> weaponKeywords = SearchValues.Create(
[
    "Axe", "Bow", "Crossbow", "Dagger", "Fist Weapon",
    "Gun", "Mace", "Polearm", "Staff", "Staves",
    "Sword", "Thrown", "Ranged Skill", "Weapon Master"
], StringComparison.OrdinalIgnoreCase);

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

// ── Phase 1: SubName-based classification ──
// Classify trainers whose SubName contains "Trainer" as ClassTrainer or ProfessionTrainer.
Console.WriteLine("=== Phase 1: SubName-based classification ===");

HashSet<int> trainerOnlyEntries = [];
int phase1Count = 0;
foreach ((string label, JArray creatures) in files)
{
    foreach (JObject creature in creatures)
    {
        int npcFlag = creature.Value<int>("NpcFlag");
        if ((npcFlag & (int)NpcFlags.Trainer) == 0)
            continue;

        string subName = creature.Value<string>("SubName") ?? "";
        if (!subName.Contains("Trainer", StringComparison.OrdinalIgnoreCase))
            continue;

        int newFlag;
        string bitLabel;

        ReadOnlySpan<char> subNameSpan = subName.AsSpan();

        if (subNameSpan.ContainsAny(classKeywords))
        {
            int correctBit = (int)NpcFlags.ClassTrainer;
            if ((npcFlag & correctBit) != 0 && (npcFlag & ~correctBit & TrainerSubtypeMask) == 0)
                continue;

            newFlag = (npcFlag & ~TrainerSubtypeMask) | correctBit;
            bitLabel = "ClassTrainer";
        }
        else if (subNameSpan.ContainsAny(professionKeywords))
        {
            int correctBit = (int)NpcFlags.ProfessionTrainer;
            if ((npcFlag & correctBit) != 0 && (npcFlag & ~correctBit & TrainerSubtypeMask) == 0)
                continue;

            newFlag = (npcFlag & ~TrainerSubtypeMask) | correctBit;
            bitLabel = "ProfessionTrainer";
        }
        else
        {
            // Weapon trainers and unmatched trainers: strip sub-type bits, keep only Trainer
            if ((npcFlag & TrainerSubtypeMask) == 0)
                continue;

            newFlag = npcFlag & ~TrainerSubtypeMask;
            bitLabel = subNameSpan.ContainsAny(weaponKeywords) ? "WeaponTrainer->Trainer" : "Unknown->Trainer";
        }

        int entry = creature.Value<int>("Entry");

        if (newFlag == npcFlag)
        {
            // Even if no change needed here, record stripped entries for Phase 2
            if ((newFlag & TrainerSubtypeMask) == 0)
                trainerOnlyEntries.Add(entry);
            continue;
        }

        // Track entries stripped to Trainer-only so Phase 2 won't re-add sub-type bits
        if ((newFlag & TrainerSubtypeMask) == 0)
            trainerOnlyEntries.Add(entry);

        string name = creature.Value<string>("Name") ?? "?";
        Console.WriteLine($"  [{label}] [{entry}] {name} \"{subName}\": {npcFlag} -> {newFlag} ({bitLabel})");

        creature["NpcFlag"] = newFlag;
        phase1Count++;
    }
}

Console.WriteLine($"Phase 1 updated: {phase1Count}");
Console.WriteLine();

// ── Phase 2: Cross-reference by Entry ID ──
Console.WriteLine("=== Phase 2a: Strip trainer-only entries across files ===");

// Strip sub-type bits from trainer-only entries that Phase 1 couldn't reach
// (e.g. same entry has a different SubName in another file)
int phase2StripCount = 0;
foreach ((string label, JArray creatures) in files)
{
    foreach (JObject creature in creatures)
    {
        int npcFlag = creature.Value<int>("NpcFlag");
        if ((npcFlag & (int)NpcFlags.Trainer) == 0)
            continue;

        int entry = creature.Value<int>("Entry");
        if (!trainerOnlyEntries.Contains(entry))
            continue;

        int subtypes = npcFlag & TrainerSubtypeMask;
        if (subtypes == 0)
            continue;

        int newFlag = npcFlag & ~TrainerSubtypeMask;
        string name = creature.Value<string>("Name") ?? "?";
        string subName = creature.Value<string>("SubName") ?? "";
        Console.WriteLine($"  [{label}] [{entry}] {name} \"{subName}\": {npcFlag} -> {newFlag} (strip sub-type)");

        creature["NpcFlag"] = newFlag;
        phase2StripCount++;
    }
}

Console.WriteLine($"Phase 2a stripped: {phase2StripCount}");
Console.WriteLine();

// Propagate sub-type flags across files for entries not stripped
Console.WriteLine("=== Phase 2b: Cross-reference by Entry ID ===");

Dictionary<int, int> entrySubtypes = [];
foreach ((string label, JArray creatures) in files)
{
    foreach (JObject creature in creatures)
    {
        int npcFlag = creature.Value<int>("NpcFlag");
        if ((npcFlag & (int)NpcFlags.Trainer) == 0)
            continue;

        int subtypes = npcFlag & TrainerSubtypeMask;
        if (subtypes == 0)
            continue;

        int entry = creature.Value<int>("Entry");
        if (entrySubtypes.TryGetValue(entry, out int existing))
            entrySubtypes[entry] = existing | subtypes;
        else
            entrySubtypes[entry] = subtypes;
    }
}

int phase2Count = ApplyTrainerFlags(files, entrySubtypes);
Console.WriteLine($"Phase 2 updated: {phase2Count}");
Console.WriteLine();

// ── Save ──
int totalUpdated = phase1Count + phase2StripCount + phase2Count;
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

int ApplyTrainerFlags(Dictionary<string, JArray> allFiles, Dictionary<int, int> lookup)
{
    int count = 0;
    foreach ((string label, JArray creatures) in allFiles)
    {
        foreach (JObject creature in creatures)
        {
            int npcFlag = creature.Value<int>("NpcFlag");
            if ((npcFlag & (int)NpcFlags.Trainer) == 0)
                continue;

            int entry = creature.Value<int>("Entry");
            if (!lookup.TryGetValue(entry, out int targetSubtypes))
                continue;

            int currentSubtypes = npcFlag & TrainerSubtypeMask;
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

