using Newtonsoft.Json;
using nietras.SeparatedValues;

using System;
using System.Collections.Generic;
using System.IO;

namespace ReadDBC_CSV;

internal sealed class ConsumablesExtractor : IExtractor
{
    private readonly string path;

    private readonly string foodDesc;
    private readonly string waterDesc;

    public string[] FileRequirement { get; } =
    [
        "spell.csv",
        "itemeffect.csv",
    ];

    public ConsumablesExtractor(string path, string foodDesc, string waterDesc)
    {
        this.path = path;

        this.foodDesc = foodDesc;
        this.waterDesc = waterDesc;
    }

    public void Run()
    {
        string spellFile = Path.Join(path, FileRequirement[0]);

        List<int> foodSpells = ExtractSpells(spellFile, foodDesc);
        List<int> waterSpells = ExtractSpells(spellFile, waterDesc);

        string itemEffectFile = Path.Join(path, FileRequirement[1]);

        List<int> foodIds = ExtractItem(itemEffectFile, foodSpells);
        foodIds.Sort();
        Console.WriteLine($"Foods: {foodIds.Count}");
        File.WriteAllText(Path.Join(path, "foods.json"), JsonConvert.SerializeObject(foodIds, Formatting.Indented));

        List<int> waterIds = ExtractItem(itemEffectFile, waterSpells);
        waterIds.Sort();
        Console.WriteLine($"Waters: {waterIds.Count}");
        File.WriteAllText(Path.Join(path, "waters.json"), JsonConvert.SerializeObject(waterIds, Formatting.Indented));
    }

    private static List<int> ExtractSpells(string path, string descLang)
    {
        using var reader = Sep.Reader(o => o with
        {
            Unescape = true,
        }).FromFile(path);

        int id = reader.Header.IndexOf("ID");
        int desc = reader.Header.IndexOf("Description_lang");

        List<int> items = new();
        foreach (SepReader.Row row in reader)
        {
            if (row[desc].Span.IndexOf(descLang.AsSpan()) > -1)
            {
                items.Add(row[id].Parse<int>());
            }
        }

        return items;
    }

    private static List<int> ExtractItem(string path, List<int> spells)
    {
        using var reader = Sep.Reader(o => o with
        {
            Unescape = true,
        }).FromFile(path);

        int spellId = reader.Header.IndexOf("SpellID", 7);
        int parentItemID = reader.Header.IndexOf("ParentItemID", 9);

        List<int> items = [];
        foreach (SepReader.Row row in reader)
        {
            int spell = row[spellId].Parse<int>();
            if (spells.Contains(spell))
            {
                items.Add(row[parentItemID].Parse<int>());
            }
        }
        return items;
    }
}
