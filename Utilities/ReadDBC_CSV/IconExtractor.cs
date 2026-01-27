using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using nietras.SeparatedValues;

namespace ReadDBC_CSV;

internal sealed class IconExtractor : IExtractor
{
    private readonly string path;

    public string[] FileRequirement { get; } =
    [
        "spellmisc.csv",
        "spellname.csv",
        "manifestinterfacedata.csv",
    ];

    public IconExtractor(string path)
    {
        this.path = path;
    }

    public void Run()
    {
        string spellMiscFile = Path.Join(path, FileRequirement[0]);
        string spellNameFile = Path.Join(path, FileRequirement[1]);
        string manifestFile = Path.Join(path, FileRequirement[2]);

        // Step 1: Read SpellID -> SpellIconFileDataID from spellmisc.csv
        Dictionary<int, int> spellToIcon = ExtractSpellToIcon(spellMiscFile);
        Console.WriteLine($"SpellMisc entries: {spellToIcon.Count}");

        // Step 2: Read SpellID -> Name from spellname.csv
        Dictionary<int, string> spellNames = ExtractSpellNames(spellNameFile);
        Console.WriteLine($"SpellName entries: {spellNames.Count}");

        // Step 3: Read FileDataID -> IconName from manifestinterfacedata.csv
        Dictionary<int, string> iconNames = ExtractIconNames(manifestFile);
        Console.WriteLine($"Icon names extracted: {iconNames.Count}");

        // Step 4: Build TextureID -> List<SpellId> mapping
        Dictionary<int, List<int>> iconToSpells = [];
        foreach (var (spellId, iconId) in spellToIcon)
        {
            if (iconId <= 0)
                continue;

            if (!iconToSpells.TryGetValue(iconId, out var spellList))
            {
                spellList = [];
                iconToSpells[iconId] = spellList;
            }
            spellList.Add(spellId);
        }

        Console.WriteLine($"Unique texture IDs: {iconToSpells.Count}");

        // Step 5: Sort spell lists and output
        foreach (var list in iconToSpells.Values)
        {
            list.Sort();
        }

        // Output spelliconmap.json: textureId -> [spellIds]
        var spellMapOutput = iconToSpells
            .OrderBy(x => x.Key)
            .ToDictionary(x => x.Key.ToString(), x => x.Value.ToArray());

        string spellMapPath = Path.Join(path, "spelliconmap.json");
        File.WriteAllText(spellMapPath, JsonConvert.SerializeObject(spellMapOutput, Formatting.Indented));
        Console.WriteLine($"Wrote {spellMapPath}");

        // Output iconnames.json: textureId -> iconName (ALL icons from Interface\Icons\)
        var allIconNames = iconNames
            .OrderBy(x => x.Key)
            .ToDictionary(x => x.Key.ToString(), x => x.Value);

        string iconNamesPath = Path.Join(path, "iconnames.json");
        File.WriteAllText(iconNamesPath, JsonConvert.SerializeObject(allIconNames, Formatting.Indented));
        Console.WriteLine($"Wrote {iconNamesPath} ({allIconNames.Count} icons)");
    }

    private static Dictionary<int, int> ExtractSpellToIcon(string path)
    {
        using var reader = Sep.Reader(o => o with
        {
            Unescape = true,
        }).FromFile(path);

        int spellIdCol = reader.Header.IndexOf("SpellID");
        int iconCol = reader.Header.IndexOf("SpellIconFileDataID");

        Dictionary<int, int> result = [];
        foreach (SepReader.Row row in reader)
        {
            int spellId = row[spellIdCol].Parse<int>();
            int iconId = row[iconCol].Parse<int>();

            if (spellId > 0 && iconId > 0)
            {
                result[spellId] = iconId;
            }
        }
        return result;
    }

    private static Dictionary<int, string> ExtractSpellNames(string path)
    {
        using var reader = Sep.Reader(o => o with
        {
            Unescape = true,
        }).FromFile(path);

        int idCol = reader.Header.IndexOf("ID");
        int nameCol = reader.Header.IndexOf("Name_lang");

        Dictionary<int, string> result = [];
        foreach (SepReader.Row row in reader)
        {
            int id = row[idCol].Parse<int>();
            string name = row[nameCol].ToString();

            if (id > 0 && !string.IsNullOrEmpty(name))
            {
                result[id] = name;
            }
        }
        return result;
    }

    /// <summary>
    /// Extracts FileDataID -> icon name from manifestinterfacedata.csv
    /// Only extracts icons from Interface\Icons\ path
    /// </summary>
    private static Dictionary<int, string> ExtractIconNames(string path)
    {
        using var reader = Sep.Reader(o => o with
        {
            Unescape = true,
        }).FromFile(path);

        int idCol = reader.Header.IndexOf("ID");
        int filePathCol = reader.Header.IndexOf("FilePath");
        int fileNameCol = reader.Header.IndexOf("FileName");

        Dictionary<int, string> result = [];
        foreach (SepReader.Row row in reader)
        {
            string filePath = row[filePathCol].ToString();

            // Only process icons from Interface\Icons\
            if (!filePath.Contains("Icons", StringComparison.OrdinalIgnoreCase))
                continue;

            int id = row[idCol].Parse<int>();
            string fileName = row[fileNameCol].ToString();

            if (id <= 0 || string.IsNullOrEmpty(fileName))
                continue;

            // Remove .blp extension to get icon name
            // e.g., "ability_ambush.blp" -> "ability_ambush"
            string iconName = Path.GetFileNameWithoutExtension(fileName);

            if (!string.IsNullOrEmpty(iconName))
            {
                result[id] = iconName.ToLowerInvariant();
            }
        }
        return result;
    }
}
