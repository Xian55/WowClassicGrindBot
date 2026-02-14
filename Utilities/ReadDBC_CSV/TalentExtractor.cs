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

        int spellRank0 = reader.Header.IndexOf("SpellRank_0", "SpellRank[0]");
        int spellRank1 = reader.Header.IndexOf("SpellRank_1", "SpellRank[1]");
        int spellRank2 = reader.Header.IndexOf("SpellRank_2", "SpellRank[2]");
        int spellRank3 = reader.Header.IndexOf("SpellRank_3", "SpellRank[3]");
        int spellRank4 = reader.Header.IndexOf("SpellRank_4", "SpellRank[4]");

        List<TalentTreeElement> talents = [];
        foreach (SepReader.Row row in reader)
        {
            //Console.WriteLine($"{values[entryIndex]} - {values[nameIndex]}");
            talents.Add(new TalentTreeElement
            {
                TierID = row[tierID].Parse<int>(),
                ColumnIndex = row[columnIndex].Parse<int>(),
                TabID = row[tabID].Parse<int>(),
                SpellIds =
                [
                    row[spellRank0].Parse<int>(),
                    row[spellRank1].Parse<int>(),
                    row[spellRank2].Parse<int>(),
                    row[spellRank3].Parse<int>(),
                    row[spellRank4].Parse<int>()
                ]
            });
        }

        return talents;
    }

}
