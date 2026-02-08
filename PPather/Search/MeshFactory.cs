using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using PPather.Graph;

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

    // ==================== NEW: Sparse Vertex Loading ====================

    /// <summary>
    /// VARIANT 1: Create mesh from actual discovered spots (most precise).
    /// Only includes vertices from triangles that spots are standing on.
    /// Perfect for visualizing the "explored" mesh.
    /// </summary>
    public static (List<Vector3> vertices, List<int> indices) CreateSparseFromSpots(
        TriangleCollection tc,
        IEnumerable<Spot> spots,
        float toonHeight,
        float toonSize)
    {
        HashSet<int> usedVertices = new();
        List<int> triangleIndices = new();

        var tSpan = tc.TrianglesSpan;
        var vSpan = tc.VerteciesSpan;

        // For each spot, find the triangle(s) it's standing on
        foreach (Spot spot in spots)
        {
            if (spot.IsBlocked())
                continue;

            Vector3 spotLoc = spot.Loc;
            Vector3 searchStart = new(spotLoc.X, spotLoc.Y, spotLoc.Z - 0.1f);
            Vector3 searchEnd = new(spotLoc.X, spotLoc.Y, spotLoc.Z + toonHeight);

            // Find triangles this spot is on
            for (int i = 0; i < tSpan.Length; i++)
            {
                TriangleCollection.GetTriangleVertices(tSpan, vSpan, i,
                    out Vector3 v0, out Vector3 v1, out Vector3 v2, out TriangleType flags);

                if (flags != TriangleType.Terrain && flags != TriangleType.Object)
                    continue;

                // Check if spot is on this triangle
                if (WowTriangles.Utils.SegmentTriangleIntersect(searchStart, searchEnd, v0, v1, v2, out _))
                {
                    TriangleCollection.GetTriangle(tSpan, i, out int idx0, out int idx1, out int idx2, out _);

                    usedVertices.Add(idx0);
                    usedVertices.Add(idx1);
                    usedVertices.Add(idx2);

                    triangleIndices.Add(idx0);
                    triangleIndices.Add(idx1);
                    triangleIndices.Add(idx2);
                }
            }
        }

        // Build compact vertex list (only used vertices)
        var sortedIndices = usedVertices.OrderBy(x => x).ToList();
        Dictionary<int, int> oldToNew = new();
        List<Vector3> compactVertices = new(sortedIndices.Count);

        for (int i = 0; i < sortedIndices.Count; i++)
        {
            int oldIndex = sortedIndices[i];
            oldToNew[oldIndex] = i;
            compactVertices.Add(vSpan[oldIndex]);
        }

        // Remap triangle indices to compact vertex list
        for (int i = 0; i < triangleIndices.Count; i++)
        {
            triangleIndices[i] = oldToNew[triangleIndices[i]];
        }

        return (compactVertices, triangleIndices);
    }

    /// <summary>
    /// VARIANT 2: Create mesh filtered by walkability test.
    /// Pre-filters vertices by testing if they're standable.
    /// Good for visualizing "walkable" areas without pathfinding.
    ///
    /// OPTIMIZED: Only tests vertices near explored spots (if spots provided).
    /// </summary>
    public static (List<Vector3> vertices, List<int> indices) CreateWalkableOnly(
        TriangleCollection tc,
        ChunkedTriangleCollection triangleWorld,
        float toonHeight,
        float toonSize,
        IEnumerable<Spot> nearbySpots = null,
        float searchRadius = 20.0f)
    {
        HashSet<int> walkableVertices = new();
        List<int> triangleIndices = new();

        var tSpan = tc.TrianglesSpan;
        var vSpan = tc.VerteciesSpan;

        // OPTIMIZATION: If spots provided, only test vertices near them
        bool hasSpots = nearbySpots != null && nearbySpots.Any();
        HashSet<Vector3> spotLocations = hasSpots
            ? new HashSet<Vector3>(nearbySpots.Select(s => s.Loc))
            : null;

        // Test each vertex for walkability
        for (int i = 0; i < vSpan.Length; i++)
        {
            Vector3 vertex = vSpan[i];

            // OPTIMIZATION: Skip vertices far from explored spots
            if (hasSpots && !IsNearAnySpot(vertex, spotLocations, searchRadius))
            {
                continue; // Skip this vertex - too far from explored areas
            }

            // Test if this vertex position is standable
            if (triangleWorld.FindStandableAt(
                vertex.X, vertex.Y,
                vertex.Z - 2.0f, vertex.Z + 2.0f,
                out float standZ, out TriangleType flags,
                toonHeight, toonSize))
            {
                // Only include if close to actual vertex height (not far above/below)
                if (Math.Abs(standZ - vertex.Z) < 5.0f)
                {
                    walkableVertices.Add(i);
                }
            }
        }

        // Build triangles using only walkable vertices
        for (int i = 0; i < tSpan.Length; i++)
        {
            TriangleCollection.GetTriangle(tSpan, i, out int v0, out int v1, out int v2, out TriangleType flags);

            if (flags != TriangleType.Terrain && flags != TriangleType.Object)
                continue;

            // Only include triangle if ALL vertices are walkable
            if (walkableVertices.Contains(v0) && walkableVertices.Contains(v1) && walkableVertices.Contains(v2))
            {
                triangleIndices.Add(v0);
                triangleIndices.Add(v1);
                triangleIndices.Add(v2);
            }
        }

        // Build compact vertex list
        var sortedIndices = walkableVertices.OrderBy(x => x).ToList();
        Dictionary<int, int> oldToNew = new();
        List<Vector3> compactVertices = new(sortedIndices.Count);

        for (int i = 0; i < sortedIndices.Count; i++)
        {
            int oldIndex = sortedIndices[i];
            oldToNew[oldIndex] = i;
            compactVertices.Add(vSpan[oldIndex]);
        }

        // Remap triangle indices
        for (int i = 0; i < triangleIndices.Count; i++)
        {
            triangleIndices[i] = oldToNew[triangleIndices[i]];
        }

        return (compactVertices, triangleIndices);
    }

    /// <summary>
    /// VARIANT 3: Create mesh from explored area (spot-based bounds).
    /// Only includes triangles within bounding box of discovered spots.
    /// Fast approximation of explored mesh.
    /// </summary>
    public static (List<Vector3> vertices, List<int> indices) CreateExploredArea(
        TriangleCollection tc,
        IEnumerable<Spot> spots,
        float expandRadius = 10.0f)
    {
        if (!spots.Any())
            return (new List<Vector3>(), new List<int>());

        // Calculate bounding box of explored area
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

        foreach (Spot spot in spots)
        {
            if (spot.IsBlocked())
                continue;

            Vector3 loc = spot.Loc;
            minX = Math.Min(minX, loc.X);
            minY = Math.Min(minY, loc.Y);
            minZ = Math.Min(minZ, loc.Z);
            maxX = Math.Max(maxX, loc.X);
            maxY = Math.Max(maxY, loc.Y);
            maxZ = Math.Max(maxZ, loc.Z);
        }

        // Expand bounds slightly
        minX -= expandRadius;
        minY -= expandRadius;
        minZ -= expandRadius;
        maxX += expandRadius;
        maxY += expandRadius;
        maxZ += expandRadius;

        HashSet<int> usedVertices = new();
        List<int> triangleIndices = new();

        var tSpan = tc.TrianglesSpan;
        var vSpan = tc.VerteciesSpan;

        // Include triangles within explored bounds
        for (int i = 0; i < tSpan.Length; i++)
        {
            TriangleCollection.GetTriangleVertices(tSpan, vSpan, i,
                out Vector3 v0, out Vector3 v1, out Vector3 v2, out TriangleType flags);

            if (flags != TriangleType.Terrain && flags != TriangleType.Object)
                continue;

            // Check if triangle is within bounds (test all 3 vertices)
            bool inBounds = IsVertexInBounds(v0, minX, minY, minZ, maxX, maxY, maxZ) ||
                           IsVertexInBounds(v1, minX, minY, minZ, maxX, maxY, maxZ) ||
                           IsVertexInBounds(v2, minX, minY, minZ, maxX, maxY, maxZ);

            if (inBounds)
            {
                TriangleCollection.GetTriangle(tSpan, i, out int idx0, out int idx1, out int idx2, out _);

                usedVertices.Add(idx0);
                usedVertices.Add(idx1);
                usedVertices.Add(idx2);

                triangleIndices.Add(idx0);
                triangleIndices.Add(idx1);
                triangleIndices.Add(idx2);
            }
        }

        // Build compact vertex list
        var sortedIndices = usedVertices.OrderBy(x => x).ToList();
        Dictionary<int, int> oldToNew = new();
        List<Vector3> compactVertices = new(sortedIndices.Count);

        for (int i = 0; i < sortedIndices.Count; i++)
        {
            int oldIndex = sortedIndices[i];
            oldToNew[oldIndex] = i;
            compactVertices.Add(vSpan[oldIndex]);
        }

        // Remap triangle indices
        for (int i = 0; i < triangleIndices.Count; i++)
        {
            triangleIndices[i] = oldToNew[triangleIndices[i]];
        }

        return (compactVertices, triangleIndices);
    }

    private static bool IsVertexInBounds(Vector3 v, float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
    {
        return v.X >= minX && v.X <= maxX &&
               v.Y >= minY && v.Y <= maxY &&
               v.Z >= minZ && v.Z <= maxZ;
    }

    private static bool IsNearAnySpot(Vector3 vertex, HashSet<Vector3> spotLocations, float searchRadius)
    {
        float radiusSquared = searchRadius * searchRadius;
        foreach (var spotLoc in spotLocations)
        {
            float dx = vertex.X - spotLoc.X;
            float dy = vertex.Y - spotLoc.Y;
            float distSquared = (dx * dx) + (dy * dy);
            if (distSquared <= radiusSquared)
                return true;
        }
        return false;
    }
}
