using SharedLib;

using Core.Database;

using System;

namespace Core;

public sealed class BagItem
{
    public int Bag { get; }
    public int Slot { get; }
    public int Count { get; private set; }
    public int LastCount { get; private set; }
    public Item Item { get; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    // Item flags from addon
    public bool IsTradable { get; private set; }
    public bool IsSoulbound { get; private set; }
    public bool IsLocked { get; private set; }
    public bool HasNoValue { get; private set; }

    // Computed properties
    public bool IsConjured => ItemDB.ConjuredItemIds.Contains(Item.Entry);
    public bool IsMailable => IsTradable && !IsConjured;

    public int LastChange => Count - LastCount;

    public BagItem(int bag, int slot, int count, Item item, int flags = 1)
    {
        this.Bag = bag;
        this.Slot = slot;
        this.Count = count;
        this.LastCount = count;
        this.Item = item;
        SetFlags(flags);
    }

    public void UpdateCount(int count)
    {
        LastCount = Count;
        Count = count;
        LastUpdated = DateTime.UtcNow;
    }

    public void UpdateFlags(int flags)
    {
        SetFlags(flags);
        LastUpdated = DateTime.UtcNow;
    }

    private void SetFlags(int flags)
    {
        IsTradable = (flags & 1) != 0;
        IsSoulbound = (flags & 2) != 0;
        IsLocked = (flags & 4) != 0;
        HasNoValue = (flags & 8) != 0;
    }
}