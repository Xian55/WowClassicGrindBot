using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PPather.Triangles.GameV2;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct MPHD
{
    public readonly Magic magic;
    public readonly UInt32 size;
    public readonly UInt32_8 data;
};

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct AdtData
{
    public readonly UInt32 exists;
    public readonly UInt32 data;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct MAIN
{
    public readonly Magic magic;
    public readonly UInt32 size;

    public readonly AdtData_Array adt;

    public AdtData this[int y, int x]
    {
        get => adt[y * Const.WDT_MAP_SIZE + x];
    }
};

public static class Const
{
    public const int WDT_MAP_SIZE = 64;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct Wdt
{
    public Wdt(byte[] data, int size)
    {
        Data = data;
        Size = (UInt32)size;
    }

    public readonly byte[] Data;
    public readonly UInt32 Size;

    public MVER Mver() => MemoryMarshal.Read<MVER>(Data.AsSpan());
    public MPHD Mphd() => MemoryMarshal.Read<MPHD>(Data.AsSpan(Unsafe.SizeOf<MVER>()));
    public MAIN Main() => MemoryMarshal.Read<MAIN>(Data.AsSpan(Unsafe.SizeOf<MVER>() + Unsafe.SizeOf<MPHD>()));
}
