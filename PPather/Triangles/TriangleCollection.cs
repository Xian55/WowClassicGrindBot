/*
 *  Part of PPather
 *  Copyright Pontus Borg 2008
 *
 */

using Microsoft.Extensions.Logging;

using PPather;
using PPather.Triangles;

using SharedLib.Extensions;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace WowTriangles;

/// <summary>
///
/// </summary>
public sealed class TriangleCollection
{
    private readonly ILogger logger;
    private readonly List<Vector3> vertecies;
    private readonly List<Triangle<int>> triangles;

    public List<Vector3> Vertecies => vertecies;
    public List<Triangle<int>> Triangles => triangles;

    public ReadOnlySpan<Vector3> VerteciesSpan => CollectionsMarshal.AsSpan(vertecies);
    public ReadOnlySpan<Triangle<int>> TrianglesSpan => CollectionsMarshal.AsSpan(triangles);

    private TriangleMatrix matrix;

    public int LRU;

    private Vector3 max = new(-1E30f, -1E30f, -1E30f);
    public Vector3 Max => max;

    private Vector3 min = new(1E30f, 1E30f, 1E30f);
    public Vector3 Min => min;

    private Vector3 limit_max = new(1E30f, 1E30f, 1E30f);
    private Vector3 limit_min = new(-1E30f, -1E30f, -1E30f);

    private int triangleCount;
    public int TriangleCount => triangles.Count;

    public int VertexCount { get; private set; }

    public TriangleCollection(ILogger logger)
    {
        this.logger = logger;
        // `2 ^ 16` is XOR in C#, so this asked for capacity 18 rather than the
        // intended 65536 and then regrew ~12 times (copying the whole vertex
        // buffer, repeatedly into the LOH) on every chunk load.
        vertecies = new(1 << 16); // terrain mesh
        triangles = new(1 << 16);
    }

    public void Clear()
    {
        triangleCount = 0;
        VertexCount = 0;

        triangles.Clear();
        vertecies.Clear();
        // Lazily created by GetTriangleMatrix - chunks that were only ever
        // read as raw triangle soup (e.g. navmesh tile extraction) never
        // materialize it.
        matrix?.Clear();
    }

    public TriangleMatrix GetTriangleMatrix()
    {
        if (matrix != null)
        {
            return matrix;
        }

        long start = Stopwatch.GetTimestamp();

        matrix = new TriangleMatrix(this);

        if (logger.IsEnabled(LogLevel.Trace))
        {
            var end = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

            logger.LogTrace("Mesh [||,||] Bounds: [{MinX:F4}, {MinY:F4}] [{MaxX:F4}, {MaxY:F4}] - {TriangleCount} tri - {VertexCount} ver - {MatrixCount} c - {ElapsedMs}ms",
                Min.X, Min.Y, Max.X, Max.Y, TriangleCount, VertexCount, matrix.Count, end);
        }

        return matrix;
    }

    public void SetLimits(float min_x, float min_y, float min_z,
                          float max_x, float max_y, float max_z)
    {
        limit_max = new(max_x, max_y, max_z);
        limit_min = new(min_x, min_y, min_z);
    }



    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int AddVertex(float x, float y, float z)
    {
        VerticesSet(VertexCount, x, y, z);
        return VertexCount++;
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddTriangle(int v0, int v1, int v2, TriangleType flags)
    {
        VerticesGet(VerteciesSpan, v0, out Vector3 vv0);
        VerticesGet(VerteciesSpan, v1, out Vector3 vv1);
        VerticesGet(VerteciesSpan, v2, out Vector3 vv2);

        // check limits
        if (!CheckVertexLimits(vv0) &&
            !CheckVertexLimits(vv1) &&
            !CheckVertexLimits(vv2))
            return;

        // Create new
        SetMinMax(ref min, ref max, vv0);
        SetMinMax(ref min, ref max, vv1);
        SetMinMax(ref min, ref max, vv2);

        TrianglesSet(triangleCount, v0, v1, v2, flags);
        triangleCount++;
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetMinMax(ref Vector3 min, ref Vector3 max, in Vector3 v)
    {
        min = Vector3.Min(min, v);
        max = Vector3.Max(max, v);
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool CheckVertexLimits(in Vector3 v)
    {
        bool withinX = v.X >= limit_min.X && v.X <= limit_max.X;
        bool withinY = v.Y >= limit_min.Y && v.Y <= limit_max.Y;
        bool withinZ = v.Z >= limit_min.Z && v.Z <= limit_max.Z;
        return withinX && withinY && withinZ;
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void GetTriangle(
        in ReadOnlySpan<Triangle<int>> span,
        int i,
        out int v0, out int v1, out int v2, out TriangleType flags)
    {
        TrianglesGet(span, i, out v0, out v1, out v2, out flags);
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void GetTriangleVertices(
        in ReadOnlySpan<Triangle<int>> tSpan,
        in ReadOnlySpan<Vector3> vSpan,
        int triangleIndex,
        out Vector3 v0, out Vector3 v1, out Vector3 v2, out TriangleType flags)
    {
        TrianglesGet(tSpan, triangleIndex, out int vi0, out int vi1, out int vi2, out flags);

        VerticesGet(vSpan, vi0, out v0);
        VerticesGet(vSpan, vi1, out v1);
        VerticesGet(vSpan, vi2, out v2);
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void VerticesGet(in ReadOnlySpan<Vector3> span, int index, out Vector3 vertex)
    {
        vertex = Unsafe.Add(ref MemoryMarshal.GetReference(span), index);
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void VerticesSet(int index, float x, float y, float z)
    {
        // index is always VertexCount, i.e. an append; Add has an inlineable
        // fast path where Insert does not.
        vertecies.Add(new(x, y, z));
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void TrianglesGet(
        in ReadOnlySpan<Triangle<int>> span,
        int index, out int v0, out int v1, out int v2, out TriangleType flags)
    {
        (v0, v1, v2, flags) = Unsafe.Add(ref MemoryMarshal.GetReference(span), index);
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TrianglesSet(int index, int v0, int v1, int v2, TriangleType flags)
    {
        triangles.Add(new(v0, v1, v2, flags));
    }
}