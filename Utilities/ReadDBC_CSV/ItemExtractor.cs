using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using SharedLib;
using SharedLib.Data;
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
        // First load item lookup from item.csv (icon, class, subclass)
        string itemFile = Path.Join(path, FileRequirement[1]);
        Dictionary<int, (int TextureId, int ClassId, int SubclassId)> itemLookup = ExtractItemLookup(itemFile);
        Console.WriteLine($"Item lookup: {itemLookup.Count}");

        // Then load items and join with lookup data
        string itemSparseFile = Path.Join(path, FileRequirement[0]);
        List<Item> items = ExtractItems(itemSparseFile, itemLookup);

        Console.WriteLine($"Items: {items.Count}");
        File.WriteAllText(Path.Join(path, "items.json"), JsonConvert.SerializeObject(items, Formatting.Indented));
    }

    /// <summary>
    /// Extracts item ID to (TextureId, ClassId, SubclassId) mapping from item.csv
    /// </summary>
    private static Dictionary<int, (int TextureId, int ClassId, int SubclassId)> ExtractItemLookup(string path)
    {
        using var reader = Sep.Reader().FromFile(path);

        int id = reader.Header.IndexOf("ID");
        int iconFileDataId = reader.Header.IndexOf("IconFileDataID");
        int classId = reader.Header.IndexOf("ClassID");
        int subclassId = reader.Header.IndexOf("SubclassID");

        Dictionary<int, (int TextureId, int ClassId, int SubclassId)> itemLookup = [];
        foreach (SepReader.Row row in reader)
        {
            int itemId = row[id].Parse<int>();
            int textureId = row[iconFileDataId].Parse<int>();
            if (textureId > 0)
            {
                itemLookup[itemId] = (textureId, row[classId].Parse<int>(), row[subclassId].Parse<int>());
            }
        }
        return itemLookup;
    }

    private static List<Item> ExtractItems(string path, Dictionary<int, (int TextureId, int ClassId, int SubclassId)> itemLookup)
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
            itemLookup.TryGetValue(itemId, out (int TextureId, int ClassId, int SubclassId) lookup);

            items.Add(new Item
            {
                Entry = itemId,
                Quality = row[quality].Parse<int>(),
                Name = row[name].ToString(),
                SellPrice = row[sellPrice].Parse<int>(),
                TextureId = lookup.TextureId,
                ClassId = (ItemClass)lookup.ClassId,
                SubclassId = lookup.SubclassId
            });
        }
        return items;
    }
}
