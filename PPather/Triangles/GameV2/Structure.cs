using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Globalization;
using System.IO;

namespace PPather.Triangles.GameV2;

public sealed class Structure
{
    // A private object used for locking.
    private readonly Lock _mutex = new();
    public Lock Mutex => _mutex;

    // Lists to hold vertex, triangle, and triangle area data.
    public List<Vector3> Verts { get; private set; } = new(37120);
    public List<Tri> Tris { get; private set; } = new(65536);
    public List<TriAreaId> TriTypes { get; private set; } = new(65536);

    // Bounding box arrays (min and max, length 3).
    public Vector3 bbMin { get; private set; }// = [0.0f, 0.0f, 0.0f];
    public Vector3 bbMax { get; private set; }// = [0.0f, 0.0f, 0.0f];

    /// <summary>
    /// Returns a flat array of floats representing the vertex coordinates.
    /// </summary>
    public float[] GetVertsFlat()
    {
        float[] flat = new float[Verts.Count * 3];
        for (int i = 0; i < Verts.Count; i++)
        {
            flat[i * 3] = Verts[i].X;
            flat[i * 3 + 1] = Verts[i].Y;
            flat[i * 3 + 2] = Verts[i].Z;
        }
        return flat;
    }

    /// <summary>
    /// Returns a flat array of ints representing the triangle indices.
    /// </summary>
    public int[] GetTrisFlat()
    {
        int[] flat = new int[Tris.Count * 3];
        for (int i = 0; i < Tris.Count; i++)
        {
            flat[i * 3] = Tris[i].a;
            flat[i * 3 + 1] = Tris[i].b;
            flat[i * 3 + 2] = Tris[i].c;
        }
        return flat;
    }

    /// <summary>
    /// Returns an array of area IDs as bytes. (Assumes TriAreaId’s underlying type can be cast to byte.)
    /// </summary>
    public byte[] GetAreaIds()
    {
        byte[] areaIds = new byte[TriTypes.Count];
        for (int i = 0; i < TriTypes.Count; i++)
        {
            areaIds[i] = (byte)TriTypes[i];
        }
        return areaIds;
    }

    /// <summary>
    /// Adds a vertex to the structure.
    /// </summary>
    public void AddVert(Vector3 vert)
    {
        lock (_mutex)
        {
            Verts.Add(vert);
        }
    }

    /// <summary>
    /// Adds a triangle and its associated area ID.
    /// </summary>
    public void AddTri(Tri tri, TriAreaId t)
    {
        lock (_mutex)
        {
            Tris.Add(tri);
            TriTypes.Add(t);
        }
    }

    /// <summary>
    /// Appends the contents of another structure to this one.
    /// Triangle vertex indices are offset by the current vertex count.
    /// </summary>
    public void Append(Structure other)
    {
        lock (_mutex)
        {
            int triOffset = Verts.Count;
            Verts.AddRange(other.Verts);
            for (int i = 0; i < other.Tris.Count; i++)
            {
                Tri t = other.Tris[i];
                // Offset each index by triOffset.
                Tri offsetTri = new Tri(t.a + triOffset, t.b + triOffset, t.c + triOffset);
                Tris.Add(offsetTri);
                TriTypes.Add(other.TriTypes[i]);
            }
        }
    }

