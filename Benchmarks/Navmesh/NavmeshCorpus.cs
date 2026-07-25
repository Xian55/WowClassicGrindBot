using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using SharedLib;
using SharedLib.Data;

using WowTriangles;

namespace Benchmarks.Navmesh;

/// <summary>
/// A representative bake target: one world position, resolved to the navmesh
/// tile containing it. Chosen to cover the cost shapes seen in production -
/// open terrain, WMO-dense cities, and water.
/// </summary>
public sealed record BakeTile(string Name, float MapId, float WorldX, float WorldY);

public static class NavmeshCorpus
{
    public static readonly BakeTile[] Tiles =
    [
        new("elwynn-open", 0, -8898f, -117f),
        new("stormwind-wmo", 0, -8913f, 554f),
        new("dunmorogh-indoor", 0, -6101f, 390f),
        new("barrens-open", 1, -896f, -3770f),
        new("durotar-water", 1, -779f, -4926f),
        new("orgrimmar-wmo", 1, 1618f, -4433f),
    ];

    /// <summary>BenchmarkDotNet [Params] source - names only, tiles resolved via <see cref="Get"/>.</summary>
    public static IEnumerable<string> Names => Tiles.Select(t => t.Name);

    public static BakeTile Get(string name)
    {
        foreach (BakeTile tile in Tiles)
        {
            if (tile.Name == name)
            {
                return tile;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(name), $"Unknown corpus tile '{name}'");
    }

    /// <summary>Directory containing MasterOfPuppets.sln, walking up from the binary.</summary>
    public static string SolutionRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "MasterOfPuppets.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new DirectoryNotFoundException("Solution root not found");
    }

    /// <summary>
    /// BenchmarkDotNet runs from a generated temp directory, so DataConfig's
    /// relative Root ("..\json") and the MPQ path only resolve after moving to
    /// a project directory inside the solution. Same trick as ClassProfile.LoadAllProfiles.
    ///
    /// Note this makes every later relative path resolve against HeadlessServer -
    /// use <see cref="SolutionRoot"/> for anything written back to the repo.
    /// </summary>
    public static void SetWorkingDirectory()
    {
        Directory.SetCurrentDirectory(Path.Combine(SolutionRoot(), "HeadlessServer"));
    }
}

/// <summary>
/// Owns one MPQ-backed triangle world per continent. Construction of a world
/// opens the archives and parses their listfiles, so it is deliberately reused
/// across benchmark iterations; use <see cref="Evict"/> to force cold ADT loads
/// without paying archive open again.
/// </summary>
public sealed class GeometryFixture : IDisposable
{
    private readonly ILogger logger;
    private readonly DataConfig dataConfig;
    private readonly Dictionary<float, (MPQTriangleSupplier Supplier, ChunkedTriangleCollection World)> worlds = [];

    public DataConfig DataConfig => dataConfig;

    public GeometryFixture(string expansion = "wrath", ILogger? logger = null)
    {
        NavmeshCorpus.SetWorkingDirectory();

        this.logger = logger ?? NullLogger.Instance;
        dataConfig = DataConfig.Load(expansion);

        // MPQTriangleSupplier resolves a continent name from the map id, which
        // PPatherService normally populates in its constructor.
        ContinentDB.Init(new WorldMapAreaDB(dataConfig).Values);
    }

    public ChunkedTriangleCollection World(float mapId)
    {
        if (worlds.TryGetValue(mapId, out (MPQTriangleSupplier Supplier, ChunkedTriangleCollection World) entry))
        {
            return entry.World;
        }

        MPQTriangleSupplier supplier = new(logger, dataConfig, mapId);
        ChunkedTriangleCollection world = new(logger, 64, supplier);

        worlds[mapId] = (supplier, world);
        return world;
    }

    /// <summary>Drops cached ADT triangle collections so the next touch re-reads from MPQ.</summary>
    public void Evict(float mapId)
    {
        if (worlds.TryGetValue(mapId, out (MPQTriangleSupplier Supplier, ChunkedTriangleCollection World) entry))
        {
            entry.World.EvictAll();
        }
    }

    public void Dispose()
    {
        foreach ((MPQTriangleSupplier _, ChunkedTriangleCollection world) in worlds.Values)
        {
            world.Close();
        }

        worlds.Clear();
    }
}
