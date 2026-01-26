using SharedLib;

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

    public int LastChange => Count - LastCount;

    public BagItem(int bag, int slot, int count, Item item)
    {
        this.Bag = bag;
        this.Slot = slot;
        this.Count = count;
        this.LastCount = count;
        this.Item = item;
    }

    public void UpdateCount(int count)
    {
        LastCount = Count;
        Count = count;

        LastUpdated = DateTime.UtcNow;
    }
}