    /// <summary>
    /// Removes unused and duplicate vertices and triangles.
    /// </summary>
    public void Clean()
    {
        lock (_mutex)
        {
            // Mark vertices used by any triangle.
            bool[] isVertexUsed = new bool[Verts.Count];
            foreach (var tri in Tris)
            {
                isVertexUsed[tri.a] = true;
                isVertexUsed[tri.b] = true;
                isVertexUsed[tri.c] = true;
            }

            // Build a dictionary of unique vertices.
            Dictionary<Vector3, int> uniqueVerticesMap = new Dictionary<Vector3, int>();
            List<Vector3> filteredVertices = new List<Vector3>();
            int[] vertexIndexMap = new int[Verts.Count];
            int newIndex = 0;
            for (int i = 0; i < Verts.Count; i++)
            {
                if (isVertexUsed[i])
                {
                    Vector3 v = Verts[i];
                    if (!uniqueVerticesMap.TryGetValue(v, out int index))
                    {
                        uniqueVerticesMap[v] = newIndex;
                        filteredVertices.Add(v);
                        vertexIndexMap[i] = newIndex;
                        newIndex++;
                    }
                    else
                    {
                        vertexIndexMap[i] = index;
                    }
                }
            }

            // Update triangle indices.
            for (int i = 0; i < Tris.Count; i++)
            {
                Tri tri = Tris[i];

                Tris[i] = new(
                    vertexIndexMap[tri.a],
                    vertexIndexMap[tri.b],
                    vertexIndexMap[tri.c]
                );
            }

            // Remove duplicate triangles.
            HashSet<Tri> uniqueTriSet = new HashSet<Tri>();
            List<Tri> filteredTris = new List<Tri>();
            List<TriAreaId> filteredTriTypes = new List<TriAreaId>();

            for (int i = 0; i < Tris.Count; i++)
            {
                Tri tri = Tris[i];
                if (uniqueTriSet.Add(tri))
                {
                    filteredTris.Add(tri);
                    filteredTriTypes.Add(TriTypes[i]);
                }
            }

            // Replace with the filtered lists.
            Verts = filteredVertices;
            Tris = filteredTris;
            TriTypes = filteredTriTypes;
        }
    }

    /// <summary>
    /// Removes vertices (and associated triangles) that fall outside the given bounds.
    /// bmin and bmax should be arrays of length 3.
    /// </summary>
    public void CleanOutOfBounds(float[] bmin, float[] bmax)
    {
        if (bmin == null || bmax == null || bmin.Length < 3 || bmax.Length < 3)
            throw new ArgumentException("Bounds arrays must have at least three elements.");

        lock (_mutex)
        {
            List<Vector3> filteredVertices = new List<Vector3>();
            int[] vertexIndexMapping = new int[Verts.Count];
            for (int i = 0; i < Verts.Count; i++)
            {
                Vector3 vertex = Verts[i];
                if (vertex.X >= bmin[0] && vertex.X <= bmax[0] &&
                    vertex.Y >= bmin[1] && vertex.Y <= bmax[1] &&
                    vertex.Z >= bmin[2] && vertex.Z <= bmax[2])
                {
                    vertexIndexMapping[i] = filteredVertices.Count;
                    filteredVertices.Add(vertex);
                }
                else
                {
                    vertexIndexMapping[i] = -1;
                }
            }

            List<Tri> filteredTriangles = new List<Tri>();
            for (int i = 0; i < Tris.Count; i++)
            {
                Tri tri = Tris[i];
                int v1 = vertexIndexMapping[tri.a];
                int v2 = vertexIndexMapping[tri.b];
                int v3 = vertexIndexMapping[tri.c];
                if (v1 != -1 && v2 != -1 && v3 != -1)
                {
                    filteredTriangles.Add(new Tri(v1, v2, v3));
                }
            }

            Verts = filteredVertices;
            Tris = filteredTriangles;
        }
    }

    /// <summary>
    /// Exports the structure to an OBJ file for debugging.
    /// </summary>
    public void ExportDebugObjFile(string filePath)
    {
        // Use InvariantCulture for consistent number formatting.
        CultureInfo ci = CultureInfo.InvariantCulture;
        using StreamWriter writer = new(filePath);

        foreach (var v in Verts)
        {
            writer.WriteLine($"v {v.X.ToString("F8", ci)} {v.Y.ToString("F8", ci)} {v.Z.ToString("F8", ci)}");
        }

        // OBJ files are 1-indexed.
        foreach (var tri in Tris)
        {
            writer.WriteLine($"f {tri.a + 1} {tri.b + 1} {tri.c + 1}");
        }
    }
}
