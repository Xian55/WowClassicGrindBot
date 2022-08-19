/*
 *  Part of PPather
 *  Copyright Pontus Borg 2008
 *
 */

using System.Numerics;

using Microsoft.Extensions.Logging;

using PPather.Triangles.Data;

namespace WowTriangles
{
    /// <summary>
    ///
    /// </summary>
    public class TriangleCollection
    {
        private readonly ILogger logger;
        private readonly TrioArray<float> vertices;
        private readonly QuadArray<int> triangles;

        private TriangleMatrix matrix;

        public int LRU;
        public int grid_x;
        public int grid_y;

        private Vector3 max = new(-1E30f, -1E30f, -1E30f);
        public Vector3 Max => max;

        private Vector3 min = new(1E30f, 1E30f, 1E30f);
        public Vector3 Min => min;

        private Vector3 limit_max = new(1E30f, 1E30f, 1E30f);
        private Vector3 limit_min = new(-1E30f, -1E30f, -1E30f);

        private int triangleCount;
        public int TriangleCount => triangleCount;

        private int vertexCount;
        public int VertexCount => vertexCount;

        public TriangleCollection(ILogger logger)
        {
            this.logger = logger;

            vertices = new TrioArray<float>();
            triangles = new QuadArray<int>();
        }

        public bool HasTriangleMatrix => matrix != null;

        public void Clear()
        {
            vertices.SetSize(0);
            triangles.SetSize(0);

            triangleCount = 0;
            vertexCount = 0;
        }

        public TriangleMatrix GetTriangleMatrix()
        {
            if (matrix == null)
                matrix = new TriangleMatrix(this, logger);

            return matrix;
        }

        public void CompactVertices()
        {
            bool[] used_indices = new bool[vertexCount];
            int[] old_to_new = new int[vertexCount];

            // check what vertives are used
            for (int i = 0; i < triangleCount; i++)
            {
                GetTriangle(i, out int v0, out int v1, out int v2);
                used_indices[v0] = true;
                used_indices[v1] = true;
                used_indices[v2] = true;
            }

            // figure out new indices and move
            int sum = 0;
            for (int i = 0; i < used_indices.Length; i++)
            {
                if (used_indices[i])
                {
                    old_to_new[i] = sum;
                    vertices.Get(i, out float x, out float y, out float z);
                    vertices.Set(sum, x, y, z);
                    sum++;
                }
                else
                {
                    old_to_new[i] = -1;
                }
            }

            vertices.SetSize(sum);

            // Change all triangles
            for (int i = 0; i < triangleCount; i++)
            {
                GetTriangle(i, out int v0, out int v1, out int v2, out int flags, out int sequence);
                triangles.Set(i, old_to_new[v0], old_to_new[v1], old_to_new[v2], flags, sequence);
            }
            vertexCount = sum;
        }

        public void SetLimits(float min_x, float min_y, float min_z,
                              float max_x, float max_y, float max_z)
        {
            limit_max = new(max_x, max_y, max_z);
            limit_min = new(min_x, min_y, min_z);
        }

        public int AddVertex(float x, float y, float z)
        {
            vertices.Set(vertexCount, x, y, z);
            return vertexCount++;
        }

        // big list if triangles (3 vertice IDs per triangle)
        public int AddTriangle(int v0, int v1, int v2, int flags, int sequence)
        {
            // check limits
            if (!CheckVertexLimits(v0) &&
                !CheckVertexLimits(v1) &&
                !CheckVertexLimits(v2))
                return -1;

            // Create new
            SetMinMax(v0);
            SetMinMax(v1);
            SetMinMax(v2);

            triangles.Set(triangleCount, v0, v1, v2, flags, sequence);
            return triangleCount++;
        }

        // big list if triangles (3 vertice IDs per triangle)
        public int AddTriangle(int v0, int v1, int v2)
        {
            return AddTriangle(v0, v1, v2, 0, 0);
        }

        private void SetMinMax(int v)
        {
            GetVertex(v, out float x, out float y, out float z);
            if (x < min.X)
                min.X = x;
            if (y < min.Y)
                min.Y = y;
            if (z < min.Z)
                min.Z = z;

            if (x > max.X)
                max.X = x;
            if (y > max.Y)
                max.Y = y;
            if (z > max.Z)
                max.Z = z;
        }

        private bool CheckVertexLimits(int v)
        {
            GetVertex(v, out float x, out float y, out float z);

            if (x < limit_min.X || x > limit_max.X ||
               y < limit_min.Y || y > limit_max.Y ||
               z < limit_min.Z || z > limit_max.Z)
                return false;

            return true;
        }


        public void GetVertex(int i, out float x, out float y, out float z)
        {
            vertices.Get(i, out x, out y, out z);
        }

        public void GetTriangle(int i, out int v0, out int v1, out int v2)
        {
            triangles.Get(i, out v0, out v1, out v2, out _, out _);
        }

        public void GetTriangle(int i, out int v0, out int v1, out int v2, out int flags, out int sequence)
        {
            triangles.Get(i, out v0, out v1, out v2, out flags, out sequence);
        }

        public void GetTriangleVertices(int i,
                                        out float x0, out float y0, out float z0,
                                        out float x1, out float y1, out float z1,
                                        out float x2, out float y2, out float z2, out int flags, out int sequence)
        {
            triangles.Get(i, out int v0, out int v1, out int v2, out flags, out sequence);
            vertices.Get(v0, out x0, out y0, out z0);
            vertices.Get(v1, out x1, out y1, out z1);
            vertices.Get(v2, out x2, out y2, out z2);
        }

        public void GetTriangleVertices(int i,
                                        out float x0, out float y0, out float z0,
                                        out float x1, out float y1, out float z1,
                                        out float x2, out float y2, out float z2)
        {
            triangles.Get(i, out int v0, out int v1, out int v2, out _, out _);
            vertices.Get(v0, out x0, out y0, out z0);
            vertices.Get(v1, out x1, out y1, out z1);
            vertices.Get(v2, out x2, out y2, out z2);
        }

        public float[] GetFlatVertices()
        {
            float[] flat = new float[VertexCount * 3];
            for (int i = 0; i < VertexCount; i++)
            {
                int off = i * 3;
                vertices.Get(i, out flat[off], out flat[off + 1], out flat[off + 2]);
            }
            return flat;
        }

        public void AddAllTrianglesFrom(TriangleCollection set)
        {
            for (int i = 0; i < set.TriangleCount; i++)
            {
                set.GetTriangleVertices(i,
                    out float v0x, out float v0y, out float v0z,
                    out float v1x, out float v1y, out float v1z,
                    out float v2x, out float v2y, out float v2z);

                int v0 = AddVertex(v0x, v0y, v0z);
                int v1 = AddVertex(v1x, v1y, v1z);
                int v2 = AddVertex(v2x, v2y, v2z);
                AddTriangle(v0, v1, v2);
            }
        }
    }
}