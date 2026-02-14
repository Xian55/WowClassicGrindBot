using Core.Database;

using SharedLib;

using System;
using System.Collections.Generic;
using System.Numerics;

namespace Core;

/// <summary>
/// Tracks recent corpse locations for Cannibalize-type abilities.
/// Uses bit-packed GUIDs to look up creature types in CreatureDB.
/// </summary>
public sealed class CorpseTracker
{
    private readonly record struct CorpseInfo(
        int PackedGuid,
        Vector3 MapLoc,
        DateTime DeathTimeUtc);

    public readonly record struct RecentKill(
        int NpcId,
        string NpcName,
        DateTime KillTimeUtc);

    private const int MAX_CORPSES = 10;
    private const int CORPSE_EXPIRY_MS = 120_000;

    private readonly CreatureDB creatureDb;
    private readonly List<CorpseInfo> corpses = new(MAX_CORPSES);

    public event Action? OnCorpseAdded;

    public CorpseTracker(CreatureDB creatureDb)
    {
        this.creatureDb = creatureDb;
    }

    /// <summary>
    /// Add a corpse to the tracker.
    /// </summary>
    /// <param name="packedGuid">Bit-packed GUID containing NPC ID.</param>
    /// <param name="mapLoc">Estimated map location of the corpse.</param>
    public void AddCorpse(int packedGuid, Vector3 mapLoc)
    {
        // Remove existing entry with same GUID (re-adding with updated position)
        corpses.RemoveAll(c => c.PackedGuid == packedGuid);

        corpses.Add(new CorpseInfo(packedGuid, mapLoc, DateTime.UtcNow));

        // Maintain max size by removing oldest
        if (corpses.Count > MAX_CORPSES)
            corpses.RemoveAt(0);

        OnCorpseAdded?.Invoke();
    }

    /// <summary>
    /// Check if there's a Humanoid or Undead corpse within range of player position.
    /// </summary>
    /// <param name="playerWorldPos">Player's current world position.</param>
    /// <param name="worldMapArea">Current world map area for coordinate conversion.</param>
    /// <param name="range">Maximum range in yards (default 5 for Cannibalize).</param>
    /// <returns>True if a valid corpse is within range.</returns>
    public bool HasCannibalizeCorpseNearby(Vector3 playerWorldPos, in WorldMapArea worldMapArea, float range = 5f)
    {
        CleanupExpired();

        float rangeSq = range * range;

        foreach (CorpseInfo corpse in corpses)
        {
            int npcId = GuidUtils.GetNpcId(corpse.PackedGuid);

            if (!creatureDb.Entries.TryGetValue(npcId, out Creature creature))
                continue;

            if (!creature.Type.IsCannibalizable())
                continue;

            Vector3 corpseWorldPos = WorldMapAreaDB.ToWorld_FlipXY(corpse.MapLoc, worldMapArea);
            float distanceSq = Vector3.DistanceSquared(playerWorldPos, corpseWorldPos);
            if (distanceSq <= rangeSq)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Clear all tracked corpses.
    /// </summary>
    public void Clear() => corpses.Clear();

    /// <summary>
    /// Get recent kills with NPC names for UI display.
    /// </summary>
    public IReadOnlyList<RecentKill> GetRecentKills()
    {
        CleanupExpired();

        List<RecentKill> kills = new(corpses.Count);
        foreach (CorpseInfo corpse in corpses)
        {
            int npcId = GuidUtils.GetNpcId(corpse.PackedGuid);
            if (creatureDb.Entries.TryGetValue(npcId, out Creature creature))
            {
                kills.Add(new RecentKill(npcId, creature.Name, corpse.DeathTimeUtc));
            }
        }
        return kills;
    }

    private void CleanupExpired()
    {
        DateTime cutoff = DateTime.UtcNow.AddMilliseconds(-CORPSE_EXPIRY_MS);
        corpses.RemoveAll(c => c.DeathTimeUtc < cutoff);
    }
}
