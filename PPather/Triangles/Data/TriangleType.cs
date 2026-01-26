using System;
using System.Runtime.CompilerServices;

namespace PPather;

[System.Flags]
public enum TriangleType : byte
{
    None = 0,
    Terrain = 1,
    Water = 2,
    Object = 4,
    Model = 8
}

public static class TriangleType_Ext
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Has(this TriangleType flags, TriangleType flag)
    {
        return (flags & flag) != 0;
    }

    public static int ToIndex(this TriangleType type)
    {
        return type switch
        {
            TriangleType.Terrain => 0,
            TriangleType.Water => 1,
            TriangleType.Object => 2,
            TriangleType.Model => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(type), $"Unexpected value: {type}")
        };
    }
}
