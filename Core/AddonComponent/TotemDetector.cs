using Core.Database;

using SharedLib;

namespace Core;

/// <summary>
/// Detects totems that are damaging the player using combat log data.
/// Uses TextReader to get totem names when CreatureDB doesn't have the NPC.
/// </summary>
public sealed class TotemDetector
{
    private readonly CombatLog combatLog;
    private readonly CreatureDB creatureDb;
    private readonly TextReader textReader;

    public TotemDetector(
        CombatLog combatLog,
        CreatureDB creatureDb,
        TextReader textReader)
    {
        this.combatLog = combatLog;
        this.creatureDb = creatureDb;
        this.textReader = textReader;
    }

    /// <summary>
    /// Returns true if any creature in DamageTaken is a Totem type.
    /// </summary>
    public bool HasDamagingTotem()
    {
        return TryGetDamagingTotem(out _);
    }

    /// <summary>
    /// Tries to get the first totem creature that is damaging the player.
    /// </summary>
    public bool TryGetDamagingTotem(out Creature totem)
    {
        foreach (int packedGuid in combatLog.DamageTaken)
        {
            int npcId = GuidUtils.GetNpcId(packedGuid);
            if (creatureDb.Entries.TryGetValue(npcId, out Creature creature) &&
                creature.Type == CreatureType.Totem)
            {
                totem = creature;
                return true;
            }
        }

        totem = default;
        return false;
    }

    /// <summary>
    /// Returns true if any creature summoned by hostile NPCs is a Totem type.
    /// </summary>
    public bool HasSummonedTotem()
    {
        return TryGetSummonedTotem(out _);
    }

    /// <summary>
    /// Tries to get the first totem creature summoned by a hostile NPC.
    /// </summary>
    public bool TryGetSummonedTotem(out Creature totem)
    {
        foreach (int packedGuid in combatLog.EnemySummons)
        {
            int npcId = GuidUtils.GetNpcId(packedGuid);
            if (creatureDb.Entries.TryGetValue(npcId, out Creature creature) &&
                creature.Type == CreatureType.Totem)
            {
                totem = creature;
                return true;
            }
        }

        totem = default;
        return false;
    }

    /// <summary>
    /// Returns true if any totem is detected (damaging or summoned).
    /// </summary>
    public bool HasTotem()
    {
        return TryGetTotem(out _);
    }

    /// <summary>
    /// Tries to get any totem (prioritizes damaging totems, then summoned totems).
    /// </summary>
    public bool TryGetTotem(out Creature totem)
    {
        return TryGetDamagingTotem(out totem) || TryGetSummonedTotem(out totem);
    }

    /// <summary>
    /// Tries to get the totem name for targeting.
    /// Prioritizes TextReader (direct from addon) over CreatureDB lookup.
    /// </summary>
    public bool TryGetTotemName(out string name)
    {
        // First check if we have a name from TextReader (most reliable for unknown NPCs)
        if (!string.IsNullOrEmpty(textReader.LastTotemName))
        {
            name = textReader.LastTotemName;
            return true;
        }

        // Fallback to CreatureDB lookup
        if (TryGetTotem(out Creature totem))
        {
            name = totem.Name;
            return true;
        }

        name = string.Empty;
        return false;
    }

    /// <summary>
    /// Clears the cached totem name. Call after successfully targeting.
    /// </summary>
    public void ClearTotemName()
    {
        textReader.ClearTotemName();
    }
}
