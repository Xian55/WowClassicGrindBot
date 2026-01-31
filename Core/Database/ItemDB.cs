using SharedLib;

using System.Collections.Frozen;
using System.Collections.Generic;

using static Newtonsoft.Json.JsonConvert;
using static System.IO.File;
using static System.IO.Path;

namespace Core.Database;

public sealed class ItemDB
{
    private static readonly Item _emptyItem = new() { Entry = 0, Name = string.Empty, Quality = 0, SellPrice = 0 };
    public static ref readonly Item EmptyItem => ref _emptyItem;

    public static readonly Item Backpack = new() { Entry = -1, Name = "Backpack", Quality = 1, SellPrice = 0 };

    private readonly IconDB iconDB;

    public FrozenDictionary<int, Item> Items { get; }
    public int[] FoodIds { get; }
    public int[] DrinkIds { get; }

    // Known conjured item IDs (cannot be mailed)
    public static readonly FrozenSet<int> ConjuredItemIds = FrozenSet.ToFrozenSet([
        // Mage Conjured Water
        5350, 2288, 2136, 3772, 8077, 8078, 8079,
        // Mage Conjured Food
        5349, 1113, 1114, 1487, 8075, 8076, 22895,
        // Warlock Healthstones (all ranks + improved)
        5512, 19004, 19005, 5511, 19006, 19007,
        5509, 19008, 19009, 5510, 19010, 19011,
        9421, 19012, 19013,
        // TBC conjured items
        22044, 30703, 34062,
        // Warlock Soulstones
        16892, 16893, 16895, 16896,
    ]);


    public ItemDB(DataConfig dataConfig, IconDB iconDB)
    {
        this.iconDB = iconDB;

        List<Item> items = DeserializeObject<List<Item>>(
            ReadAllText(Join(dataConfig.ExpDbc, "items.json")))!;

        items.Add(Backpack);

        this.Items = items.ToFrozenDictionary(item => item.Entry);

        FoodIds = DeserializeObject<int[]>(
            ReadAllText(Join(dataConfig.ExpDbc, "foods.json")))!;

        DrinkIds = DeserializeObject<int[]>(
            ReadAllText(Join(dataConfig.ExpDbc, "waters.json")))!;

        // Set static reference for KeyReader item alias resolution
        KeyReader.ItemDB = this;
    }

    /// <summary>
    /// Gets texture IDs for all known food items.
    /// </summary>
    public IEnumerable<int> GetFoodTextures()
    {
        foreach (int itemId in FoodIds)
        {
            if (Items.TryGetValue(itemId, out Item item) && item.TextureId > 0)
                yield return item.TextureId;
        }
    }

    /// <summary>
    /// Gets texture IDs for all known drink items.
    /// </summary>
    public IEnumerable<int> GetDrinkTextures()
    {
        foreach (int itemId in DrinkIds)
        {
            if (Items.TryGetValue(itemId, out Item item) && item.TextureId > 0)
                yield return item.TextureId;
        }
    }

    /// <summary>
    /// Gets the texture ID for an item by its item ID.
    /// </summary>
    public bool TryGetTexture(int itemId, out int textureId)
    {
        if (Items.TryGetValue(itemId, out Item item) && item.TextureId > 0)
        {
            textureId = item.TextureId;
            return true;
        }
        textureId = 0;
        return false;
    }

    /// <summary>
    /// Gets the icon name for an item (e.g., "inv_misc_bag_08").
    /// Returns null if the item or icon is not found.
    /// </summary>
    public string? GetItemIconName(int itemId)
    {
        if (Items.TryGetValue(itemId, out Item item) && item.TextureId > 0)
        {
            if (iconDB.TryGetIconName(item.TextureId, out string? iconName))
                return iconName;
        }
        return null;
    }

    /// <summary>
    /// Gets the icon URL for an item.
    /// Size: 18, 36, or 56 pixels.
    /// Returns null if the item or icon is not found.
    /// </summary>
    public string? GetItemIconUrl(int itemId, int size = 56)
    {
        if (Items.TryGetValue(itemId, out Item item) && item.TextureId > 0)
        {
            return iconDB.GetIconUrl(item.TextureId, size);
        }
        return null;
    }
}
