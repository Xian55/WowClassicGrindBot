using nietras.SeparatedValues;

using SharedLib.Data;

using System;
using System.Collections.Generic;
using System.IO;

namespace ReadDBC_CSV;
internal sealed class FactionTemplateExtractor : IExtractor
{
    private readonly string path;

    public string[] FileRequirement { get; } =
    [
        "factiontemplate.csv",
    ];

    public FactionTemplateExtractor(string path)
    {
        this.path = path;
    }

    public void Run()
    {
        string factionTemplateFile = Path.Join(path, FileRequirement[0]);
        List<FactionTemplate> factionTemplates = ExtractFactionTemplates(factionTemplateFile);

        Console.WriteLine($"Faction Templates: {factionTemplates.Count}");

        File.WriteAllText(Path.Join(path, "factiontemplates.json"), System.Text.Json.JsonSerializer.Serialize(factionTemplates));
    }

    private static List<FactionTemplate> ExtractFactionTemplates(string path)
    {
        using var reader = Sep.Reader(o => o with
        {
            Unescape = true,
        }).FromFile(path);

        int id = reader.Header.IndexOf("ID");
        int friendGroup = reader.Header.IndexOf("FriendGroup");

        List<FactionTemplate> factionTemplates = [];
        foreach (SepReader.Row row in reader)
        {
            factionTemplates.Add(new FactionTemplate
            {
                Id = row[id].Parse<int>(),
                FriendGroup = row[friendGroup].Parse<int>(),
            });
        }
        return factionTemplates;
    }
}
