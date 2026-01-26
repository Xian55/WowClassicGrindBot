using System;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace PPather.Extensions;

public static class BinaryReaderExtensions
{
    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe Vector3 ReadVector3(this BinaryReader b)
    {
        Vector3 v;
        Span<byte> buffer = new(&v, sizeof(Vector3));
        b.Read(buffer);
        return v;
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 ReadVector3_XZY(this BinaryReader b)
    {
        Vector3 v3 = ReadVector3(b);

        // from format
        // X Z -Y
        // to format
        // X Y Z
        (v3[1], v3[2]) = (-v3[2], v3[1]);

        return v3;
    }

    public static bool EOF(this BinaryReader b)
    {
        Stream s = b.BaseStream;
        return s.Position >= s.Length;
    }

}
