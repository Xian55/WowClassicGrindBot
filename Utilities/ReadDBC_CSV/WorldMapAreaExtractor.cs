using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using SharedLib;
using nietras.SeparatedValues;
using System.Linq;

namespace ReadDBC_CSV;

internal sealed class WorldMapAreaExtractor : IExtractor
{
    private readonly string path;

    public string[] FileRequirement { get; } =
    [
        "uimap.csv",
        "uimapassignment.csv",
        "map.csv",
        "areatable.csv"
    ];

    public WorldMapAreaExtractor(string path)
    {
        this.path = path;
    }

    public void Run()
    {
        // UIMapId - AreaName
        string uimapFile = Path.Join(path, FileRequirement[0]);
        List<WorldMapArea> wmas = ExtractUIMap(uimapFile);

        // MapID - AreaID - LocBottom - LocRight - LocTop - LocLeft
        string uimapassignmentFile = Path.Join(path, FileRequirement[1]);
        ExtractBoundaries(uimapassignmentFile, wmas);

        // Extend Subzones
        ExtendWithSubZones(path, wmas);

        // ParentAreaId - ExplorationLevel
        string areaTableFile = Path.Join(path, FileRequirement[3]);
        ExtractParentAndLevel(areaTableFile, wmas);

        // Continent / Directory
        string mapFile = Path.Join(path, FileRequirement[2]);
        ExtractContinent(mapFile, wmas);

        ClearEmptyBound(wmas);

        Console.WriteLine($"WMAs: {wmas.Count}");
        File.WriteAllText(Path.Join(path, "WorldMapArea.json"), JsonConvert.SerializeObject(wmas, Formatting.Indented));
    }

    private static List<WorldMapArea> ExtractUIMap(string path)
    {
        using var reader = Sep.Reader(o => o with
        {
            Unescape = true,
        }).FromFile(path);

        int idIndex = reader.Header.IndexOf("ID");
        int nameIndex = reader.Header.IndexOf("Name_lang");
        int systemIndex = reader.Header.IndexOf("System");

        List<WorldMapArea> items = new();
        foreach (SepReader.Row row in reader)
        {
            int uiMapId = row[idIndex].Parse<int>();
            int system = row[systemIndex].Parse<int>();

            // 1 ([DEPRECATED] Legacy Taxi)
            // 2 (Taxi and Adventure)
            if (system == 1 || system == 2)
            {
                continue;
            }

            items.Add(new WorldMapArea
            {
                UIMapId = uiMapId,
                AreaName = row[nameIndex].ToString(),
            });
        }

        return items;
    }

    private static void ExtractBoundaries(string path, List<WorldMapArea> wmas)
    {
        using var reader = Sep.Reader(o => o with
        {
            Unescape = true,
        }).FromFile(path);

        int uiMapId = reader.Header.IndexOf("UiMapID");
        int mapId = reader.Header.IndexOf("MapID");
        int areaId = reader.Header.IndexOf("AreaID");

        int orderIndex = reader.Header.IndexOf("OrderIndex");

        int region0 = reader.Header.IndexOf("Region_0", "Region[0]");
        int region1 = reader.Header.IndexOf("Region_1", "Region[1]");

        int region3 = reader.Header.IndexOf("Region_3", "Region[3]");
        int region4 = reader.Header.IndexOf("Region_4", "Region[4]");

        foreach (SepReader.Row row in reader)
        {
            int _uiMapId = row[uiMapId].Parse<int>();
            int _orderIndex = row[orderIndex].Parse<int>();

            int index = wmas.FindIndex(x => x.UIMapId == _uiMapId && _orderIndex == 0);
            if (index > -1)
            {
                WorldMapArea wma = wmas[index];
                wmas[index] = wma with
                {
                    MapID = row[mapId].Parse<int>(),
                    AreaID = row[areaId].Parse<int>(),

                    LocBottom = row[region0].Parse<float>(),
                    LocRight = row[region1].Parse<float>(),

                    LocTop = row[region3].Parse<float>(),
                    LocLeft = row[region4].Parse<float>(),
                };
            }
        }
    }

