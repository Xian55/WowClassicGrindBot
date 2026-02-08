using Core.Minimap;

using Microsoft.Extensions.Logging;

using SharedLib;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;

using System;
using System.Buffers;

namespace Core;

public sealed class MinimapNodeFinder
{
    private readonly ILogger logger;
    private readonly IMinimapImageProvider provider;
    public event EventHandler<MinimapNodeEventArgs>? NodeEvent;
    private Rectangle rect;

    private readonly ArrayCounter counter;

    private const int minScore = 1;
    private const int size = 5;

    public MinimapNodeFinder(ILogger logger, IMinimapImageProvider provider)
    {
        this.logger = logger;
        this.provider = provider;

        counter = new();
    }

    public void Update()
    {
        var settings = provider.MinimapSettings;
        if (settings.Width <= 0)
            return;

        var pooler = ArrayPool<Point>.Shared;
        Point[] points = pooler.Rent(MinimapRowOperation.SIZE);
        points.AsSpan().Fill(Point.Empty);

        ReadOnlySpan<Point> span = FindYellowPoints(points);
        ScorePoints(span, settings, out Point best, out int amountAboveMin);

        pooler.Return(points, clearArray: true);

        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("Minimap: {RawCount} yellow px, {Scored} scored, best=({X},{Y})",
                span.Length, amountAboveMin, best.X, best.Y);
        }

        NodeEvent?.Invoke(this, new MinimapNodeEventArgs(best.X, best.Y, amountAboveMin, rect));
    }

    private ReadOnlySpan<Point> FindYellowPoints(Point[] points)
    {
        counter.count = 0;

        MinimapSettings settings = provider.MinimapSettings;

        MinimapRowOperation operation = new(
            provider.MiniMapImage.Frames[0].PixelBuffer,
            settings, counter, points);

        rect = operation.rect;

        ParallelRowIterator.IterateRows<MinimapRowOperation, Point>(
            Configuration.Default,
            operation.rect,
            in operation);

        return points.AsSpan(0, counter.count);
    }

    public Image<Bgra32> CreateDebugImage()
    {
        Image<Bgra32> clone = provider.MiniMapImage.Clone();
        MinimapRowOperation.DrawDebugMask(clone, provider.MinimapSettings);
        return clone;
    }

    private static void ScorePoints(ReadOnlySpan<Point> points,
        in MinimapSettings settings,
        out Point best, out int amountAboveMin)
    {
        best = Point.Empty;
        amountAboveMin = 0;

        Span<byte> scores = stackalloc byte[points.Length];

        for (int i = 0; i < points.Length; i++)
        {
            Point pi = points[i];
            if (pi == Point.Empty)
                continue;

            byte score = 0;
            for (int j = 0; j < points.Length; j++)
            {
                if (i == j) continue;

                Point pj = points[j];

                if (Math.Abs((long)pi.X - pj.X) < size &&
                    Math.Abs((long)pi.Y - pj.Y) < size)
                {
                    score++;
                }
            }

            scores[i] = score;
        }


        int sumX = 0, sumY = 0, sumW = 0;

        for (int i = 0; i < points.Length; i++)
        {
            int w = scores[i];
            if (w <= minScore)
                continue;

            sumX += points[i].X * w;
            sumY += points[i].Y * w;
            sumW += w;
            amountAboveMin++;
        }

        if (sumW > 0)
        {
            best = new Point(sumX / sumW, sumY / sumW);
        }

    }

}
