using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using SharedLib;
using nietras.SeparatedValues;

namespace ReadDBC_CSV;

internal sealed class TalentExtractor : IExtractor
{
    private readonly string path;

    public string[] FileRequirement { get; } =
    [
        "talenttab.csv",
        "talent.csv"
    ];

    public TalentExtractor(string path)
    {
        this.path = path;
    }

    public void Run()
    {
        string talenttabFile = Path.Join(path, FileRequirement[0]);
        List<TalentTab> talenttabs = ExtractTalentTabs(talenttabFile);
        Console.WriteLine($"TalentTabs: {talenttabs.Count}");
        File.WriteAllText(Path.Join(path, "talenttab.json"), JsonConvert.SerializeObject(talenttabs, Formatting.Indented));

        string talentFile = Path.Join(path, FileRequirement[1]);
        List<TalentTreeElement> talents = ExtractTalentTrees(talentFile);
        Console.WriteLine($"Talents: {talents.Count}");
        File.WriteAllText(Path.Join(path, "talent.json"), JsonConvert.SerializeObject(talents, Formatting.Indented));
    }

    private static List<TalentTab> ExtractTalentTabs(string path)
    {
        using var reader = Sep.Reader(o => o with
        {
            Unescape = true,
        }).FromFile(path);

        int id = reader.Header.IndexOf("ID");
        int orderIndex = reader.Header.IndexOf("OrderIndex");
        int classMask = reader.Header.IndexOf("ClassMask");

        List<TalentTab> talenttabs = new();
        foreach (SepReader.Row row in reader)
        {
            talenttabs.Add(new TalentTab
            {
                Id = row[id].Parse<int>(),
                OrderIndex = row[orderIndex].Parse<int>(),
                ClassMask = row[classMask].Parse<int>()
            });
        }

        return talenttabs;
    }

    // 5.0 replaced the ranked per-tab trees with six tiers of a single pick each.
    // Those rows identify their class on ClassID and carry the spell on SpellID,
    // leaving SpellRank_* at zero - reading only the ranks dropped the spell of
    // every 5.x talent. Pre-5.0 rows are the mirror image: ranks are filled,
    // ClassID and SpellID are zero.
    private const int MOP_TALENT_COLUMNS = 3;

    // Column 4 of the 5.x grid is filler pointing at spell 102052 "Dummy 5.0
    // Talent" for warrior, paladin and mage. It is not a talent and must not
    // reach the UI.
    private const int DUMMY_TALENT_SPELL_ID = 102052;

    public static List<TalentTreeElement> ExtractTalentTrees(string path)
    {
        using var reader = Sep.Reader(o => o with
        {
            Unescape = true,
        }).FromFile(path);

        int id = reader.Header.IndexOf("ID");

        int tierID = reader.Header.IndexOf("TierID");
        int columnIndex = reader.Header.IndexOf("ColumnIndex");
        int tabID = reader.Header.IndexOf("TabID");

        // Absent from old enough exports, so both are optional.
        bool hasClassID = reader.Header.TryIndexOf("ClassID", out int classID);
        bool hasSpellID = reader.Header.TryIndexOf("SpellID", out int spellID);

        int spellRank0 = reader.Header.IndexOf("SpellRank_0", "SpellRank[0]");
        int spellRank1 = reader.Header.IndexOf("SpellRank_1", "SpellRank[1]");
        int spellRank2 = reader.Header.IndexOf("SpellRank_2", "SpellRank[2]");
        int spellRank3 = reader.Header.IndexOf("SpellRank_3", "SpellRank[3]");
        int spellRank4 = reader.Header.IndexOf("SpellRank_4", "SpellRank[4]");

        List<TalentTreeElement> talents = [];
        foreach (SepReader.Row row in reader)
        {
            //Console.WriteLine($"{values[entryIndex]} - {values[nameIndex]}");
            int rowClassID = hasClassID ? row[classID].Parse<int>() : 0;
            int rowColumnIndex = row[columnIndex].Parse<int>();
            int rowSpellID = hasSpellID ? row[spellID].Parse<int>() : 0;

            if (rowClassID != 0 &&
                (rowColumnIndex >= MOP_TALENT_COLUMNS || rowSpellID == DUMMY_TALENT_SPELL_ID))
                continue;

            int[] spellIds =
            [
                row[spellRank0].Parse<int>(),
                row[spellRank1].Parse<int>(),
                row[spellRank2].Parse<int>(),
                row[spellRank3].Parse<int>(),
                row[spellRank4].Parse<int>()
            ];

            if (spellIds[0] == 0 && rowSpellID != 0)
                spellIds[0] = rowSpellID;

            talents.Add(new TalentTreeElement
            {
                TierID = row[tierID].Parse<int>(),
                ColumnIndex = rowColumnIndex,
                TabID = row[tabID].Parse<int>(),
                ClassID = rowClassID,
                SpellIds = spellIds
            });
        }

        return talents;
    }

}
