using System;
using System.Runtime.InteropServices;

namespace PPather.Triangles.GameV2;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct MVER
{
    public readonly Magic magic;
    public readonly UInt32 size;
    public readonly UInt32 version;
}
