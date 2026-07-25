using System.Numerics;
using System.Runtime.CompilerServices;

using Wmo;

namespace PPather.Navmesh;

/// <summary>
/// Coordinate bridge between WoW world space and Recast/Detour space.
///
/// Detour axes: index 0 = wow Y, index 1 = wow Z (up), index 2 = wow X
/// (the AmeisenNavigation/TrinityCore convention, so their query extents and
/// filter parameters transfer verbatim).
///
/// Detour tile indices are ascending in rc-space from the world corner at
/// -ZEROPOINT: dtTileX walks along wow Y, dtTileZ along wow X. This is
/// deliberately NOT the PPather ADT grid convention ((ZEROPOINT - coord) /
/// TILESIZE, descending); conversion between the two only ever happens through
/// wow-space AABBs produced by <see cref="GetTileWowBounds"/>.
/// </summary>
public static class NavmeshCoords
{
    /// <summary>Rc-space grid origin (both horizontal axes): -32 ADT.</summary>
    public const float Origin = -ChunkReader.ZEROPOINT;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 ToRc(in Vector3 wow)
    {
        return new(wow.Y, wow.Z, wow.X);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 ToWow(in Vector3 rc)
    {
        return new(rc.Z, rc.X, rc.Y);
    }

    /// <summary>Detour tile indices for a WoW position.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void GetTileIndex(float wowX, float wowY, out int dtTileX, out int dtTileZ)
    {
        dtTileX = (int)((wowY - Origin) / NavmeshSettings.TileWorldSize);
        dtTileZ = (int)((wowX - Origin) / NavmeshSettings.TileWorldSize);
    }

    /// <summary>Unpadded WoW-space bounds of a Detour tile.</summary>
    public static void GetTileWowBounds(int dtTileX, int dtTileZ,
        out float minX, out float minY, out float maxX, out float maxY)
    {
        minY = Origin + (dtTileX * NavmeshSettings.TileWorldSize);
        maxY = minY + NavmeshSettings.TileWorldSize;
        minX = Origin + (dtTileZ * NavmeshSettings.TileWorldSize);
        maxX = minX + NavmeshSettings.TileWorldSize;
    }

    public static bool IsValidTile(int dtTileX, int dtTileZ)
    {
        // Plain comparisons: TilesPerSide is derived from the tile size at
        // startup, so it is not a compile-time constant and cannot be a pattern.
        return dtTileX >= 0 && dtTileX < NavmeshSettings.TilesPerSide &&
               dtTileZ >= 0 && dtTileZ < NavmeshSettings.TilesPerSide;
    }
}
