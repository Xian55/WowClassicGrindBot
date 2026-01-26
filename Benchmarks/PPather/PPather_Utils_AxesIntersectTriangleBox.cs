using BenchmarkDotNet.Attributes;

using System.Numerics;
using System.Runtime.CompilerServices;

using static System.MathF;
using static System.Numerics.Vector3;

namespace Benchmarks.PPather;

public class PPather_Utils_AxesIntersectTriangleBox
{
    //static readonly Vector3 v0 = new(0.1f, 0.2f, 0.3f);
    //static readonly Vector3 v1 = new(-0.1f, 0.2f, -0.3f);
    //static readonly Vector3 v2 = new(0.1f, -0.2f, 0.3f);

    //static readonly Vector3 boxExtents = new(1.0f, 1.0f, 1.0f); // AABB with extents of 1 unit in each axis

    //

    //static readonly Vector3 v0 = new(1.5f, 1.5f, 1.5f);
    //static readonly Vector3 v1 = new(3.0f, 0.0f, -1.0f);
    //static readonly Vector3 v2 = new(0.0f, 3.0f, 2.0f);

    //static readonly Vector3 boxExtents = new(2.0f, 2.0f, 2.0f);

    //

    static readonly Vector3 v0 = new(5.0f, 5.0f, 5.0f); // Far outside the box
    static readonly Vector3 v1 = new(6.0f, 5.0f, 4.0f); // Far outside the box
    static readonly Vector3 v2 = new(5.0f, 6.0f, 4.0f); // Far outside the box

    static readonly Vector3 boxExtents = new(1.0f, 1.0f, 1.0f); // AABB with extents of 1 unit in each axis

    //

    static readonly Vector3 f0 = v1 - v0;
    static readonly Vector3 f1 = v2 - v1;
    static readonly Vector3 f2 = v0 - v2;


    [Benchmark(Baseline = true)]
#pragma warning disable CA1822 // Mark members as static
    public void Old_AxesIntersectTriangleBox()
#pragma warning restore CA1822 // Mark members as static
    {
        _ = AxesIntersectTriangleBox_old(
            in v0, in v1, in v2,
            in boxExtents,
            in f0, in f1, in f2);
    }

