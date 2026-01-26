using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace SharedLib.NpcFinder;

internal readonly struct LineSegmentOperation : IRowOperation<LineSegment>
{
    private readonly Buffer2D<Bgra32> source;

    private readonly int size;
    private readonly Rectangle area;

    private readonly float minLength;
    private readonly float minEndLength;

    private readonly Func<byte, byte, byte, bool> colorMatcher;

    private readonly LineSegment[] segments;

    private readonly ArrayCounter counter;

    public LineSegmentOperation(
        LineSegment[] segments,
        int size,
        Rectangle area,
        float minLength,
        float minEndLength,
        ArrayCounter counter,
        Func<byte, byte, byte, bool> colorMatcher,
        Buffer2D<Bgra32> source)
    {
        this.segments = segments;
        this.size = size;
        this.area = area;
        this.minLength = minLength;
        this.minEndLength = minEndLength;
        this.counter = counter;
        this.colorMatcher = colorMatcher;
        this.source = source;
    }

    public readonly int GetRequiredBufferLength(Rectangle bounds)
    {
        return size;
    }

    [SkipLocalsInit]
    public readonly void Invoke(int y, Span<LineSegment> span)
    {
        int xStart = -1;
        int xEnd = -1;
        int end = area.Right;

        int i = 0;

        ReadOnlySpan<Bgra32> row = source.DangerousGetRowSpan(y);

        for (int x = area.Left; x < end; x++)
        {
            ref readonly Bgra32 pixel = ref row[x];

            if (!colorMatcher(pixel.R, pixel.G, pixel.B))
                continue;

            if (xStart > -1 && (x - xEnd) < minLength)
            {
                //xEnd = x;
            }
            else
            {
                if (xStart > -1 && xEnd - xStart > minEndLength)
                {
                    if (i + 1 >= size)
                        break;

                    span[i++] = new LineSegment(xStart, xEnd, y);
                }

                xStart = x;
            }
            xEnd = x;
        }

        if (xStart > -1 && xEnd - xStart > minEndLength)
        {
            span[i++] = new LineSegment(xStart, xEnd, y);
        }

        if (i == 0)
            return;

        int newCount = Interlocked.Add(ref counter.count, i);
        if (counter.count + newCount > segments.Length)
            return;

        span[..i].CopyTo(segments.AsSpan(counter.count, newCount));
    }
}
