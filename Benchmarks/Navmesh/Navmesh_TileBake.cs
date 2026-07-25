using BenchmarkDotNet.Attributes;

using DotRecast.Core;
using DotRecast.Detour;
using DotRecast.Recast;

using PPather.Navmesh;

namespace Benchmarks.Navmesh;

/// <summary>
/// The recast half of a tile bake, over geometry extracted once in setup - pure
/// CPU, no MPQ I/O. This is the dominant cost of a cold tile today (0.8-2.3s).
///
/// Run: run.bat --filter *Navmesh_TileBake*
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 5)]
public class Navmesh_TileBake
{
    private GeometryFixture fixture = null!;
    private TileGeometry geom = null!;
    private int tileX;
    private int tileZ;

    [Params("elwynn-open", "stormwind-wmo", "orgrimmar-wmo")]
    public string Tile { get; set; } = "elwynn-open";

    [GlobalSetup]
    public void Setup()
    {
        fixture = new GeometryFixture();

        BakeTile tile = NavmeshCorpus.Get(Tile);
        NavmeshCoords.GetTileIndex(tile.WorldX, tile.WorldY, out tileX, out tileZ);
        geom = TileGeometryExtractor.Extract(fixture.World(tile.MapId), tileX, tileZ);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        fixture?.Dispose();
    }

    /// <summary>Shipping parameters: cs 0.2667, watershed, detail mesh on.</summary>
    [Benchmark(Baseline = true)]
    public int Default()
    {
        DtMeshData? data = NavmeshTileBuilder.Bake(geom, tileX, tileZ);
        return data?.header.polyCount ?? 0;
    }

    /// <summary>Half the voxel resolution: 4x fewer cells. Coarser doorways/ledges.</summary>
    [Benchmark]
    public int CoarseCells()
    {
        return BakeWith(NavmeshTileBuilder.CreateConfig(
            cellSize: 0.5333333f, RcPartition.WATERSHED, buildMeshDetail: true));
    }

    /// <summary>Monotone partitioning: no distance field, but long thin regions.</summary>
    [Benchmark]
    public int Monotone()
    {
        return BakeWith(NavmeshTileBuilder.CreateConfig(
            NavmeshSettings.CellSize, RcPartition.MONOTONE, buildMeshDetail: true));
    }

    /// <summary>Detail mesh off: prices BuildPolyMeshDetail, which is often 30-50% of bake.</summary>
    [Benchmark]
    public int NoDetailMesh()
    {
        return BakeWith(NavmeshTileBuilder.CreateConfig(
            NavmeshSettings.CellSize, RcPartition.WATERSHED, buildMeshDetail: false));
    }

    private int BakeWith(RcConfig cfg)
    {
        DtMeshData? data = NavmeshTileBuilder.Bake(geom, tileX, tileZ, new RcContext(), cfg);
        return data?.header.polyCount ?? 0;
    }
}
