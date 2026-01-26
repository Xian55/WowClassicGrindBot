using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;

namespace Core;

public static class PathSimplify
{
    public const float DEFAULT = 0.3f;
    public const float HALF = 0.15f;

    // square distance from a Vector3 to a segment
    private static float GetSquareSegmentDistance(in Vector3 p, in Vector3 p1, in Vector3 p2)
    {
        float x = p1.X;
        float y = p1.Y;
        float dx = p2.X - x;
        float dy = p2.Y - y;

        if (!dx.Equals(0f) || !dy.Equals(0f))
        {
            float t = ((p.X - x) * dx + (p.Y - y) * dy) / (dx * dx + dy * dy);

            if (t > 1)
            {
                x = p2.X;
                y = p2.Y;
            }
            else if (t > 0)
            {
                x += dx * t;
                y += dy * t;
            }
        }

        dx = p.X - x;
        dy = p.Y - y;

        return (dx * dx) + (dy * dy);
    }

    // basic distance-based simplification
    private static Span<Vector3> RadialDistance(Span<Vector3> points, float sqTolerance)
    {
        var pooler = ArrayPool<Vector3>.Shared;
        Vector3[] reduced = pooler.Rent(points.Length);
        int c = 1;

        Vector3 prev = points[0];
        Vector3 curr = Vector3.Zero;

        reduced[0] = prev;

        for (int i = 1; i < points.Length; i++)
        {
            curr = points[i];
            if (Vector3.Distance(curr, prev) > sqTolerance)
            {
                reduced[c++] = curr;
                prev = curr;
            }
        }

        if (curr != Vector3.Zero && !prev.Equals(curr))
            reduced[c++] = curr;

        pooler.Return(reduced);
        return reduced.AsSpan(0, c);
    }

    // simplification using optimized Douglas-Peucker algorithm with recursion elimination
    private static Span<Vector3> DouglasPeucker(Span<Vector3> points, float sqTolerance)
    {
        int len = points.Length;
        Span<bool> markers = stackalloc bool[len];

        int? first = 0;
        int? last = len - 1;
        int? index = 0;

        Stack<int?> stack = new(len);

        var pooler = ArrayPool<Vector3>.Shared;
        Vector3[] reduced = pooler.Rent(len);
        int count = 0;

        markers[first.Value] = true;
        markers[last.Value] = true;

        while (last != null)
        {
            float maxSqDist = 0f;

            for (int? i = first + 1; first.HasValue && i < last; i++)
            {
                float sqDist = GetSquareSegmentDistance(points[i.Value], points[first.Value], points[last.Value]);
                if (sqDist > maxSqDist)
                {
                    index = i;
                    maxSqDist = sqDist;
                }
            }

            if (maxSqDist > sqTolerance)
            {
                markers[index.Value] = true;
                stack.Push(first);
                stack.Push(index);
                stack.Push(index);
                stack.Push(last);
            }

            last = stack.Count > 0 ? stack.Pop() : null;
            first = stack.Count > 0 ? stack.Pop() : null;
        }

        for (int i = 0; i < len; i++)
        {
            if (markers[i])
                reduced[count++] = points[i];
        }

        pooler.Return(reduced);
        return reduced.AsSpan(0, count);
    }

    /// <summary>
    /// Simplifies a list of Vector3 to a shorter list of Vector3.
    /// </summary>
    /// <param name="points">Vector3 original list of Vector3</param>
    /// <param name="tolerance">Tolerance tolerance in the same measurement as the Vector3 coordinates</param>
    /// <param name="highestQuality">Enable highest quality for using Douglas-Peucker, set false for Radial-Distance algorithm</param>
    /// <returns>Simplified list of Vector3</returns>
    public static Span<Vector3> Simplify(Span<Vector3> points, float tolerance = DEFAULT, bool highestQuality = false)
    {
        if (points.Length == 0)
            return [];

        float sqTolerance = tolerance * tolerance;

        if (highestQuality)
            return DouglasPeucker(points, sqTolerance);

        Span<Vector3> reduced = RadialDistance(points, sqTolerance);
        return DouglasPeucker(reduced, sqTolerance);
    }
}
