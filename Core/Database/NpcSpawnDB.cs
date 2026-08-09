using Microsoft.Extensions.Logging;

using Newtonsoft.Json;

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Numerics;

using static System.IO.File;
using static System.IO.Path;

namespace Core.Database;

/// <summary>
/// Creature entry -&gt; world spawn positions, for an arbitrary map id.
///
/// <para><see cref="AreaDB.NpcWorldLocations"/> holds the same data but only for the map the
/// player is currently standing on, and refreshes it on a background thread when the zone
/// changes. Route generation needs a specific map, deterministically, possibly before the
/// player has ever been there - hence a separate, explicitly-keyed cache.</para>
///
/// <para>A missing file is a normal state, not an error: the dumps are per client and cover
/// neither every map nor every client (<c>npcspawnlocations/som</c> has maps 0 and 1 only).</para>
/// </summary>
public sealed partial class NpcSpawnDB(ILogger<NpcSpawnDB> logger, DataConfig dataConfig)
{
    private readonly Dictionary<int, FrozenDictionary<int, Vector3[]>> byMapId = [];

    public FrozenDictionary<int, Vector3[]> Get(int mapId)
    {
        if (byMapId.TryGetValue(mapId, out FrozenDictionary<int, Vector3[]>? cached))
        {
            return cached;
        }

        FrozenDictionary<int, Vector3[]> result = Load(mapId);
        byMapId[mapId] = result;
        return result;
    }

    private FrozenDictionary<int, Vector3[]> Load(int mapId)
    {
        string path = Join(dataConfig.NpcSpawnLocations, $"{mapId}.json");

        if (!System.IO.File.Exists(path))
        {
            LogNoSpawnData(logger, mapId, path);
            return FrozenDictionary<int, Vector3[]>.Empty;
        }

        Dictionary<int, Vector3[]>? data =
            JsonConvert.DeserializeObject<Dictionary<int, Vector3[]>>(ReadAllText(path));

        if (data == null)
        {
            return FrozenDictionary<int, Vector3[]>.Empty;
        }

        LogLoadedSpawns(logger, data.Count, mapId);

        return data.ToFrozenDictionary();
    }

    #region Logging

    [LoggerMessage(
        EventId = 0080,
        Level = LogLevel.Warning,
        Message = "No NPC spawn data for map {mapId} ({path}) - routes cannot be generated there")]
    static partial void LogNoSpawnData(ILogger logger, int mapId, string path);

    [LoggerMessage(
        EventId = 0081,
        Level = LogLevel.Debug,
        Message = "Loaded spawns for {count} creatures on map {mapId}")]
    static partial void LogLoadedSpawns(ILogger logger, int count, int mapId);

    #endregion
}
