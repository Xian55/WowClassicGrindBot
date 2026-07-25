using System;
using System.Collections.Generic;
using System.Numerics;

using PPather.Triangles;

using SharedLib;

using WowTriangles;

namespace PPather.Navmesh;

/// <summary>
/// Pulls the triangle soup for one navmesh tile out of the existing MPQ
/// geometry pipeline (<see cref="ChunkedTriangleCollection"/>), split into
/// ground (Terrain|Object|Model, slope-filtered later by recast) and liquid
/// (Water/Magma/Slime as recast areas) sets, converted to rc-space.
/// </summary>
public static class TileGeometryExtractor
{
    /// <summary>
    /// Extra padding beyond the recast border so border-cell voxels see
    /// geometry that starts just outside the tile (border is 5 cells ≈ 1.33yd).
    /// </summary>
    public const float PaddingYd = 4f;

    // Dev-baseline TriangleType has no lava/slime distinction; all liquid is
    // Water. AreaMagmaSlime stays reserved for when the supplier learns to
    // classify lethal liquids.
    private const TriangleType LiquidMask = TriangleType.Water;

    /// <summary>
    /// MCNK-granularity sample step, yards: touching every point on this grid
    /// inside the padded bounds loads all overlapping ADT chunks on demand.
    /// </summary>
    private const float ChunkSampleStepYd = 256f;

    public static TileGeometry Extract(ChunkedTriangleCollection world, int dtTileX, int dtTileZ,
        NavmeshBakeOptions? bake = null, float? minWorldZ = null)
    {
        bake ??= new NavmeshBakeOptions();

        NavmeshCoords.GetTileWowBounds(dtTileX, dtTileZ,
            out float tileMinX, out float tileMinY, out float tileMaxX, out float tileMaxY);

        float border = (BorderCells(bake.AgentRadius) * NavmeshSettings.CellSize) + PaddingYd;
        float minX = tileMinX - border;
        float minY = tileMinY - border;
        float maxX = tileMaxX + border;
        float maxY = tileMaxY + border;

        // Touching every sample point inside the padded bounds loads all
        // overlapping ADT chunks on demand (ADT = 533.33yd).
        List<TriangleCollection> chunks = [];
        int failedChunkLoads = 0;
        for (float x = minX; ; x += ChunkSampleStepYd)
        {
            bool lastX = x >= maxX;
            if (lastX)
            {
                x = maxX;
            }

            for (float y = minY; ; y += ChunkSampleStepYd)
            {
                bool lastY = y >= maxY;
                if (lastY)
                {
                    y = maxY;
                }

                // A missing or unreadable ADT (ocean edges, corrupt archives)
                // must not kill the whole tile - bake what did load.
                try
                {
                    TriangleCollection tc = world.GetChunkAt(x, y);
                    if (!chunks.Contains(tc))
                    {
                        chunks.Add(tc);
                    }
                }
                catch (Exception)
                {
                    failedChunkLoads++;
                }

                if (lastY)
                {
                    break;
                }
            }

            if (lastX)
            {
                break;
            }
        }

        List<float> groundVerts = [];
        List<int> groundTris = [];
        List<float> liquidVerts = [];
        List<int> liquidTris = [];
        List<int> liquidAreas = [];

        float minZ = float.MaxValue;
        float maxZ = float.MinValue;

        foreach (TriangleCollection tc in chunks)
        {
            ReadOnlySpan<Triangle<int>> tris = tc.TrianglesSpan;
            ReadOnlySpan<Vector3> verts = tc.VerteciesSpan;

            for (int i = 0; i < tris.Length; i++)
            {
                Triangle<int> t = tris[i];

                Vector3 v0 = verts[t.V0];
                Vector3 v1 = verts[t.V1];
                Vector3 v2 = verts[t.V2];

                // AABB reject against padded tile bounds (wow XY plane).
                float triMinX = MathF.Min(v0.X, MathF.Min(v1.X, v2.X));
                if (triMinX > maxX)
                {
                    continue;
                }

                float triMaxX = MathF.Max(v0.X, MathF.Max(v1.X, v2.X));
                if (triMaxX < minX)
                {
                    continue;
                }

                float triMinY = MathF.Min(v0.Y, MathF.Min(v1.Y, v2.Y));
                if (triMinY > maxY)
                {
                    continue;
                }

                float triMaxY = MathF.Max(v0.Y, MathF.Max(v1.Y, v2.Y));
                if (triMaxY < minY)
                {
                    continue;
                }

                // Below the world-Z floor: the floating-continent death base
                // (Outland's playable landmass sits far above it). Dropping it
                // here keeps it out of both the ground and liquid sets and out
                // of the tile's vertical bounds.
                if (minWorldZ is float floorZ &&
                    MathF.Max(v0.Z, MathF.Max(v1.Z, v2.Z)) < floorZ)
                {
                    continue;
                }

                minZ = MathF.Min(minZ, MathF.Min(v0.Z, MathF.Min(v1.Z, v2.Z)));
                maxZ = MathF.Max(maxZ, MathF.Max(v0.Z, MathF.Max(v1.Z, v2.Z)));

                if ((t.Flags & LiquidMask) != 0)
                {
                    AppendTriangle(liquidVerts, liquidTris, v0, v1, v2);
                    liquidAreas.Add(NavmeshSettings.AreaWater);
                }
                else
                {
                    AppendTriangle(groundVerts, groundTris, v0, v1, v2);
                }
            }
        }

        if (groundTris.Count == 0)
        {
            minZ = 0;
            maxZ = 1;
        }

        // Horizontal bounds stay the unpadded tile edges: RcBuilderConfig
        // expands them by borderSize*cs itself. Vertical from geometry with
        // climb headroom.
        Vector3 wowMin = new(tileMinX, tileMinY, minZ - bake.AgentMaxClimb);
        Vector3 wowMax = new(tileMaxX, tileMaxY, maxZ + NavmeshSettings.AgentHeight);

        return new TileGeometry
        {
            GroundVerts = [.. groundVerts],
            GroundTris = [.. groundTris],
            LiquidVerts = [.. liquidVerts],
            LiquidTris = [.. liquidTris],
            LiquidAreas = [.. liquidAreas],
            BMin = NavmeshCoords.ToRc(wowMin),
            BMax = NavmeshCoords.ToRc(wowMax),
            FailedChunkLoads = failedChunkLoads,
        };
    }

