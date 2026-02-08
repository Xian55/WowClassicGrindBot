/*
 *  Part of PPather
 *  Copyright Pontus Borg 2008
 *
 */

using Microsoft.Extensions.Logging;

using PPather.Triangles.Data;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using static System.Math;

using static WowTriangles.Utils;

namespace WowTriangles;

public sealed class TriangleMatrix
{
    private const float resolution = 8.0f;
    private const float halfResoltion = resolution / 2f;

    private const int CellCapacity = 4096;
    private const int ACount = 128;
    private const int MinElementBufferSize = 256;

    [ThreadStatic] private static int[]? t_elementBuffer;

    private readonly SparseFloatMatrix2D<List<int>> matrix = new(resolution, CellCapacity);

    public int Count => matrix.Count;

    [SkipLocalsInit]
    public TriangleMatrix(TriangleCollection tc)
    {
        int triangleCount = tc.TriangleCount;
        var LocalDict = new ThreadLocal<Dictionary<int, List<int>>>(() => new Dictionary<int, List<int>>(ACount), true);
        var m = matrix;

        _ = Parallel.For(0, triangleCount, index =>
        {
            var tSpan = tc.TrianglesSpan;
            var vSpan = tc.VerteciesSpan;

            TriangleCollection.GetTriangleVertices(tSpan, vSpan, index,
                out Vector3 v0,
                out Vector3 v1,
                out Vector3 v2,
                out _);

            float minx = Min3(v0.X, v1.X, v2.X);
            float maxx = Max3(v0.X, v1.X, v2.X);
            float miny = Min3(v0.Y, v1.Y, v2.Y);
            float maxy = Max3(v0.Y, v1.Y, v2.Y);

            int startx = m.LocalToGrid(minx);
            int endx = m.LocalToGrid(maxx);
            int starty = m.LocalToGrid(miny);
            int endy = m.LocalToGrid(maxy);

            const int BatchSize = 8;
            int cellCount = (endx - startx + 1) * (endy - starty + 1);
            Span<float> gridXs = stackalloc float[cellCount];
            Span<float> gridYs = stackalloc float[cellCount];

            int idx = 0;
            for (int x = startx; x <= endx; x++)
            {
                for (int y = starty; y <= endy; y++)
                {
                    gridXs[idx] = m.GridToLocal(x) + halfResoltion;
                    gridYs[idx] = m.GridToLocal(y) + halfResoltion;
                    idx++;
                }
            }

            var localDict = LocalDict.Value;
            Vector3 box_halfsize = new(halfResoltion, halfResoltion, 1E6f);

            // Prepare batch of box centers
            Span<Vector3> boxCenters = stackalloc Vector3[BatchSize];
            Span<int> keys = stackalloc int[BatchSize];

            for (int i = 0; i < cellCount; i += BatchSize)
            {
                int batchLen = Math.Min(BatchSize, cellCount - i);

                for (int j = 0; j < batchLen; j++)
                {
                    boxCenters[j] = new Vector3(gridXs[i + j], gridYs[i + j], 0);
                    keys[j] = m.GetKey(gridXs[i + j] - halfResoltion, gridYs[i + j] - halfResoltion);
                }

                for (int j = 0; j < batchLen; j++)
                {
                    if (!TriangleBoxIntersect_SIMD(v0, v1, v2, boxCenters[j], box_halfsize))
                        continue;

                    ref List<int> list = ref CollectionsMarshal.GetValueRefOrAddDefault(localDict, keys[j], out bool exists);
                    if (!exists)
                    {
                        localDict[keys[j]] = list = new List<int>(ACount);
                    }
                    list.Add(index);
                }
            }
        });

        foreach (var localDict in LocalDict.Values)
        {
            foreach (var kvp in localDict)
            {
                ref List<int> list = ref CollectionsMarshal.GetValueRefOrAddDefault(m.Dict, kvp.Key, out bool exists);
                if (!exists)
                {
                    list = kvp.Value;
                    m.Add(kvp.Key, list);
                }
                else
                {
                    list.AddRange(kvp.Value);
                }
            }
        }

        LocalDict.Dispose();
    }

    public void Clear()
    {
        foreach (List<int> list in matrix.GetAllElements())
        {
            list.Clear();
        }

        matrix.Clear();
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<int> GetAllCloseTo(float x, float y, float range)
    {
        int collectionSize = matrix.CalculateSize(x - range, y - range, x + range, y + range);

        var collectionPooler = ArrayPool<List<int>>.Shared;
        List<int>[] collection = collectionPooler.Rent(collectionSize);
        Memory<List<int>> collectionMem = collection.AsMemory();

        (int collectionCount, int totalSize) = matrix.GetAllInSquare(collectionMem, x - range, y - range, x + range, y + range);

        if (t_elementBuffer == null || t_elementBuffer.Length < totalSize)
            t_elementBuffer = new int[Max(totalSize, MinElementBufferSize)];

        Span<int> outputSpan = t_elementBuffer.AsSpan();

        int c = 0;
        for (int i = 0; i < collectionCount; i++)
        {
            ReadOnlySpan<int> fromSpan = CollectionsMarshal.AsSpan(collectionMem.Span[i]);
            fromSpan.CopyTo(outputSpan.Slice(c, fromSpan.Length));
            c += fromSpan.Length;
        }

        collectionPooler.Return(collection);

        return outputSpan[..totalSize];
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<int> GetAllInSquare(float x0, float y0, float x1, float y1)
    {
        int collectionSize = matrix.CalculateSize(x0, y0, x1, y1);

        var collectionPooler = ArrayPool<List<int>>.Shared;
        List<int>[] collection = collectionPooler.Rent(collectionSize);
        Memory<List<int>> collectionMem = collection.AsMemory();

        (int collectionCount, int totalSize) = matrix.GetAllInSquare(collectionMem, x0, y0, x1, y1);

        if (t_elementBuffer == null || t_elementBuffer.Length < totalSize)
            t_elementBuffer = new int[Max(totalSize, MinElementBufferSize)];

        Span<int> outputSpan = t_elementBuffer.AsSpan();

        int c = 0;
        for (int i = 0; i < collectionCount; i++)
        {
            ReadOnlySpan<int> fromSpan = CollectionsMarshal.AsSpan(collectionMem.Span[i]);
            fromSpan.CopyTo(outputSpan.Slice(c, fromSpan.Length));
            c += fromSpan.Length;
        }

        collectionPooler.Return(collection);

        return outputSpan[..totalSize];
    }
}