#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;

namespace PPather.Navmesh;

/// <summary>
/// Centripetal Catmull-Rom spline (Barry-Goldman formulation, alpha 0.5).
/// Standard textbook math - mirrors the output shape of the RemoteV3 server's
/// SMOOTH_CATMULLROM option that the WASD follower is already tuned for.
/// </summary>
public static class CatmullRom
{
    public const float Alpha = 0.5f;
    public const int PointsPerSegment = 4;

    /// <summary>
    /// Minimum points to resample. Two (a single leg) is enough: the loop clamps
    /// the missing outer control points to the endpoints, so short 2-3 corner
    /// funnel paths still get densified. Bailing at 4 handed those short legs to
    /// the follower as sparse corners, which - popping a waypoint several yards
    /// out - cut across whatever the path routed around (into trees/rocks).
    /// </summary>
    public const int MinPoints = 2;

    /// <summary>
    /// Resamples the control points to roughly <paramref name="targetSpacingYd"/>
    /// yards apart (0 restores the fixed per-segment subdivision), preserving the
    /// endpoints. A fixed subdivision count gives a long corner-to-corner leg the
    /// same few points as a short one, so on long legs the waypoints end up far
    /// apart and the WASD follower - which pops a waypoint several yards out -
    /// cuts the corner across whatever the path was routing around. Fixed spacing
    /// keeps a waypoint just ahead of the follower on the curve. Query-time only:
    /// shapes the path handed to the follower, does not touch the baked tiles.
    /// </summary>
    public static List<Vector3> Smooth(List<Vector3> points, float targetSpacingYd,
        int pointsPerSegment = PointsPerSegment)
    {
        if (points.Count < MinPoints)
        {
            return points;
        }

        List<Vector3> output = new((points.Count - 1) * pointsPerSegment + 1)
        {
            points[0]
        };

        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector3 p0 = points[Math.Max(0, i - 1)];
            Vector3 p1 = points[i];
            Vector3 p2 = points[i + 1];
            Vector3 p3 = points[Math.Min(points.Count - 1, i + 2)];

            // Subdivide this leg by its length, not a fixed count: a long leg
            // gets proportionally more points so the spacing stays roughly
            // uniform. Catmull-Rom arc length is not linear in t, so this is
            // approximate - but "roughly every TargetSpacingYd" is all the
            // follower needs. TargetSpacingYd <= 0 falls back to the old fixed
            // subdivision.
            int n = pointsPerSegment;
            if (targetSpacingYd > 0f)
            {
                float legLength = Vector3.Distance(p1, p2);
                n = Math.Max(1, (int)MathF.Ceiling(legLength / targetSpacingYd));
            }

            for (int s = 1; s <= n; s++)
            {
                float t = s / (float)n;
                output.Add(Sample(p0, p1, p2, p3, t));
            }
        }

        return output;
    }

    private static Vector3 Sample(in Vector3 p0, in Vector3 p1, in Vector3 p2, in Vector3 p3, float t)
    {
        float t0 = 0f;
        float t1 = t0 + KnotInterval(p0, p1);
        float t2 = t1 + KnotInterval(p1, p2);
        float t3 = t2 + KnotInterval(p2, p3);

        // Degenerate (duplicate) control points collapse the knot interval -
        // fall back to linear interpolation on the middle segment.
        if (t1 == t2 || t0 == t1 || t2 == t3)
        {
            return Vector3.Lerp(p1, p2, t);
        }

        float u = t1 + (t * (t2 - t1));

        Vector3 a1 = ((t1 - u) / (t1 - t0) * p0) + ((u - t0) / (t1 - t0) * p1);
        Vector3 a2 = ((t2 - u) / (t2 - t1) * p1) + ((u - t1) / (t2 - t1) * p2);
        Vector3 a3 = ((t3 - u) / (t3 - t2) * p2) + ((u - t2) / (t3 - t2) * p3);

        Vector3 b1 = ((t2 - u) / (t2 - t0) * a1) + ((u - t0) / (t2 - t0) * a2);
        Vector3 b2 = ((t3 - u) / (t3 - t1) * a2) + ((u - t1) / (t3 - t1) * a3);

        return ((t2 - u) / (t2 - t1) * b1) + ((u - t1) / (t2 - t1) * b2);
    }

    private static float KnotInterval(in Vector3 a, in Vector3 b)
    {
        return MathF.Pow(Vector3.DistanceSquared(a, b), Alpha * 0.5f);
    }
}
