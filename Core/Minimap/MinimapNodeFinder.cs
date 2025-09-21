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
    
    private Rectangle rect;

    private readonly ArrayCounter counter;

    private const int minScore = 1;

    public MinimapNodeFinder(ILogger logger, IMinimapImageProvider provider)
    {
        this.logger = logger;
        this.provider = provider;

        counter = new();
    }

    public void Update()
    {
        ReadOnlySpan<Point> span = FindYellowPoints();
        ScorePoints(span, provider.MinimapSettings, out Point best, out int amountAboveMin);
        NodeEvent?.Invoke(this, new MinimapNodeEventArgs(best.X, best.Y, amountAboveMin, rect));
    }

    private ReadOnlySpan<Point> FindYellowPoints()
    {
        var pooler = ArrayPool<Point>.Shared;
        Point[] points = pooler.Rent(MinimapRowOperation.SIZE);

        counter.count = 0;

        var settings = provider.MinimapSettings;

        MinimapRowOperation operation = new(
            provider.MiniMapImage.Frames[0].PixelBuffer,
            settings, counter, points);

        rect = operation.rect;

        ParallelRowIterator.IterateRows<MinimapRowOperation, Point>(
            Configuration.Default,
            operation.rect,
            in operation);

        pooler.Return(points);

        return points.AsSpan(0, counter.count);
    }

    private static void ScorePoints(ReadOnlySpan<Point> points,
        in MinimapSettings settings,
        out Point best, out int amountAboveMin)
    {
        const int baseSize = 5;

        float zoomScale = (float)(settings.ZoomLevels - settings.Zoom) / settings.ZoomLevels;
        int size = Math.Max(3, (int)MathF.Ceiling(baseSize * zoomScale));

        best = new Point();
        amountAboveMin = 0;

        int maxIndex = -1;
        int maxScore = 0;

        for (int i = 0; i < points.Length; i++)
        {
            Point pi = points[i];
            if (pi.X == 0 && pi.Y == 0)
                continue;

            int score = 0;
            for (int j = 0; j < points.Length; j++)
            {
                Point pj = points[j];

                if (i != j &&
                    (Math.Abs(pi.X - pj.X) < size &&
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