    /// <summary>Recast's CalcBorder base cell count (the constant term of 3 + ceil(radius/cs)).</summary>
    public const int RcBorderBaseCells = 3;

    /// <summary>
    /// Cells of geometry to pull beyond the tile edge, matching recast's
    /// RcConfig.CalcBorder(agentRadius, CellSize) = 3 + ceil(radius / cellSize).
    ///
    /// Must track the agent radius: the bake erodes by it, so its border grows
    /// with the radius, and if the extractor fetched a smaller border the outer
    /// ring of each tile would bake against missing geometry and leave holes at
    /// tile seams. At the default radius this is 5; at radius 1.0 it is 7.
    /// </summary>
    public static int BorderCells(float agentRadius) =>
        RcBorderBaseCells + (int)MathF.Ceiling(agentRadius / NavmeshSettings.CellSize);

    private static void AppendTriangle(List<float> verts, List<int> tris, in Vector3 v0, in Vector3 v1, in Vector3 v2)
    {
        int baseIndex = verts.Count / 3;

        AppendVertex(verts, v0);
        AppendVertex(verts, v1);
        AppendVertex(verts, v2);

        tris.Add(baseIndex);
        tris.Add(baseIndex + 1);
        tris.Add(baseIndex + 2);
    }

    private static void AppendVertex(List<float> verts, in Vector3 wow)
    {
        // rc order: (wow.Y, wow.Z, wow.X)
        verts.Add(wow.Y);
        verts.Add(wow.Z);
        verts.Add(wow.X);
    }
}
