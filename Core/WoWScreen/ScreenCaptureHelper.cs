using System;
using System.Runtime.CompilerServices;

namespace Core;

internal static class ScreenCaptureHelper
{
    public const int Bgra32Size = 4;

    [SkipLocalsInit]
    public static void CopyRegion(
        ReadOnlySpan<byte> src, int srcRowPitch,
        int srcX, int srcY,
        Span<byte> dest,
        int width, int height)
    {
        int bytesPerRow = width * Bgra32Size;

        // Fast path: source region is contiguous (no X offset, no pitch padding)
        if (srcX == 0 && srcRowPitch == bytesPerRow)
        {
            src.Slice(srcY * srcRowPitch, bytesPerRow * height).CopyTo(dest);
            return;
        }

        for (int y = 0; y < height; y++)
        {
            src.Slice((srcY + y) * srcRowPitch + srcX * Bgra32Size, bytesPerRow)
               .CopyTo(dest.Slice(y * bytesPerRow, bytesPerRow));
        }
    }
}
