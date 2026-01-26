using Core.Minimap;

using Microsoft.Extensions.Logging;

using SharedLib;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;

using System;
using System.Buffers;

namespace Core;

public sealed class MinimapNodeFinder
{
    private readonly ILogger logger;
    private readonly IMinimapImageProvider provider;
    public event EventHandler<MinimapNodeEventArgs>? NodeEvent;

    private readonly ArrayCounter counter;

    private const int minScore = 2;

    public MinimapNodeFinder(ILogger logger, IMinimapImageProvider provider)
    {
        this.logger = logger;
        this.provider = provider;

        counter = new();
    }

    public void Update()
    {
        ReadOnlySpan<Point> span = FindYellowPoints();
        ScorePoints(span, out Point best, out int amountAboveMin);
        NodeEvent?.Invoke(this, new MinimapNodeEventArgs(best.X, best.Y, amountAboveMin));
    }

    private ReadOnlySpan<Point> FindYellowPoints()
    {
        var pooler = ArrayPool<Point>.Shared;
        Point[] points = pooler.Rent(MinimapRowOperation.SIZE);

        counter.count = 0;

        MinimapRowOperation operation = new(
            provider.MiniMapImage.Frames[0].PixelBuffer,
            provider.MiniMapRect, counter, points);

        ParallelRowIterator.IterateRows<MinimapRowOperation, Point>(
            Configuration.Default,
            operation.rect,
            in operation);

        pooler.Return(points);

        return points.AsSpan(0, counter.count);
    }

    private static void ScorePoints(ReadOnlySpan<Point> points, out Point best, out int amountAboveMin)
    {
        const int size = 5;

        best = new Point();
        amountAboveMin = 0;

        int maxIndex = -1;
        int maxScore = 0;

        for (int i = 0; i < points.Length; i++)
        {
            Point pi = points[i];

            int score = 0;
            for (int j = 0; j < points.Length; j++)
            {
                Point pj = points[j];

                if (i != j &&
                    (Math.Abs(pi.X - pj.X) < size ||
                    Math.Abs(pi.Y - pj.Y) < size))
                {
                    score++;
                }
            }

            if (score > minScore)
                amountAboveMin++;

            if (maxScore < score)
            {
                maxIndex = i;
                maxScore = score;
            }
        }

        if (maxIndex >= 0 && maxScore > minScore)
        {
            best = points[maxIndex];
        }
    }
}