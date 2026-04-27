using System;

using static Core.ActionBar;

namespace Core;

public sealed class ActionBarCastTimeReader : IReader
{
    private const int cActionbarNum = 112;

    // Cast-time portion is < 50000; the +50000 offset added by the addon
    // marks the slot as item-like (true items, macros wrapping items, or
    // auto-repeat actions like Shoot / Auto Shot). Lets KeyAction.Item be
    // derived without a manual JSON setting.
    private const int ITEM_FLAG_OFFSET = 50000;

    private readonly int[] castTimeData;
    private readonly bool[] isItemData;

    public ActionBarCastTimeReader()
    {
        int n = CELL_COUNT * BIT_PER_CELL;
        castTimeData = new int[n];
        isItemData = new bool[n];
    }

    public void Update(IAddonDataProvider reader)
    {
        // Guard against the bot's Data array being shorter than our cell —
        // happens when frame_config.json is stale relative to the addon
        // (e.g. addon bumped frame count but the bot still uses a cached
        // smaller layout). Without this the indexer at line 29 throws
        // IndexOutOfRangeException and crashes the addon thread.
        if ((uint)cActionbarNum >= (uint)reader.Data.Length)
            return;

        int value = reader.GetInt(cActionbarNum);
        if (value is 0 or < ACTION_SLOT_MUL)
            return;

        int slotIdx = (value / ACTION_SLOT_MUL) - 1;

        // Guard against a corrupt / mis-aligned read producing a slot index
        // outside the 120-slot action bar range.
        if ((uint)slotIdx >= (uint)castTimeData.Length)
            return;

        int rem = value % ACTION_SLOT_MUL;

        bool isItem = rem >= ITEM_FLAG_OFFSET;
        int castTimeMs = isItem ? rem - ITEM_FLAG_OFFSET : rem;

        castTimeData[slotIdx] = castTimeMs;
        isItemData[slotIdx] = isItem;
    }

    public void Reset()
    {
        castTimeData.AsSpan().Clear();
        isItemData.AsSpan().Clear();
    }

    public int Get(KeyAction keyAction) => castTimeData[keyAction.SlotIndex];

    public bool HasCastBar(KeyAction keyAction) => castTimeData[keyAction.SlotIndex] > 0;

    public bool IsItem(KeyAction keyAction) => isItemData[keyAction.SlotIndex];

    public int GetBySlotIndex(int slotIndex) =>
        (uint)slotIndex < (uint)castTimeData.Length ? castTimeData[slotIndex] : 0;
}
