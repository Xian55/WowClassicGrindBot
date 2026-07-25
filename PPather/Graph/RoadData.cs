using System;

namespace PPather.Graph;

public sealed class RoadData
{
    public RoadSegment[] Roads { get; init; } = [];

    /// <summary>
    /// Returns the normalized distance from the nearest road centerline [0..1],
    /// or -1 if no roads are defined or point is outside all corridors.
    /// 0 = on centerline, 1 = at corridor edge.
    /// </summary>
    public float GetRoadDistanceFraction(float px, float py)
    {
        ReadOnlySpan<RoadSegment> roads = Roads;
        if (roads.Length == 0)
            return -1f;

        float bestFraction = -1f;

        foreach (RoadSegment road in roads)
        {
            float dist = road.DistanceToCenter(px, py);
            float halfWidth = road.HalfWidthWorld;
            if (dist > halfWidth)
                continue;

            float frac = dist / halfWidth;
            if (bestFraction < 0f || frac < bestFraction)
                bestFraction = frac;
        }

        return bestFraction;
    }

    /// <summary>
    /// Finds the closest point on any road centerline to (px, py).
    /// Returns true if a road point was found within corridor; out params hold the XY.
    /// </summary>
    public bool TryGetClosestRoadPoint(float px, float py, out float rx, out float ry)
    {
        ReadOnlySpan<RoadSegment> roads = Roads;
        if (roads.Length == 0)
        {
            rx = px;
            ry = py;
            return false;
        }

        float bestDistSq = float.MaxValue;
        rx = px;
        ry = py;

        foreach (RoadSegment road in roads)
        {
            float distSq = road.ClosestPointOnCenter(px, py, out float cx, out float cy);

            // Only consider points within the road corridor
            float halfWidth = road.HalfWidthWorld;
            if (distSq > halfWidth * halfWidth)
                continue;

            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                rx = cx;
                ry = cy;
            }
        }

        return bestDistSq < float.MaxValue;
    }
}