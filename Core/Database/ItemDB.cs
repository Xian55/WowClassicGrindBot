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

    public FrozenDictionary<int, Item> Items { get; }
    public int[] FoodIds { get; }
    public int[] DrinkIds { get; }

    public ItemDB(DataConfig dataConfig)
    {
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
}
