using Microsoft.Extensions.Logging;

using Newtonsoft.Json;

using SharedLib;
using SharedLib.Extensions;

using System;
using System.Collections.Generic;
using System.Numerics;

using static System.IO.File;
using static System.IO.Path;

namespace Core.Database;

/// <summary>
/// Database of mailbox world locations.
/// Loaded from Json/mailboxlocations/{expansion}/{mapId}.json
/// </summary>
public sealed class MailboxDB
{
    private readonly ILogger logger;
    private readonly DataConfig dataConfig;
    private readonly WorldMapAreaDB worldMapAreaDB;

    private Vector3[] mailboxLocations = [];
    private int loadedMapId = -1;

    public MailboxDB(ILogger logger, DataConfig dataConfig, WorldMapAreaDB worldMapAreaDB)
    {
        this.logger = logger;
        this.dataConfig = dataConfig;
        this.worldMapAreaDB = worldMapAreaDB;
    }

    /// <summary>
    /// Gets the nearest mailbox for the given UI map and player position.
    /// </summary>
    /// <param name="uiMapId">The UI map ID (from addon)</param>
    /// <param name="playerWorldPos">Player's world position</param>
    /// <returns>World position of nearest mailbox, or null if none found</returns>
    public Vector3? GetNearestMailbox(int uiMapId, Vector3 playerWorldPos)
    {
        if (!worldMapAreaDB.TryGet(uiMapId, out WorldMapArea wma))
        {
            logger.LogWarning("Unknown UI map ID: {uiMapId}", uiMapId);
            return null;
        }

        int mapId = wma.MapID;
        EnsureMailboxesLoaded(mapId);

        if (mailboxLocations.Length == 0)
        {
            return null;
        }

        Vector3? nearest = null;
        float minDistance = float.MaxValue;

        foreach (Vector3 location in mailboxLocations)
        {
            float distance = playerWorldPos.WorldDistanceXYTo(location);
            if (distance < minDistance)
            {
                minDistance = distance;
                nearest = location;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Gets all mailboxes within a maximum distance from the player position.
    /// </summary>
    public List<Vector3> GetMailboxesWithinRange(int uiMapId, Vector3 playerWorldPos, float maxDistance)
    {
        List<Vector3> result = [];

        if (!worldMapAreaDB.TryGet(uiMapId, out WorldMapArea wma))
        {
            return result;
        }

        int mapId = wma.MapID;
        EnsureMailboxesLoaded(mapId);

        foreach (Vector3 location in mailboxLocations)
        {
            float distance = playerWorldPos.WorldDistanceXYTo(location);
            if (distance <= maxDistance)
            {
                result.Add(location);
            }
        }

        return result;
    }

    private void EnsureMailboxesLoaded(int mapId)
    {
        if (loadedMapId == mapId)
            return;

        loadedMapId = mapId;
        LoadMailboxes(mapId);
    }

    private void LoadMailboxes(int mapId)
    {
        string path = Join(dataConfig.MailboxLocations, $"{mapId}.json");

        if (!System.IO.File.Exists(path))
        {
            logger.LogDebug("No mailbox data for map {mapId}", mapId);
            mailboxLocations = [];
            return;
        }

        try
        {
            string json = ReadAllText(path);
            Vector3[]? data = JsonConvert.DeserializeObject<Vector3[]>(json);

            mailboxLocations = data ?? [];

            logger.LogDebug("Loaded {count} mailbox locations for map {mapId}", mailboxLocations.Length, mapId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load mailbox locations for map {mapId}", mapId);
            mailboxLocations = [];
        }
    }
}
