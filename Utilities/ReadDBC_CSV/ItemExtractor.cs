using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using SharedLib;
using nietras.SeparatedValues;

namespace ReadDBC_CSV;

internal sealed class ItemExtractor : IExtractor
{
    private readonly string path;

    public string[] FileRequirement { get; } =
    [
        "itemsparse.csv",
        "item.csv"
    ];

    public ItemExtractor(string path)
    {
        this.path = path;
    }

    public void Run()
    {
        // First load icon mappings from item.csv
        string itemFile = Path.Join(path, FileRequirement[1]);
        Dictionary<int, int> iconMap = ExtractIconMap(itemFile);
        Console.WriteLine($"Item icons: {iconMap.Count}");

        // Then load items and join with icons
        string itemSparseFile = Path.Join(path, FileRequirement[0]);
        List<Item> items = ExtractItems(itemSparseFile, iconMap);

        Console.WriteLine($"Items: {items.Count}");
        File.WriteAllText(Path.Join(path, "items.json"), JsonConvert.SerializeObject(items));
    }

    /// <summary>
    /// Extracts item ID to icon texture ID mapping from item.csv
    /// </summary>
    private static Dictionary<int, int> ExtractIconMap(string path)
    {
        using var reader = Sep.Reader().FromFile(path);

        int id = reader.Header.IndexOf("ID");
        int iconFileDataId = reader.Header.IndexOf("IconFileDataID");

        Dictionary<int, int> iconMap = [];
        foreach (SepReader.Row row in reader)
        {
            int itemId = row[id].Parse<int>();
            int textureId = row[iconFileDataId].Parse<int>();
            if (textureId > 0)
            {
                iconMap[itemId] = textureId;
            }
        }
        return iconMap;
    }

    private static List<Item> ExtractItems(string path, Dictionary<int, int> iconMap)
    {
        using var reader = Sep.Reader(o => o with
        {
            Unescape = true,
        }).FromFile(path);

        int id = reader.Header.IndexOf("ID");
        int name = reader.Header.IndexOf("Display_lang");
        int quality = reader.Header.IndexOf("OverallQualityID");
        int sellPrice = reader.Header.IndexOf("SellPrice");

        List<Item> items = [];
        foreach (SepReader.Row row in reader)
        {
            int itemId = row[id].Parse<int>();
            iconMap.TryGetValue(itemId, out int textureId);

            items.Add(new Item
            {
                Entry = itemId,
                Quality = row[quality].Parse<int>(),
                Name = row[name].ToString(),
                SellPrice = row[sellPrice].Parse<int>(),
                TextureId = textureId
            });
        }
        return items;
    }
}
