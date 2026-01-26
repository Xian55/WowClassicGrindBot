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
    public const int SIZE = 100;

    private const byte maxBlue = 34;
    private const byte minRedGreen = 176;

    private const int minX = 6;
    private const int maxX = 170;
    private const int minY = 36;
    private readonly int maxY;

    public readonly Rectangle rect;
    private readonly Point center;
    private readonly float radius;

    private readonly Buffer2D<Bgra32> source;
    private readonly Point[] points;
    private readonly ArrayCounter counter;

    public MinimapRowOperation(Buffer2D<Bgra32> source,
        Rectangle minimapRect, ArrayCounter counter, Point[] points)
    {
        this.source = source;
        this.points = points;
        this.counter = counter;

        maxY = minimapRect.Height - 6;

        rect = new(minX, minY, maxX - minX, maxY - minY);
        center = rect.Centre();
        radius = (maxX - minX) / 2f;
    }

    public int GetRequiredBufferLength(Rectangle bounds)
    {
        return 64; // SIZE / 2
    }

    [SkipLocalsInit]
    public void Invoke(int y, Span<Point> span)
    {
        ReadOnlySpan<Bgra32> row = source.DangerousGetRowSpan(y);

        int i = 0;

        for (int x = minX; x < maxX; x++)
        {
            if (!IsValidSquareLocation(x, y, center, radius))
            {
                continue;
            }

            ref readonly Bgra32 pixel = ref row[x];

            if (IsMatch(pixel.R, pixel.G, pixel.B))
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

        static bool IsMatch(byte red, byte green, byte blue)
        {
            return blue < maxBlue && red > minRedGreen && green > minRedGreen;
        }
    }
}
