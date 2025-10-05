using SharedLib;
using SharedLib.Extensions;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Core.Minimap;

internal readonly struct MinimapRowOperation : IRowOperation<Point>
{
    public const int SIZE = 256;

    private const int EDGE_PIXEL = 10;

    private const byte maxBlue = 80;
    private const byte minRedGreen = 160;

    public readonly Rectangle rect;
    private readonly Point center;
    private readonly float radius;

    private readonly Buffer2D<Bgra32> source;
    private readonly Point[] output;
    private readonly ArrayCounter counter;

    public MinimapRowOperation(
        Buffer2D<Bgra32> source,
        MinimapSettings settings,
        ArrayCounter counter,
        Point[] output)
    {
        this.source = source;
        this.output = output;
        this.counter = counter;

        int size = settings.Width;

        int x = source.Width - settings.RightOffset - size;
        int y = settings.TopOffset;

        rect = new Rectangle(x, y, size, size);

        // circle center is always in the middle of the square
        center = rect.Centre();

        // radius is half size minus frame thickness
        radius = (size / 2f) - EDGE_PIXEL;
    }

    public int GetRequiredBufferLength(Rectangle bounds)
    {
        return SIZE / 2;
    }

    [SkipLocalsInit]
    public void Invoke(int y, Span<Point> threadLocalSpan)
    {
        ReadOnlySpan<Bgra32> row = source.DangerousGetRowSpan(y);
        int localCount = 0;

        for (int x = rect.Left; x < rect.Right; x++)
        {
            if (!IsValidSquareLocation(x, y, center, radius))
            {
                continue;
            }

            ref readonly Bgra32 pixel = ref row[x];
            if (IsMatch(pixel))
            {
                if (localCount >= threadLocalSpan.Length)
                    break;

                threadLocalSpan[localCount++] = new(x, y);
            }
        }

        if (localCount == 0)
            return;

        int start = Interlocked.Add(ref counter.count, localCount) - localCount;
        threadLocalSpan[..localCount].CopyTo(output.AsSpan(start, localCount));

        static bool IsValidSquareLocation(int x, int y, Point center, float width)
        {
            return MathF.Sqrt(((x - center.X) * (x - center.X)) + ((y - center.Y) * (y - center.Y))) < width;
        }

        static bool IsMatch(in Bgra32 p)
        {
            return p.R > minRedGreen && p.G > minRedGreen && p.B < maxBlue;

            int r = p.R, g = p.G, b = p.B;
            int max = Math.Max(r, Math.Max(g, b));
            int min = Math.Min(r, Math.Min(g, b));
            int delta = max - min;

            // Skip very dark or very light gray pixels
            if (max < 90 || delta < 25)
                return false;

            // Approximate hue in degrees
            float hue = (max == r) ? 60f * ((g - b) / (float)delta)
                      : (max == g) ? 60f * (2f + (b - r) / (float)delta)
                      : 60f * (4f + (r - g) / (float)delta);
            if (hue < 0) hue += 360f;

            // Yellow hue range ~40–65°, high brightness
            return hue >= 40f && hue <= 65f && max > 150 && g > 0.8f * r;
        }
    }
}
