using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.IO;

using nietras.SeparatedValues;

namespace ReadDBC_CSV;

// Full AreaTable dump: areaId -> name + parent + continent. WorldMapArea only
// carries map-level zones, but ADT terrain often reports a fine SUBZONE areaId
// with no WorldMapArea row. This lets the Leaflet map name the subzone and walk
// ParentAreaId up to its parent zone.
internal sealed record AreaTableEntry
{
    public int AreaID { get; init; }
    public int MapID { get; init; }
    public int ParentAreaId { get; init; }
    public string AreaName { get; init; } = "";
}

internal sealed class AreaTableExtractor : IExtractor
{
    private readonly string path;

    public string[] FileRequirement { get; } =
    [
        "areatable.csv"
    ];

    public AreaTableExtractor(string path)
    {
        this.path = path;
    }

    public void Run()
    {
        string file = Path.Join(path, FileRequirement[0]);

        using var reader = Sep.Reader(o => o with
        {
            Unescape = true,
        }).FromFile(file);

        int idIndex = reader.Header.IndexOf("ID");
        int continentIndex = reader.Header.IndexOf("ContinentID");
        int parentIndex = reader.Header.IndexOf("ParentAreaID");
        int nameIndex = reader.Header.IndexOf("AreaName_lang");

        List<AreaTableEntry> areas = new();
        foreach (SepReader.Row row in reader)
        {
            int id = row[idIndex].Parse<int>();
            if (id == 0)
                continue;

            areas.Add(new AreaTableEntry
            {
                AreaID = id,
                MapID = row[continentIndex].Parse<int>(),
                ParentAreaId = row[parentIndex].Parse<int>(),
                AreaName = row[nameIndex].ToString(),
            });
        }

        Console.WriteLine($"AreaTable: {areas.Count}");
        File.WriteAllText(Path.Join(path, "AreaTable.json"),
            JsonConvert.SerializeObject(areas, Formatting.Indented));
    }
}
