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
    public const int SIZE = 128;

    private const int EDGE_PIXEL = 10;

    private const byte maxBlue = 80;
    private const byte minRedGreen = 160;

    public readonly Rectangle rect;
    private readonly Point center;
    private readonly float radius;

    private readonly Buffer2D<Bgra32> source;
    private readonly Point[] points;
    private readonly ArrayCounter counter;

    public MinimapRowOperation(
        Buffer2D<Bgra32> source,
        MinimapSettings settings,
        ArrayCounter counter,
        Point[] points)
    {
        this.source = source;
        this.points = points;
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
        return SIZE;
    }

    [SkipLocalsInit]
    public void Invoke(int y, Span<Point> span)
    {
        ReadOnlySpan<Bgra32> row = source.DangerousGetRowSpan(y);
        int i = 0;

        for (int x = rect.Left; x < rect.Right; x++)
        {
            if (!IsValidSquareLocation(x, y, center, radius))
            {
                continue;
            }

            ref readonly Bgra32 pixel = ref row[x];
            if (IsMatch(pixel))
            {
                if (i >= SIZE)
                    break;

                points[i++] = new(x, y);
            }
        }

        if (i == 0)
            return;

        Interlocked.Add(ref counter.count, i);

        span[..i].CopyTo(points.AsSpan(counter.count, i));

        static bool IsValidSquareLocation(int x, int y, Point center, float width)
        {
            return MathF.Sqrt(((x - center.X) * (x - center.X)) + ((y - center.Y) * (y - center.Y))) < width;
        }

        static bool IsMatch(in Bgra32 p)
        {
            return p.R > minRedGreen && p.G > minRedGreen && p.B < maxBlue;
        }
    }
}
