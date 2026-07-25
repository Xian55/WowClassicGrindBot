using BenchmarkDotNet.Attributes;

using Microsoft.Extensions.Logging.Abstractions;

using PPather.Navmesh;

using SharedLib;

using WowTriangles;

namespace Benchmarks.Navmesh;

/// <summary>
/// Isolates the MPQ half of a cold tile bake: opening the archives (listfile
/// parse) and loading one ADT's triangle soup.
///
/// Run: run.bat --filter *Navmesh_MpqLoad*
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 5)]
public class Navmesh_MpqLoad
{
    private GeometryFixture fixture = null!;
    private BakeTile tile = null!;
    private DataConfig dataConfig = null!;

    [Params("elwynn-open", "stormwind-wmo", "orgrimmar-wmo")]
    public string Tile { get; set; } = "elwynn-open";

    [GlobalSetup]
    public void Setup()
    {
        fixture = new GeometryFixture();
        dataConfig = fixture.DataConfig;
        tile = NavmeshCorpus.Get(Tile);

        // Force the continent's archives + WDT open once so ArchiveOpen and
        // ColdAdtLoad measure distinct things.
        fixture.World(tile.MapId);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        fixture?.Dispose();
    }

    /// <summary>
    /// Archive open + listfile parse + WDT load for one continent. Paid once per
    /// process today; this is the startup cost of the navmesh engine.
    /// </summary>
    [Benchmark]
    public object ArchiveOpenAndWdt()
    {
        MPQTriangleSupplier supplier = new(NullLogger.Instance, dataConfig, tile.MapId);
        return supplier;
    }

    /// <summary>
    /// One ADT read + parsed + emitted as triangles, with archives already open
    /// and the model/WMO caches warm from previous iterations - i.e. the marginal
    /// cost of walking into a new area during a session.
    /// </summary>
    [Benchmark]
    public int ColdAdtLoad()
    {
        fixture.Evict(tile.MapId);

        ChunkedTriangleCollection world = fixture.World(tile.MapId);
        TriangleCollection tc = world.GetChunkAt(tile.WorldX, tile.WorldY);
        return tc.TriangleCount;
    }
}
