using System.Runtime.InteropServices;

namespace Wmo;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct SMChunkInfo
{
    public readonly uint offset;
    public readonly uint size;
    public readonly uint flags;
    public readonly uint padding;
}