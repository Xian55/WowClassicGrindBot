using System;
using System.Numerics;
using System.Runtime.CompilerServices;

using Newtonsoft.Json;

using Wmo;

namespace PPather.Graph;

public readonly record struct RoadSegment(string Name, int Width, Vector2[] Points)
{
    /// <summary>
    /// Half-width of the road corridor in world units.
    /// Width is measured in MCNK chunks on each side.
    /// </summary>
    [JsonIgnore]
    public float HalfWidthWorld => Width * ChunkReader.CHUNKSIZE;

    /// <summary>
    /// Minimum distance from point (px, py) to this road's polyline centerline.
    /// </summary>
    public float DistanceToCenter(float px, float py)
    {
        ReadOnlySpan<Vector2> pts = Points;
        if (pts.Length == 0)
            return float.MaxValue;

        if (pts.Length == 1)
        {
            float ddx = px - pts[0].X;
            float ddy = py - pts[0].Y;
            return MathF.Sqrt(ddx * ddx + ddy * ddy);
        }

        float minDistSq = float.MaxValue;

        for (int i = 0; i < pts.Length - 1; i++)
        {
            float distSq = PointToSegmentDistanceSq(
                px, py,
                pts[i].X, pts[i].Y,
                pts[i + 1].X, pts[i + 1].Y);

            if (distSq < minDistSq)
                minDistSq = distSq;
        }

        return MathF.Sqrt(minDistSq);
    }

    /// <summary>
    /// Closest point on line segment (ax, ay)-(bx, by) to point (px, py).
    /// Returns squared distance; closest point in out params.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ClosestPointOnSegment(
        float px, float py,
        float ax, float ay,
        float bx, float by,
        out float closestX, out float closestY)
    {
        float abx = bx - ax;
        float aby = by - ay;
        float apx = px - ax;
        float apy = py - ay;

        float dot = apx * abx + apy * aby;
        float lenSq = abx * abx + aby * aby;

        // t = projection parameter clamped to [0,1]
        float t = lenSq > 0f ? Math.Clamp(dot / lenSq, 0f, 1f) : 0f;

        closestX = ax + t * abx;
        closestY = ay + t * aby;

        float ddx = px - closestX;
        float ddy = py - closestY;
        return ddx * ddx + ddy * ddy;
    }

    /// <summary>
    /// Squared distance from point (px, py) to line segment (ax, ay)-(bx, by).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float PointToSegmentDistanceSq(
        float px, float py,
        float ax, float ay,
        float bx, float by) =>
        ClosestPointOnSegment(px, py, ax, ay, bx, by, out _, out _);

    /// <summary>
    /// Closest point on this road's polyline centerline to point (px, py).
    /// Returns squared distance; closest point in out params.
    /// </summary>
    public float ClosestPointOnCenter(float px, float py, out float rx, out float ry)
    {
        ReadOnlySpan<Vector2> pts = Points;
        if (pts.Length == 0)
        {
            rx = px;
            ry = py;
            return float.MaxValue;
        }

        if (pts.Length == 1)
        {
            rx = pts[0].X;
            ry = pts[0].Y;
            float ddx = px - rx;
            float ddy = py - ry;
            return ddx * ddx + ddy * ddy;
        }

        float minDistSq = float.MaxValue;
        rx = px;
        ry = py;

        for (int i = 0; i < pts.Length - 1; i++)
        {
            float distSq = ClosestPointOnSegment(
                px, py,
                pts[i].X, pts[i].Y,
                pts[i + 1].X, pts[i + 1].Y,
                out float cx, out float cy);

            if (distSq < minDistSq)
            {
                minDistSq = distSq;
                rx = cx;
                ry = cy;
            }
        }

        return minDistSq;
    }
}
