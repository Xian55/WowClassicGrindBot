/*
  This file is part of ppather.

    PPather is free software: you can redistribute it and/or modify
    it under the terms of the GNU Lesser General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    PPather is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Lesser General Public License for more details.

    You should have received a copy of the GNU Lesser General Public License
    along with ppather.  If not, see <http://www.gnu.org/licenses/>.

    Copyright Pontus Borg 2008

 */

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

using static System.MathF;
using static System.Numerics.Vector3;

namespace WowTriangles;

public static class Utils
{
    private const float ParallelEpsilon = 1e-6f;
    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool SegmentTriangleIntersect(
        in Vector3 p0, in Vector3 p1,
        in Vector3 t0, in Vector3 t1, in Vector3 t2,
        out Vector3 I)
    {
        // Segment direction
        Vector3 dir = p1 - p0;

        // Triangle edges
        Vector3 e1 = t1 - t0;
        Vector3 e2 = t2 - t0;

        // Begin calculating determinant
        Vector3 pvec = Cross(dir, e2);
        float det = Dot(e1, pvec);

        // If determinant is near zero → ray is parallel to triangle plane
        if (Abs(det) < ParallelEpsilon)
        {
            I = default;
            return false;
        }

        float invDet = 1.0f / det;

        // Calculate distance from t0 to ray origin
        Vector3 tvec = p0 - t0;

        // Calculate u parameter and test bounds
        float u = Dot(tvec, pvec) * invDet;
        if (u is < 0.0f or > 1.0f)
        {
            I = default;
            return false;
        }

        // Prepare to test v parameter
        Vector3 qvec = Cross(tvec, e1);

        float v = Dot(dir, qvec) * invDet;
        if (v < 0.0f || u + v > 1.0f)
        {
            I = default;
            return false;
        }

        // At this stage we know the line intersects the triangle
        float t = Dot(e2, qvec) * invDet;

        // For segment intersection: require t ∈ [0,1]
        if (t is < 0.0f or > 1.0f)
        {
            I = default;
            return false;
        }

        // Compute intersection point only now (after passing all tests)
        I = p0 + (dir * t);
        return true;
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float PointDistanceToSegment(in Vector3 p0, in Vector3 x1, in Vector3 x2)
    {
        Vector3 L = x2 - x1;           // segment vector
        float l2 = Dot(L, L);          // squared segment length
        Vector3 D = p0 - x1;           // vector from point to x1
        float d = Dot(D, L);           // projection scalar

        // Clamp projection between [0, l2] without branching
        float t = Math.Clamp(d, 0.0f, l2);

        // Compute projection point and distance
        Vector3 proj = D - (L * (t / l2));
        return proj.Length();
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void GetTriangleNormal(
        in Vector3 t0, in Vector3 t1, in Vector3 t2, out Vector3 normal)
    {
        normal = Normalize(Cross(t1 - t0, t2 - t0));
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float PointDistanceToTriangle(
        in Vector3 p0,
        in Vector3 t0, in Vector3 t1, in Vector3 t2)
    {
        Vector3 u = Subtract(t1, t0); // triangle vector 1
        Vector3 v = Subtract(t2, t0); // triangle vector 2
        Vector3 n = Cross(u, v);      // unnormalized triangle normal

        float normalLenSq = Dot(n, n);
        if (normalLenSq >= 1e-12f)
        {
            Vector3 normalDir = n * (1.0f / Sqrt(normalLenSq));
            Vector3 above = p0 + normalDir * 1E6f;
            Vector3 below = p0 - normalDir * 1E6f;

            if (SegmentTriangleIntersect(above, below, t0, t1, t2, out Vector3 intersect))
            {
                return Subtract(intersect, p0).Length();
            }
        }

        float d0 = PointDistanceToSegment(p0, t0, t1);
        float d1 = PointDistanceToSegment(p0, t1, t2);
        float d2 = PointDistanceToSegment(p0, t2, t0);

        return Min3(d0, d1, d2);
    }

    // From the book "Real-Time Collision Detection" by Christer Ericson, page 169
    // See also the published Errata
    // http://realtimecollisiondetection.net/books/rtcd/errata/
    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TriangleBoxIntersect(
        in Vector3 a, in Vector3 b, in Vector3 c,
        in Vector3 boxCenter, in Vector3 boxExtents)
    {
        Vector3 v0 = a - boxCenter;
        Vector3 v1 = b - boxCenter;
        Vector3 v2 = c - boxCenter;

        Vector3 f0 = v1 - v0;
        Vector3 f1 = v2 - v1;
        Vector3 f2 = v0 - v2;

        return
            AxesIntersectTriangleBox(v0, v1, v2, boxExtents, f0, f1, f2) &&
            TriangleVerticesInsideBox(v0, v1, v2, boxExtents) &&
            TrianglePlaneIntersectBox(f0, f1, v0, boxExtents);
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TriangleBoxIntersect_SIMD(
        in Vector3 a, in Vector3 b, in Vector3 c,
        in Vector3 boxCenter, in Vector3 boxExtents)
    {
        // Move triangle to box space
        Vector3 v0 = a - boxCenter;
        Vector3 v1 = b - boxCenter;
        Vector3 v2 = c - boxCenter;

        ReadOnlySpan<Vector3> vs = [v0, v1, v2];
        ReadOnlySpan<Vector3> fs = [v1 - v0, v2 - v1, v0 - v2];

        if (Sse.IsSupported)
        {
            for (int edge = 0; edge < 3; edge++)
            {
                var f = fs[edge];

                // Prepare vectors for v0, v1, v2
                var vx = Vector128.Create(vs[0].X, vs[1].X, vs[2].X, 0f);
                var vy = Vector128.Create(vs[0].Y, vs[1].Y, vs[2].Y, 0f);
                var vz = Vector128.Create(vs[0].Z, vs[1].Z, vs[2].Z, 0f);

                // Axis 1: Z * f.Y - Y * f.Z
                var fY = Vector128.Create(f.Y);
                var fZ = Vector128.Create(f.Z);
                var p = Sse.Subtract(
                    Sse.Multiply(vz, fY),
                    Sse.Multiply(vy, fZ)
                );
                float r = boxExtents.Y * Abs(f.Z) + boxExtents.Z * Abs(f.Y);
                float p0 = p.GetElement(0), p1 = p.GetElement(1), p2 = p.GetElement(2);
                if (Max3(p0, p1, p2) < -r || Min3(p0, p1, p2) > r) return false;

                // Axis 2: X * f.Z - Z * f.X
                var fX = Vector128.Create(f.X);
                p = Sse.Subtract(
                    Sse.Multiply(vx, fZ),
                    Sse.Multiply(vz, fX)
                );
                r = boxExtents.X * Abs(f.Z) + boxExtents.Z * Abs(f.X);
                p0 = p.GetElement(0); p1 = p.GetElement(1); p2 = p.GetElement(2);
                if (Max3(p0, p1, p2) < -r || Min3(p0, p1, p2) > r) return false;

                // Axis 3: Y * f.X - X * f.Y
                p = Sse.Subtract(
                    Sse.Multiply(vy, fX),
                    Sse.Multiply(vx, fY)
                );
                r = boxExtents.X * Abs(f.Y) + boxExtents.Y * Abs(f.X);
                p0 = p.GetElement(0); p1 = p.GetElement(1); p2 = p.GetElement(2);
                if (Max3(p0, p1, p2) < -r || Min3(p0, p1, p2) > r) return false;
            }
        }
        else
        {
            if (!AxesIntersectTriangleBox(v0, v1, v2, boxExtents, fs[0], fs[1], fs[2]))
            {
                return false;
            }
        }

        if (!TriangleVerticesInsideBox(v0, v1, v2, boxExtents))
        {
            return false;
        }

        return TrianglePlaneIntersectBox(fs[0], fs[1], v0, boxExtents);
    }

    [SkipLocalsInit]
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TriangleVerticesInsideBox(
        in Vector3 v0, in Vector3 v1, in Vector3 v2,
        in Vector3 boxExtents)
    {
        return
            !(Max3(v0.X, v1.X, v2.X) < -boxExtents.X || Min3(v0.X, v1.X, v2.X) > boxExtents.X) &&
            !(Max3(v0.Y, v1.Y, v2.Y) < -boxExtents.Y || Min3(v0.Y, v1.Y, v2.Y) > boxExtents.Y) &&
            !(Max3(v0.Z, v1.Z, v2.Z) < -boxExtents.Z || Min3(v0.Z, v1.Z, v2.Z) > boxExtents.Z);
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TrianglePlaneIntersectBox(
        in Vector3 f0, in Vector3 f1,
        in Vector3 v0,
        in Vector3 boxExtents)
    {
        Vector3 planeNormal = Cross(f0, f1);
        float planeDistance = Dot(planeNormal, v0);

        float r =
            (boxExtents.X * Abs(planeNormal.X)) +
            (boxExtents.Y * Abs(planeNormal.Y)) +
            (boxExtents.Z * Abs(planeNormal.Z));

        return planeDistance <= r;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Min3(float a, float b, float c)
    {
        return Min(a, Min(b, c));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Min4(float a, float b, float c, float d)
    {
        return Min(Min(a, b), Min(c, d));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Max3(float a, float b, float c)
    {
        return Max(a, Max(b, c));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Max4(float a, float b, float c, float d)
    {
        return Max(Max(a, b), Max(c, d));
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 ClosestPointOnSegment(in Vector3 p0, in Vector3 x1, in Vector3 x2)
    {
        Vector3 L = x2 - x1;
        float l2 = Dot(L, L);
        if (l2 < 1e-12f)
            return x1;

        float t = Math.Clamp(Dot(p0 - x1, L) / l2, 0.0f, 1.0f);
        return x1 + (L * t);
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 ClosestPointOnTriangle(in Vector3 p0, in Vector3 t0, in Vector3 t1, in Vector3 t2)
    {
        Vector3 u = Subtract(t1, t0);
        Vector3 v = Subtract(t2, t0);
        Vector3 n = Cross(u, v);

        float normalLenSq = Dot(n, n);
        if (normalLenSq >= 1e-12f)
        {
            Vector3 normalDir = n * (1.0f / Sqrt(normalLenSq));
            Vector3 above = p0 + normalDir * 1E6f;
            Vector3 below = p0 - normalDir * 1E6f;

            if (SegmentTriangleIntersect(above, below, t0, t1, t2, out Vector3 intersect))
            {
                return intersect;
            }
        }

        Vector3 c0 = ClosestPointOnSegment(p0, t0, t1);
        Vector3 c1 = ClosestPointOnSegment(p0, t1, t2);
        Vector3 c2 = ClosestPointOnSegment(p0, t2, t0);

        float d0 = Subtract(c0, p0).LengthSquared();
        float d1 = Subtract(c1, p0).LengthSquared();
        float d2 = Subtract(c2, p0).LengthSquared();

        if (d0 <= d1 && d0 <= d2) return c0;
        if (d1 <= d2) return c1;
        return c2;
    }
}