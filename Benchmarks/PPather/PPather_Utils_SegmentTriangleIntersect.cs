using BenchmarkDotNet.Attributes;

using System.Numerics;
using System.Runtime.CompilerServices;

using static System.MathF;
using static System.Numerics.Vector3;

namespace Benchmarks.PPather;

public class PPather_Utils_SegmentTriangleIntersect
{
    private readonly Vector3 p0 = new(0, 0, 0);
    private readonly Vector3 p1 = new(1, 1, 1);

    private readonly Vector3 t0 = new(0, 0, 0);
    private readonly Vector3 t1 = new(1, 0, 0);
    private readonly Vector3 t2 = new(0, 1, 0);


    [Benchmark(Baseline = true)]
    public void Old_SegmentTriangleIntersect()
    {
        _ = SegmentTriangleIntersect_old(
            in p0, in p1,
            in t0, in t1, in t2,
            out _);
    }

    [Benchmark]
    public void New_SegmentTriangleIntersect()
    {
        _ = SegmentTriangleIntersect(
            in p0, in p1,
            in t0, in t1, in t2,
            out _);
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool SegmentTriangleIntersect(
        in Vector3 p0, in Vector3 p1,
        in Vector3 t0, in Vector3 t1, in Vector3 t2,
        out Vector3 I)
    {
        Vector3 u = t1 - t0; // triangle vector 1
        Vector3 v = t2 - t0; // triangle vector 2
        Vector3 n = Cross(u, v); // triangle normal

        Vector3 dir = p1 - p0; // ray direction vector
        Vector3 w0 = p0 - t0;
        float a = -Dot(n, w0);
        float b = Dot(n, dir);

        // Avoid repeating Dot(n, dir)
        if (Abs(b) < float.Epsilon)
        {
            I = default;
            return false; // parallel
        }

        // get intersect point of ray with triangle plane
        float r = a / b;
        if (r < 0.0f || r > 1.0f)
        {
            I = default;
            return false; // outside of segment bounds
        }

        I = p0 + (dir * r); // intersect point of line and plane

        // Avoid re-calculating things by merging conditions
        float uu = Dot(u, u);
        float uv = Dot(u, v);
        float vv = Dot(v, v);
        Vector3 w = I - t0;
        float wu = Dot(w, u);
        float wv = Dot(w, v);
        float D = uv * uv - uu * vv;

        // Parametric coordinates test
        float s = (uv * wv - vv * wu) / D;
        if (s < 0.0f || s > 1.0f) return false;

        float t = (uv * wu - uu * wv) / D;
        return !(t < 0.0f || (s + t) > 1.0f);
    }


    [SkipLocalsInit]
    public static bool SegmentTriangleIntersect_old(
        in Vector3 p0, in Vector3 p1,
        in Vector3 t0, in Vector3 t1, in Vector3 t2,
        out Vector3 I)
    {
        Vector3 u = Subtract(t1, t0); // triangle vector 1
        Vector3 v = Subtract(t2, t0); // triangle vector 2
        Vector3 n = Cross(u, v); // triangle normal

        Vector3 dir = Subtract(p1, p0); // ray direction vector
        Vector3 w0 = Subtract(p0, t0);
        float a = -Dot(n, w0);
        float b = Dot(n, dir);
        if (Abs(b) < float.Epsilon)
        {
            I = default;
            return false; // parallel
        }

        // get intersect point of ray with triangle plane
        float r = a / b;
        if (r < 0.0f)
        {
            I = default;
            return false; // "before" p0
        }
        if (r > 1.0f)
        {
            I = default;
            return false; // "after" p1
        }

        Vector3 M = Multiply(dir, r);
        I = Add(p0, M);// intersect point of line and plane

        // is I inside T?
        float uu = Dot(u, u);
        float uv = Dot(u, v);
        float vv = Dot(v, v);
        Vector3 w = Subtract(I, t0);
        float wu = Dot(w, u);
        float wv = Dot(w, v);
        float D = uv * uv - uu * vv;

        // get and test parametric coords
        float s = (uv * wv - vv * wu) / D;
        if (s < 0.0f || s > 1.0f)        // I is outside T
            return false;

        float t = (uv * wu - uu * wv) / D;
        if (t < 0.0f || (s + t) > 1.0f)  // I is outside T
            return false;

        return true;
    }
}
