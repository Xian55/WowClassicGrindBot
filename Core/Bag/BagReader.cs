using Core.Database;

using SharedLib;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Core;

public enum BagItemChange
{
    New,
    Remove,
    Update
}

public sealed class BagReader : IDisposable, IReader
{
    private const int cBagMeta = 20;
    private const int cItemNumCount = 21;
    private const int cItemId = 22;

    private readonly EquipmentReader equipmentReader;

    private readonly ItemDB ItemDB;

    public List<BagItem> BagItems { get; } = new();

    public Bag[] Bags { get; } = new Bag[5];

    public event Action? DataChanged;
    public event Action<BagItem, BagItemChange>? BagItemChange;

    public int Hash { private set; get; }
    public int HashNewOrStackGain { private set; get; }

    public BagReader(ItemDB itemDb, EquipmentReader equipmentReader)
    {
        this.ItemDB = itemDb;
        this.equipmentReader = equipmentReader;

        this.equipmentReader.OnEquipmentChanged -= OnEquipmentChanged;
        this.equipmentReader.OnEquipmentChanged += OnEquipmentChanged;

        for (int i = 0; i < Bags.Length; i++)
        {
            Bags[i] = new Bag();
        }
        Bags[0].Item = ItemDB.Backpack;
    }

    public void Dispose()
    {
        this.equipmentReader.OnEquipmentChanged -= OnEquipmentChanged;
    }

    public void Update(IAddonDataProvider reader)
    {
        ReadBagMeta(reader, out bool metaChanged);

        ReadInventory(reader, out bool inventoryChanged);

        if (metaChanged || inventoryChanged)
        {
            DataChanged?.Invoke();

            Hash++;
        }
    }

    private void ReadBagMeta(IAddonDataProvider reader, out bool changed)
    {
        int data = reader.GetInt(cBagMeta);
        if (data == 0)
        {
            changed = false;
            return;
        }

        //bagType * 1000000 + bagNum * 100000 + freeSlots * 1000 + slotCount
        int bagType = data / 1000000;
        int index = data / 100000 % 10;
        int freeSlots = data / 1000 % 100;
        int slotCount = data % 1000;

        if (index >= 0 && index < Bags.Length)
        {
            Bag bag = Bags[index];

            // default bag, the first has no equipment slot
            if (index != 0)
            {
                int itemId = equipmentReader.GetId((int)InventorySlotId.Bag_0 + index - 1);
                UpdateBagItem(index, itemId);
            }

            bag.BagType = (BagType)bagType;
            bag.SlotCount = slotCount;
            bag.FreeSlot = freeSlots;

            BagItems.RemoveAll(RemoveByIndex);
            bool RemoveByIndex(BagItem b)
                => b.Bag == index && b.Slot > bag.SlotCount;

            changed = true;
        }
        else
        {
            changed = false;
        }
    }

    private void ReadInventory(IAddonDataProvider reader, out bool hasChanged)
    {
        hasChanged = false;

        int data = reader.GetInt(cItemNumCount);
        if (data == 0) return;

        // 21 -- 0-4 bagNum + 1-21 itemNum + 1-1000 quantity
        int bag = data / 1000000;
        int slot = data / 10000 % 100;
        int itemCount = data % 10000;

        // 22 -- flags * 1000000 + itemId (flags: bits 0-3)
        int itemIdData = reader.GetInt(cItemId);
        int flags = itemIdData / 1000000;
        int itemId = itemIdData % 1000000;

        BagItem? existingItem = BagItems.FirstOrDefault(Exists);
        bool Exists(BagItem b) =>
            b.Bag == bag && b.Slot == slot;

        if (itemCount > 0)
        {
            bool addItem = true;

            if (existingItem != null)
            {
                if (existingItem.Item.Entry != itemId)
                {
                    BagItemChange?.Invoke(existingItem, Core.BagItemChange.Remove);
                    BagItems.Remove(existingItem);
                    addItem = true;
                }
                else
                {
                    addItem = false;

                    bool countChanged = existingItem.Count != itemCount;
                    bool flagsChanged =
                        existingItem.IsTradable != ((flags & 1) != 0) ||
                        existingItem.IsSoulbound != ((flags & 2) != 0) ||
                        existingItem.IsLocked != ((flags & 4) != 0) ||
                        existingItem.HasNoValue != ((flags & 8) != 0);

                    if (countChanged || flagsChanged)
                    {
                        if (countChanged)
                        {
                            if (existingItem.Count < itemCount)
                                HashNewOrStackGain++;
                            existingItem.UpdateCount(itemCount);
                        }
                        if (flagsChanged)
                        {
                            existingItem.UpdateFlags(flags);
                        }
                        BagItemChange?.Invoke(existingItem, Core.BagItemChange.Update);
                        hasChanged = true;
                    }
                }
            }

            if (addItem)
            {
                hasChanged = true;

                if (ItemDB.Items.TryGetValue(itemId, out Item item))
                {
                    BagItem newItem = new(bag, slot, itemCount, item, flags);
                    BagItems.Add(newItem);
                    BagItemChange?.Invoke(newItem, Core.BagItemChange.New);
                }
                else
                {
                    BagItem unknownItem =
                        new(bag, slot, itemCount, new Item() { Entry = itemId, Name = "Unknown" }, flags);
                    BagItems.Add(unknownItem);
                    BagItemChange?.Invoke(unknownItem, Core.BagItemChange.New);
                }

                HashNewOrStackGain++;
            }
        }
        else
        {
            if (existingItem != null)
            {
                BagItemChange?.Invoke(existingItem, Core.BagItemChange.Remove);
                BagItems.Remove(existingItem);
                hasChanged = true;
            }
        }
    }

