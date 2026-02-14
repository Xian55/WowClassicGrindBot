using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using SharedLib;
using nietras.SeparatedValues;

namespace ReadDBC_CSV;

internal sealed class SpellExtractor : IExtractor
{
    private readonly string path;

    public string[] FileRequirement { get; } =
    [
        "spellname.csv",
        "spelllevels.csv",
    ];

    public SpellExtractor(string path)
    {
        this.path = path;
    }

    public void Run()
    {
        string spellnameFile = Path.Join(path, FileRequirement[0]);
        List<Spell> spells = ExtractNames(spellnameFile);

        string spelllevelsFile = Path.Join(path, FileRequirement[1]);
        ExtractLevels(spelllevelsFile, spells);

        Console.WriteLine($"Spells: {spells.Count}");

        File.WriteAllText(Path.Join(path, "spells.json"), JsonConvert.SerializeObject(spells, Formatting.Indented));
    }

    private static List<Spell> ExtractNames(string path)
    {
        using var reader = Sep.Reader(o => o with
        {
            Unescape = true,
        }).FromFile(path);

        int id = reader.Header.IndexOf("ID");
        int name = reader.Header.IndexOf("Name_lang");

        List<Spell> spells = new();
        foreach (SepReader.Row row in reader)
        {
            spells.Add(new Spell
            {
                Id = row[id].Parse<int>(),
                Name = row[name].ToString()
            });
        }
        return spells;
    }

    private static void ExtractLevels(string path, List<Spell> spells)
    {
        using var reader = Sep.Reader(o => o with
        {
            Unescape = true,
        }).FromFile(path);

        int entry = reader.Header.IndexOf("ID");

        int spellId = reader.Header.IndexOf("SpellID", 6);
        int baseLevel = reader.Header.IndexOf("BaseLevel", 2);

        foreach (SepReader.Row row in reader)
        {
            int level = row[baseLevel].Parse<int>();
            int spell = row[spellId].Parse<int>();

            if (level <= 0 || spell <= 0)
                continue;

            bool ById(Spell x) => x.Id == spell;
            int index = spells.FindIndex(0, ById);
            if (index <= -1)
                continue;

            Spell s = spells[index];
            spells[index] = s with { Level = level };
        }
    }
}
