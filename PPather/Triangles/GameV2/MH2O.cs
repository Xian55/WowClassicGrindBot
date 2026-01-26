using System;
using System.Runtime.InteropServices;

namespace PPather.Triangles.GameV2;


[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct MH2OHeader
{
    public readonly Magic magic;
    public readonly uint size;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct LiquidCell
{
    public readonly uint offsetInstances;
    public readonly uint used;
    public readonly uint offsetAttributes;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct MH2O
{
    public readonly Magic magic;
    public readonly uint size;

    public readonly LiquidCell_Array Data;

    public LiquidCell this[int y, int x]
    {
        get => Data[y * Adt.ADT_CELLS_PER_GRID + x];
    }

    // The entire MH2O data block (which includes:
    // - 8 bytes header (4 bytes magic, 4 bytes size)
    // - then ADT_CELLS_PER_GRID * ADT_CELLS_PER_GRID LiquidCell entries,
    // - then additional data referenced by offsets)

    //public byte[] Data { get; }
    //public MH2O(byte[] data)
    //{
    //    Data = data;
    //}

    // Helper: computes the size of the header (magic + size)
    private const int HeaderSize = 8;

    // Helper: gets the size of a LiquidCell (should match the 3 x 4-byte fields = 12 bytes)
    private static readonly int LiquidCellSize = Marshal.SizeOf<LiquidCell>();

    // Computes the offset in Data for the liquid cell at (x, y)
    private static int GetLiquidCellOffset(int x, int y)
    {
        int index = x * Adt.ADT_CELLS_PER_GRID + y;
        return HeaderSize + index * LiquidCellSize;
    }

    // Reads the liquid cell at (x,y)
    public LiquidCell GetLiquidCell(int x, int y)
    {
        //int offset = GetLiquidCellOffset(x, y);
        //return MemoryMarshal.Read<LiquidCell>(Data.AsSpan(offset, LiquidCellSize));
        return this[y, x];
    }

    /// <summary>
    /// Returns the AdtLiquid instance for a given cell if available; otherwise, null.
    /// </summary>
    public AdtLiquid? GetInstance(byte[] Data, uint x, uint y)
    {
        LiquidCell cell = GetLiquidCell((int)x, (int)y);
        if (cell.used != 0 && cell.offsetInstances != 0)
        {
            int instanceOffset = HeaderSize + (int)cell.offsetInstances;
            if (instanceOffset + Marshal.SizeOf<AdtLiquid>() <= Data.Length)
            {
                return MemoryMarshal.Read<AdtLiquid>(Data.AsSpan(instanceOffset, Marshal.SizeOf<AdtLiquid>()));
            }
        }
        return null;
    }

    // A static default attribute (all bits set to 1)
    private static readonly AdtLiquidAttributes DefaultAttributes;
    /*
        = new()
    {
        fishable = ulong.MaxValue,
        deep = ulong.MaxValue
    };
    */

    /// <summary>
    /// Returns the AdtLiquidAttributes for the given cell.
    /// If used is true and offsetAttributes is nonzero, reads them from Data;
    /// if used is true but offsetAttributes is zero, returns a default value;
    /// otherwise, returns null.
    /// </summary>
    public AdtLiquidAttributes? GetAttributes(byte[] Data, uint x, uint y)
    {
        LiquidCell cell = GetLiquidCell((int)x, (int)y);
        if (cell.used != 0)
        {
            if (cell.offsetAttributes != 0)
            {
                int attrOffset = HeaderSize + (int)cell.offsetAttributes;
                if (attrOffset + Marshal.SizeOf<AdtLiquidAttributes>() <= Data.Length)
                {
                    return MemoryMarshal.Read<AdtLiquidAttributes>(Data.AsSpan(attrOffset, Marshal.SizeOf<AdtLiquidAttributes>()));
                }
            }
            else
            {
                // Return the default attributes if offsetAttributes is zero.
                return DefaultAttributes;
            }
        }
        return null;
    }

    /// <summary>
    /// Returns the render mask (as a ulong) referenced by the given AdtLiquid,
    /// or null if offsetRenderMask is zero.
    /// </summary>
    public static ulong? GetRenderMask(byte[] Data, AdtLiquid h)
    {
        if (h.offsetRenderMask != 0)
        {
            int offset = HeaderSize + (int)h.offsetRenderMask;
            if (offset + sizeof(ulong) <= Data.Length)
            {
                return MemoryMarshal.Read<ulong>(Data.AsSpan(offset, sizeof(ulong)));
            }
        }
        return null;
    }

    /// <summary>
    /// Returns a ReadOnlySpan of floats representing the liquid height data,
    /// if available for the given AdtLiquid.
    /// Returns null if offsetVertexData is zero or the vertex format is not supported.
    /// </summary>
    public static ReadOnlySpan<float> GetLiquidHeight(byte[] Data, AdtLiquid liquid)
    {
        if (liquid.offsetVertexData == 0)
            return null;

        switch (liquid.vertexFormat)
        {
            case AdtLiquidVertexFormat.HeightDepth:
            case AdtLiquidVertexFormat.HeightTextureCoord:
                {
                    int offset = HeaderSize + (int)liquid.offsetVertexData;
                    // Ensure we have at least one float available.
                    if (offset + sizeof(float) <= Data.Length)
                    {
                        // Return the remainder of the data interpreted as floats.
                        return MemoryMarshal.Cast<byte, float>(Data.AsSpan(offset));
                    }
                    return null;
                }
            case AdtLiquidVertexFormat.Depth:
            default:
                return [];
        }
    }
}