    public int BagItemCount() => BagItems.Count;

    public int SlotCount => Bags.Sum(BagSlotCount);

    public bool BagsFull() => Bags.Sum(BagFreeSlotCount) == 0;

    public bool AnyGreyItem() => BagItems.Any(BagItemCommonQuality);

    public int ItemCount(int itemId)
    {
        int count = 0;

        var span = CollectionsMarshal.AsSpan(BagItems);
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i].Item.Entry == itemId)
            {
                count += span[i].Count;
            }
        }

        return count;
    }

    public bool HasItem(int itemId)
    {
        return BagItems.Exists(ById);
        bool ById(BagItem x) => x.Item.Entry == itemId;
    }

    public int HighestQuantityOfDrinkItemId()
    {
        return ItemDB.DrinkIds.
            OrderByDescending(ItemCount).
            FirstOrDefault();
    }

    public int DrinkItemCount()
    {
        return ItemCount(HighestQuantityOfDrinkItemId());
    }

    public int HighestQuantityOfFoodItemId()
    {
        return ItemDB.FoodIds.
            OrderByDescending(ItemCount).
            FirstOrDefault();
    }
    public int FoodItemCount()
    {
        return ItemCount(HighestQuantityOfFoodItemId());
    }

    private void OnEquipmentChanged(object? s, (int, int) tuple)
    {
        if (tuple.Item1 is
            not >= ((int)InventorySlotId.Bag_0) or
            not <= ((int)InventorySlotId.Bag_3))
        {
            return;
        }
        int index = tuple.Item1 - (int)InventorySlotId.Tabard;
        UpdateBagItem(index, tuple.Item2);
    }

    private void UpdateBagItem(int index, int itemId)
    {
        if (ItemDB.Items.TryGetValue(itemId, out Item item))
            Bags[index].Item = item;
        else
            Bags[index].Item = ItemDB.EmptyItem;
    }


    #region Helpers

    private static int BagSlotCount(Bag b) => b.SlotCount;

    private static int BagFreeSlotCount(Bag b) => b.BagType == BagType.Unspecified ? b.FreeSlot : 0;

    private static bool BagItemCommonQuality(BagItem bi) => bi.Item.Quality == 0;

    public int BagMaxSlot() => Bags.Max(BagSlotCount);

    /// <summary>
    /// Checks if there are any items meeting the quality threshold for mailing.
    /// </summary>
    /// <param name="minQuality">Minimum item quality (0=grey, 1=white, 2=green, etc.)</param>
    /// <param name="excludedItemIds">Set of item IDs to exclude</param>
    /// <returns>True if mailable items exist</returns>
    public bool HasMailableItems(int minQuality, IReadOnlySet<int> excludedItemIds)
    {
        var span = CollectionsMarshal.AsSpan(BagItems);
        for (int i = 0; i < span.Length; i++)
        {
            BagItem item = span[i];
            if (item.IsTradable && item.Item.Quality >= minQuality)
            {
                if (!excludedItemIds.Contains(item.Item.Entry))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Counts items meeting the quality threshold for mailing.
    /// </summary>
    /// <param name="minQuality">Minimum item quality (0=grey, 1=white, 2=green, etc.)</param>
    /// <param name="excludedItemIds">Set of item IDs to exclude</param>
    /// <returns>Count of mailable items</returns>
    public int CountMailableItems(int minQuality, IReadOnlySet<int> excludedItemIds)
    {
        int count = 0;
        var span = CollectionsMarshal.AsSpan(BagItems);
        for (int i = 0; i < span.Length; i++)
        {
            BagItem item = span[i];
            if (item.IsTradable && item.Item.Quality >= minQuality)
            {
                if (!excludedItemIds.Contains(item.Item.Entry))
                {
                    count++;
                }
            }
        }
        return count;
    }

    #endregion
}