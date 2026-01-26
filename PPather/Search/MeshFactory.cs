using System.Collections.Generic;
using System.Numerics;

using WowTriangles;

namespace PPather;

public static class MeshFactory
{
    public static List<Vector3> CreatePoints(TriangleCollection collection)
    {
        return collection.Vertecies;
    }


    public static int CreateTriangles(TriangleType modelType, TriangleCollection tc, int[] output)
    {
        int c = 0;

        var span = tc.TrianglesSpan;
        for (int i = 0; i < span.Length; i++)
        {
            TriangleCollection.GetTriangle(span, i, out int v0, out int v1, out int v2, out TriangleType flags);
            if (flags != modelType)
                continue;

            output[c++] = v0;
            output[c++] = v1;
            output[c++] = v2;
        }

        return c;
    }
}
