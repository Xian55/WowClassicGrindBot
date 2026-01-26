using System;
using System.Collections.Specialized;
using System.Numerics;

using static Core.ActionBar;

namespace Core;

public interface IActionBarBits
{
    void Update(IAddonDataProvider reader);
    bool Is(KeyAction keyAction);

    bool Any { get; }
    int Count { get; }
}

public interface ICurrentAction : IActionBarBits { }

public interface IUsableAction : IActionBarBits { }

public sealed class ActionBarBits<T> : IActionBarBits, IReader
{
    private readonly int[] cells;

    private readonly BitVector32[] bits;

    public ActionBarBits(params ReadOnlySpan<int> cells)
    {
        this.cells = cells.ToArray();
        bits = new BitVector32[cells.Length];
    }

    public void Update(IAddonDataProvider reader)
    {
        Span<BitVector32> span = bits;
        for (int i = 0; i < span.Length; i++)
        {
            span[i] = new(reader.GetInt(cells[i]));
        }
    }

    // https://wowwiki-archive.fandom.com/wiki/ActionSlot
    public bool Is(KeyAction keyAction)
    {
        if (keyAction.Slot == 0) return false;

        int index = keyAction.SlotIndex;
        return bits
            [index / BIT_PER_CELL]
            [Mask.M[index % BIT_PER_CELL]];
    }

    public bool Any
    {
        get
        {
            ReadOnlySpan<BitVector32> span = bits;
            BitVector32 zero = new();
            return span.IndexOfAnyExcept(zero) >= 0;
        }
    }

    public int Count
    {
        get
        {
            ReadOnlySpan<BitVector32> span = bits;
            int count = 0;
            foreach (BitVector32 b in span)
            {
                count += BitOperations.PopCount((nuint)b.Data);
            }
            return count;
        }
    }
}
