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

    private const byte maxBlue = 80;
    private const byte minRedGreen = 150;

    private const int EDGE_PIXEL = 10;

    public readonly Rectangle rect;
    private readonly Point center;
    private readonly float radius;

    private readonly Buffer2D<Bgra32> source;
    private readonly Point[] points;
    private readonly ArrayCounter counter;

    public MinimapRowOperation(Buffer2D<Bgra32> source,
        MinimapSettings settings, ArrayCounter counter, Point[] points)
    {
        this.source = source;
        this.points = points;
        this.counter = counter;

        int size = settings.Width;
        int x = source.Width - settings.RightOffset - size;
        int y = settings.TopOffset;

        rect = new Rectangle(x, y, size, size);
        center = rect.Centre();
        radius = (size / 2f) - EDGE_PIXEL;
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
        int bufferLen = span.Length;

        for (int x = rect.Left; x < rect.Right; x++)
        {
            if (!IsValidSquareLocation(x, y, center, radius))
            {
                continue;
            }

            ref readonly Bgra32 pixel = ref row[x];

            if (IsMatch(pixel.R, pixel.G, pixel.B))
            {
                if (i + 1 >= bufferLen)
                    break;

                span[i++] = new(x, y);
            }
        }

        if (i == 0)
            return;

        int newCount = Interlocked.Add(ref counter.count, i);
        int startIndex = newCount - i;
        if (newCount > points.Length)
            return;

        span[..i].CopyTo(points.AsSpan(startIndex, i));

        static bool IsValidSquareLocation(int x, int y, Point center, float width)
        {
            return MathF.Sqrt(((x - center.X) * (x - center.X)) + ((y - center.Y) * (y - center.Y))) < width;
        }

        static bool IsMatch(byte red, byte green, byte blue)
        {
            return blue < maxBlue && red > minRedGreen && green > minRedGreen;
        }
    }

    internal static void DrawDebugMask(Image<Bgra32> image, MinimapSettings settings)
    {
        int size = settings.Width;
        int x = image.Width - settings.RightOffset - size;
        int y = settings.TopOffset;

        Rectangle rect = new(x, y, size, size);
        Point center = rect.Centre();
        float radius = (size / 2f) - EDGE_PIXEL;

        image.ProcessPixelRows(accessor =>
        {
            for (int row = 0; row < accessor.Height; row++)
            {
                Span<Bgra32> pixelRow = accessor.GetRowSpan(row);

                for (int col = 0; col < pixelRow.Length; col++)
                {
                    ref Bgra32 pixel = ref pixelRow[col];

                    float dx = col - center.X;
                    float dy = row - center.Y;
                    float dist = MathF.Sqrt((dx * dx) + (dy * dy));

                    if (MathF.Abs(dist - radius) < 1.5f)
                    {
                        pixel = new Bgra32(255, 255, 0, 255); // cyan (BGRA)
                    }
                    else if (dist >= radius)
                    {
                        pixel = new Bgra32(
                            (byte)(pixel.B / 3u),
                            (byte)(pixel.G / 3u),
                            (byte)(pixel.R / 3u),
                            pixel.A);
                    }
                    else if (IsMatch(pixel.R, pixel.G, pixel.B))
                    {
                        pixel = new Bgra32(255, 0, 255, 255); // magenta (BGRA)
                    }
                }
            }
        });

        static bool IsMatch(byte red, byte green, byte blue)
        {
            return blue < maxBlue && red > minRedGreen && green > minRedGreen;
        }
    }
}
