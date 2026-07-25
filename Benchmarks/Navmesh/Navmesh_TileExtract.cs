using BenchmarkDotNet.Attributes;

using PPather.Navmesh;

using WowTriangles;

namespace Benchmarks.Navmesh;

/// <summary>
/// Isolates <see cref="TileGeometryExtractor.Extract"/> with the ADT already
/// resident: the AABB filter sweep over the whole chunk plus the growth of the
/// five output lists. This is the work that currently runs under the global
/// extractLock and therefore serializes every bake worker.
///
/// Run: run.bat --filter *Navmesh_TileExtract*
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 10)]
public class Navmesh_TileExtract
{
    private GeometryFixture fixture = null!;
    private ChunkedTriangleCollection world = null!;
    private int tileX;
    private int tileZ;

    [Params("elwynn-open", "stormwind-wmo", "orgrimmar-wmo", "durotar-water")]
    public string Tile { get; set; } = "elwynn-open";

    [GlobalSetup]
    public void Setup()
    {
        fixture = new GeometryFixture();

        BakeTile tile = NavmeshCorpus.Get(Tile);
        world = fixture.World(tile.MapId);

        NavmeshCoords.GetTileIndex(tile.WorldX, tile.WorldY, out tileX, out tileZ);

        // Warm the ADT so the measurement excludes MPQ I/O.
        TileGeometryExtractor.Extract(world, tileX, tileZ);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        fixture?.Dispose();
    }

    [Benchmark]
    public int Extract()
    {
        TileGeometry geom = TileGeometryExtractor.Extract(world, tileX, tileZ);
        return geom.GroundTriangleCount + geom.LiquidTriangleCount;
    }
}
