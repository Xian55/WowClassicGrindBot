using Core.Database;

using SharedLib;

using System;

namespace Core;

public sealed class EquipmentReader : IReader
{
    private const int MAX_EQUIPMENT_COUNT = 24;

    private readonly ItemDB itemDB;
    private readonly int cSlotNum = 23;
    private readonly int cItemId = 24;

    private readonly int[] equipmentIds = new int[MAX_EQUIPMENT_COUNT];
    public Item[] Items { get; private init; } = new Item[MAX_EQUIPMENT_COUNT];

    public event EventHandler<(int, int)>? OnEquipmentChanged;

    public EquipmentReader(ItemDB itemDB)
    {
        this.itemDB = itemDB;

        var span = Items.AsSpan();
        span.Fill(ItemDB.EmptyItem);
    }

    public void Update(IAddonDataProvider reader)
    {
        int index = reader.GetInt(cSlotNum);
        if (index >= MAX_EQUIPMENT_COUNT)
            return;

        int itemId = reader.GetInt(cItemId);
        bool changed = equipmentIds[index] != itemId;

        if (!changed)
            return;

        equipmentIds[index] = itemId;

        Items[index] = itemId == 0
            ? ItemDB.EmptyItem
            : itemDB.Items.TryGetValue(itemId, out Item item)
                ? item
                : ItemDB.EmptyItem with { Entry = itemId, Name = "Unknown" };

        OnEquipmentChanged?.Invoke(this, (index, itemId));
    }

    public string ToStringList()
    {
        return string.Join(", ", equipmentIds);
    }

    public bool RangedWeapon()
    {
        return equipmentIds[(int)InventorySlotId.Ranged] != 0;
    }

    public bool HasItem(int itemId)
    {
        for (int i = 0; i < equipmentIds.Length; i++)
        {
            if (equipmentIds[i] == itemId)
                return true;
        }

        return false;
    }

    public int GetId(int slot)
    {
        return equipmentIds[slot];
    }
}