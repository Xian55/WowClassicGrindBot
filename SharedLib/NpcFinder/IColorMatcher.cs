using System.Runtime.CompilerServices;

namespace SharedLib.NpcFinder;

public interface IColorMatcher
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool IsMatch(byte r, byte g, byte b);
}
