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
*/

using System.Numerics;
using System.Runtime.CompilerServices;

using Wmo;

namespace WowTriangles;

/// <summary>
/// Helper utilities for MCNK-aligned coordinate conversions and spatial queries.
///
/// WoW's terrain hierarchy:
/// - ADT: 533.33 yards (64×64 world grid)
/// - MCNK: 33.33 yards (16×16 grid per ADT = 256 chunks)
/// - MCVT: 145 vertices per MCNK (9×9 outer + 8×8 inner)
/// </summary>
internal static class MCNKHelper
{
    /// <summary>
    /// Get ADT tile coordinates from world position
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void GetADTCoord(float x, float y, out int adt_x, out int adt_y)
    {
        float xOffset = ChunkReader.ZEROPOINT - y;
        float yOffset = ChunkReader.ZEROPOINT - x;

        adt_x = (int)(xOffset / ChunkReader.TILESIZE);
        adt_y = (int)(yOffset / ChunkReader.TILESIZE);
    }

    /// <summary>
    /// Get MCNK chunk coordinates within an ADT from world position
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void GetMCNKCoord(float x, float y, out int adt_x, out int adt_y, out int mcnk_x, out int mcnk_y)
    {
        // First get ADT coordinates
        GetADTCoord(x, y, out adt_x, out adt_y);

        // Then get MCNK coordinates within the ADT
        float localX = (ChunkReader.ZEROPOINT - y) % ChunkReader.TILESIZE;
        float localY = (ChunkReader.ZEROPOINT - x) % ChunkReader.TILESIZE;

        mcnk_x = (int)(localX / ChunkReader.CHUNKSIZE);
        mcnk_y = (int)(localY / ChunkReader.CHUNKSIZE);

        // Clamp to valid range [0, 15]
        mcnk_x = System.Math.Clamp(mcnk_x, 0, 15);
        mcnk_y = System.Math.Clamp(mcnk_y, 0, 15);
    }

    /// <summary>
    /// Get MCNK index within MapTile (0-255)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetMCNKIndex(int mcnk_x, int mcnk_y)
    {
        return mcnk_y * MapTile.SIZE + mcnk_x;
    }

    /// <summary>
    /// Get world bounds for a specific MCNK chunk
    /// </summary>
    public static void GetMCNKBounds(int adt_x, int adt_y, int mcnk_x, int mcnk_y,
                                     out float minX, out float minY, out float maxX, out float maxY)
    {
        // Get ADT base position
        float adt_base_x = ChunkReader.ZEROPOINT - (adt_x * ChunkReader.TILESIZE);
        float adt_base_y = ChunkReader.ZEROPOINT - (adt_y * ChunkReader.TILESIZE);

        // Calculate MCNK bounds within ADT
        minX = adt_base_x - (mcnk_x * ChunkReader.CHUNKSIZE);
        minY = adt_base_y - (mcnk_y * ChunkReader.CHUNKSIZE);
        maxX = minX - ChunkReader.CHUNKSIZE;
        maxY = minY - ChunkReader.CHUNKSIZE;

        // Ensure min < max
        if (minX > maxX) (minX, maxX) = (maxX, minX);
        if (minY > maxY) (minY, maxY) = (maxY, minY);
    }

    /// <summary>
    /// Get MCNK bounds from MapChunk
    /// </summary>
    public static void GetMCNKBoundsFromChunk(in MapChunk chunk, out float minX, out float minY, out float maxX, out float maxY)
    {
        // MapChunk stores base position
        minX = chunk.xbase;
        minY = chunk.ybase;
        maxX = minX + ChunkReader.CHUNKSIZE;
        maxY = minY + ChunkReader.CHUNKSIZE;
    }

    /// <summary>
    /// Check if a 2D point is within MCNK bounds (ignoring Z)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPointInMCNK(float x, float y, float minX, float minY, float maxX, float maxY)
    {
        return x >= minX && x <= maxX && y >= minY && y <= maxY;
    }

