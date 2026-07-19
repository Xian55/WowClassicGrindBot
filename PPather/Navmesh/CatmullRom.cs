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

    /// <summary>Endpoints are preserved; needs at least 4 control points.</summary>
    public static List<Vector3> Smooth(List<Vector3> points, int pointsPerSegment = PointsPerSegment)
    {
        if (points.Count < 4)
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

            for (int s = 1; s <= pointsPerSegment; s++)
            {
                float t = s / (float)pointsPerSegment;
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
