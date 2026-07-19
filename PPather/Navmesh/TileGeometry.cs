#nullable enable
using System.Numerics;

namespace PPather.Navmesh;

/// <summary>
/// Triangle soup for one navmesh tile, already in rc-space.
/// Vertices are unshared (3 per triangle): the voxel rasterizer does not
/// benefit from indexed sharing and this keeps extraction allocation-simple.
/// </summary>
public sealed class TileGeometry
{
    public required float[] GroundVerts { get; init; }
    public required int[] GroundTris { get; init; }

    public required float[] LiquidVerts { get; init; }
    public required int[] LiquidTris { get; init; }
    /// <summary>Per-liquid-triangle recast area (AreaWater or AreaMagmaSlime).</summary>
    public required int[] LiquidAreas { get; init; }

    /// <summary>Rc-space bounds: horizontal = unpadded tile edges, vertical from geometry.</summary>
    public required Vector3 BMin { get; init; }
    public required Vector3 BMax { get; init; }

    /// <summary>ADT chunks that failed to load (missing/corrupt) and were skipped.</summary>
    public int FailedChunkLoads { get; init; }

    public int GroundTriangleCount => GroundTris.Length / 3;
    public int LiquidTriangleCount => LiquidTris.Length / 3;

    public bool IsEmpty => GroundTris.Length == 0;
}