    private static void ExtractContinent(string path, List<WorldMapArea> wmas)
    {
        using var reader = Sep.Reader(o => o with
        {
            Unescape = true,
        }).FromFile(path);

        int mapId = reader.Header.IndexOf("ID");
        int directory = reader.Header.IndexOf("Directory", 1);

        foreach (SepReader.Row row in reader)
        {
            int _mapId = row[mapId].Parse<int>();
            string _directory = row[directory].ToString();

            for (int i = 0; i < wmas.Count; i++)
            {
                if (wmas[i].MapID != _mapId)
                    continue;

                WorldMapArea wma = wmas[i];
                wmas[i] = wma with
                {
                    Continent = _directory
                };
            }
        }
    }

    private static void ExtractParentAndLevel(string path, List<WorldMapArea> wmas)
    {
        using var reader = Sep.Reader(o => o with
        {
            Unescape = true,
        }).FromFile(path);

        int areaId = reader.Header.IndexOf("ID");
        int zoneName = reader.Header.IndexOf("ZoneName");
        int areaNameLang = reader.Header.IndexOf("AreaName_lang");
        int continentID = reader.Header.IndexOf("ContinentID");
        int parentAreaId = reader.Header.IndexOf("ParentAreaID");
        int explorationLevel = reader.Header.IndexOf("ExplorationLevel");

        foreach (SepReader.Row row in reader)
        {
            int _areaId = row[areaId].Parse<int>();
            string _areaNameLang = row[areaNameLang].ToString();
            string _zoneName = row[zoneName].ToString();
            int _parentAreaId = row[parentAreaId].Parse<int>();
            int _contientID = row[continentID].Parse<int>();
            int _explorationLevel = row[explorationLevel].Parse<int>();

            if (_zoneName.Contains("NightmareGrove"))
                continue;

            int i = wmas.FindIndex(x => x.AreaID == _areaId);
            if (i == -1)
            {
                //Console.WriteLine($"[{_areaId}] not found {_areaNameLang} {_zoneName}");
                continue;
            }

            WorldMapArea wma = wmas[i];
            var newWma = wma with
            {
                MapID = _contientID,
                ParentAreaId = _parentAreaId,
                ExplorationLevel = _explorationLevel,
                AreaName = _areaNameLang,
            };

            wmas[i] = newWma;
        }
    }

    private static void ExtendWithSubZones(string rootPath, List<WorldMapArea> wmas)
    {
        var filesWithNumberOnlyInName = Directory.GetFiles(rootPath, "*.json", SearchOption.AllDirectories)
            .Where(x => Path.GetFileNameWithoutExtension(x).All(char.IsDigit));

        foreach (string file in filesWithNumberOnlyInName)
        {
            ReadOnlySpan<SubZoneArea> subzones = JsonConvert.DeserializeObject<SubZoneArea[]>(File.ReadAllText(file));

            foreach (SubZoneArea sub in subzones)
            {
                (float left, float right, float top, float bottom) = ConvertCoord(sub);

                // skip if already exists
                if (wmas.Exists(x => x.AreaID == sub.Id))
                {
                    var existing = wmas.Find(x => x.AreaID == sub.Id);

                    WorldMapArea clickWMA = existing with
                    {
                        UIMapId = 0,
                        AreaID = sub.Id + 1000000,
                        AreaName = "C_" + existing.AreaName,
                        LocLeft = left,
                        LocRight = right,
                        LocTop = top,
                        LocBottom = bottom,
                    };
                    wmas.Add(clickWMA);

                    continue;
                }

                WorldMapArea wma = new()
                {
                    AreaID = sub.Id,
                    LocLeft = left,
                    LocRight = right,
                    LocTop = top,
                    LocBottom = bottom,
                };

                wmas.Add(wma);
            }
        }
    }

    private static (float left, float right, float top, float bottom) ConvertCoord(SubZoneArea sub)
    {
        return (
            sub.Min.Y, // left
            sub.Max.Y, // right
            sub.Min.X, // top
            sub.Max.X);// bottom   
    }


    private static void ClearEmptyBound(List<WorldMapArea> wmas)
    {
        for (int i = wmas.Count - 1; i >= 0; i--)
        {
            WorldMapArea wma = wmas[i];

            if ((wma.LocBottom == 0 &&
                wma.LocLeft == 0 &&
                wma.LocRight == 0 &&
                wma.LocTop == 0) ||
                wma.AreaName == null ||
                wma.AreaName.Contains("UNUSED") ||
                wma.AreaName.Contains("DELETE ME"))
            {
                Console.WriteLine($"{wma}");
                wmas.RemoveAt(i);
            }
        }
    }
}