    [Benchmark]
#pragma warning disable CA1822 // Mark members as static
    public void New_AxesIntersectTriangleBox()
#pragma warning restore CA1822 // Mark members as static
    {
        _ = AxesIntersectTriangleBox(
            in v0, in v1, in v2,
            in boxExtents,
            in f0, in f1, in f2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool AxesIntersectTriangleBox(
        in Vector3 v0, in Vector3 v1, in Vector3 v2,
        in Vector3 boxExtents,
        in Vector3 f0, in Vector3 f1, in Vector3 f2)
    {
        float r, p0, p1, p2;

        // Axis 1: Cross product of triangle edge f0 with the X, Y, Z axes
        p0 = v0.Z * f0.Y - v0.Y * f0.Z;
        p1 = v1.Z * f0.Y - v1.Y * f0.Z;
        p2 = v2.Z * f0.Y - v2.Y * f0.Z;
        r = boxExtents.Y * Abs(f0.Z) + boxExtents.Z * Abs(f0.Y);
        if (Max3(p0, p1, p2) < -r || Min3(p0, p1, p2) > r) return false;

        p0 = v0.X * f0.Z - v0.Z * f0.X;
        p1 = v1.X * f0.Z - v1.Z * f0.X;
        p2 = v2.X * f0.Z - v2.Z * f0.X;
        r = boxExtents.X * Abs(f0.Z) + boxExtents.Z * Abs(f0.X);
        if (Max3(p0, p1, p2) < -r || Min3(p0, p1, p2) > r) return false;

        p0 = v0.Y * f0.X - v0.X * f0.Y;
        p1 = v1.Y * f0.X - v1.X * f0.Y;
        p2 = v2.Y * f0.X - v2.X * f0.Y;
        r = boxExtents.X * Abs(f0.Y) + boxExtents.Y * Abs(f0.X);
        if (Max3(p0, p1, p2) < -r || Min3(p0, p1, p2) > r) return false;

        // Axis 2: Cross product of triangle edge f1 with the X, Y, Z axes
        p0 = v0.Z * f1.Y - v0.Y * f1.Z;
        p1 = v1.Z * f1.Y - v1.Y * f1.Z;
        p2 = v2.Z * f1.Y - v2.Y * f1.Z;
        r = boxExtents.Y * Abs(f1.Z) + boxExtents.Z * Abs(f1.Y);
        if (Max3(p0, p1, p2) < -r || Min3(p0, p1, p2) > r) return false;

        p0 = v0.X * f1.Z - v0.Z * f1.X;
        p1 = v1.X * f1.Z - v1.Z * f1.X;
        p2 = v2.X * f1.Z - v2.Z * f1.X;
        r = boxExtents.X * Abs(f1.Z) + boxExtents.Z * Abs(f1.X);
        if (Max3(p0, p1, p2) < -r || Min3(p0, p1, p2) > r) return false;

        p0 = v0.Y * f1.X - v0.X * f1.Y;
        p1 = v1.Y * f1.X - v1.X * f1.Y;
        p2 = v2.Y * f1.X - v2.X * f1.Y;
        r = boxExtents.X * Abs(f1.Y) + boxExtents.Y * Abs(f1.X);
        if (Max3(p0, p1, p2) < -r || Min3(p0, p1, p2) > r) return false;

        // Axis 3: Cross product of triangle edge f2 with the X, Y, Z axes
        p0 = v0.Z * f2.Y - v0.Y * f2.Z;
        p1 = v1.Z * f2.Y - v1.Y * f2.Z;
        p2 = v2.Z * f2.Y - v2.Y * f2.Z;
        r = boxExtents.Y * Abs(f2.Z) + boxExtents.Z * Abs(f2.Y);
        if (Max3(p0, p1, p2) < -r || Min3(p0, p1, p2) > r) return false;

        p0 = v0.X * f2.Z - v0.Z * f2.X;
        p1 = v1.X * f2.Z - v1.Z * f2.X;
        p2 = v2.X * f2.Z - v2.Z * f2.X;
        r = boxExtents.X * Abs(f2.Z) + boxExtents.Z * Abs(f2.X);
        if (Max3(p0, p1, p2) < -r || Min3(p0, p1, p2) > r) return false;

        p0 = v0.Y * f2.X - v0.X * f2.Y;
        p1 = v1.Y * f2.X - v1.X * f2.Y;
        p2 = v2.Y * f2.X - v2.X * f2.Y;
        r = boxExtents.X * Abs(f2.Y) + boxExtents.Y * Abs(f2.X);
        if (Max3(p0, p1, p2) < -r || Min3(p0, p1, p2) > r) return false;

        return true;
    }


    [SkipLocalsInit]
    private static bool AxesIntersectTriangleBox_old(
        in Vector3 v0, in Vector3 v1, in Vector3 v2,
        in Vector3 boxExtents,
        in Vector3 f0, in Vector3 f1, in Vector3 f2)
    {
        float r;

        ReadOnlySpan<Vector3> axes =
        [
            new(0, -f0.Z, f0.Y),
            new(0, -f1.Z, f1.Y),
            new(0, -f2.Z, f2.Y),

            new(f0.Z, 0, -f0.X),
            new(f1.Z, 0, -f1.X),
            new(f2.Z, 0, -f2.X),

            new(-f0.Y, f0.X, 0),
            new(-f1.Y, f1.X, 0),
            new(-f2.Y, f2.X, 0)
        ];

        for (int i = 0; i < axes.Length; i++)
        {
            Vector3 axis = axes[i];

            float p0 = Dot(v0, axis);
            float p1 = Dot(v1, axis);
            float p2 = Dot(v2, axis);

            r =
                (boxExtents.X * Abs(axis.X)) +
                (boxExtents.Y * Abs(axis.Y)) +
                (boxExtents.Z * Abs(axis.Z));

            if (Max(-Max3(p0, p1, p2), Min3(p0, p1, p2)) > r)
            {
                return false;
            }
        }

        return true;
    }




    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Min3(float a, float b, float c)
    {
        return Min(a, Min(b, c));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Max3(float a, float b, float c)
    {
        return Max(a, Max(b, c));
    }
}