    /// <summary>
    /// Check if an AABB (Axis-Aligned Bounding Box) intersects with MCNK bounds
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool AABBIntersectsMCNK(
        float aabb_minX, float aabb_minY, float aabb_maxX, float aabb_maxY,
        float mcnk_minX, float mcnk_minY, float mcnk_maxX, float mcnk_maxY)
    {
        // AABB intersection test (2D)
        return !(aabb_maxX < mcnk_minX || aabb_minX > mcnk_maxX ||
                 aabb_maxY < mcnk_minY || aabb_minY > mcnk_maxY);
    }

    /// <summary>
    /// Calculate AABB for a ModelInstance
    /// </summary>
    public static void GetModelBounds(in ModelInstance mi, out float minX, out float minY, out float maxX, out float maxY)
    {
        if (mi.model.boundingVertices == null || mi.model.boundingVertices.Length == 0)
        {
            // Fallback: use position with small radius
            const float DEFAULT_RADIUS = 5.0f;
            minX = mi.pos.X - DEFAULT_RADIUS;
            minY = mi.pos.Y - DEFAULT_RADIUS;
            maxX = mi.pos.X + DEFAULT_RADIUS;
            maxY = mi.pos.Y + DEFAULT_RADIUS;
            return;
        }

        // Calculate actual bounding box from vertices
        minX = float.MaxValue;
        minY = float.MaxValue;
        maxX = float.MinValue;
        maxY = float.MinValue;

        for (int i = 0; i < mi.model.boundingVertices.Length / 3; i++)
        {
            int off = i * 3;
            float x = mi.model.boundingVertices[off] * mi.scale + mi.pos.X;
            float y = mi.model.boundingVertices[off + 2] * mi.scale + mi.pos.Y;

            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }
    }

    /// <summary>
    /// Calculate AABB for a WMOInstance using bounding box from WMORoot
    /// </summary>
    public static void GetWMOBounds(in WMOInstance wi, out float minX, out float minY, out float maxX, out float maxY)
    {
        // WMOInstance stores pos2 and pos3 which appear to be bounding box corners
        // Use these directly for a conservative AABB
        minX = System.Math.Min(wi.pos2.X, wi.pos3.X);
        minY = System.Math.Min(wi.pos2.Y, wi.pos3.Y);
        maxX = System.Math.Max(wi.pos2.X, wi.pos3.X);
        maxY = System.Math.Max(wi.pos2.Y, wi.pos3.Y);

        // Ensure we have valid bounds
        if (minX > maxX) (minX, maxX) = (maxX, minX);
        if (minY > maxY) (minY, maxY) = (maxY, minY);
    }

    /// <summary>
    /// Check if a ModelInstance intersects with MCNK chunk
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ModelIntersectsMCNK(in ModelInstance mi, float mcnk_minX, float mcnk_minY, float mcnk_maxX, float mcnk_maxY)
    {
        GetModelBounds(mi, out float model_minX, out float model_minY, out float model_maxX, out float model_maxY);
        return AABBIntersectsMCNK(model_minX, model_minY, model_maxX, model_maxY,
                                  mcnk_minX, mcnk_minY, mcnk_maxX, mcnk_maxY);
    }

    /// <summary>
    /// Check if a WMOInstance intersects with MCNK chunk
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool WMOIntersectsMCNK(in WMOInstance wi, float mcnk_minX, float mcnk_minY, float mcnk_maxX, float mcnk_maxY)
    {
        GetWMOBounds(wi, out float wmo_minX, out float wmo_minY, out float wmo_maxX, out float wmo_maxY);
        return AABBIntersectsMCNK(wmo_minX, wmo_minY, wmo_maxX, wmo_maxY,
                                  mcnk_minX, mcnk_minY, mcnk_maxX, mcnk_maxY);
    }
}
