using SharedLib;

namespace Core.Database;

/// <summary>
/// Faction reaction rules shared by the NPC search (which wants friendly NPCs) and the
/// route generator (which wants the opposite). Kept in one place so the two cannot drift:
/// a creature the vendor search treats as friendly must never also be a grind candidate.
/// </summary>
public static class FactionExt
{
    private const int AllPlayers = 1;
    private const int AlliancePlayers = 2;
    private const int HordePlayers = 4;

    public static bool FriendlyToPlayer(in Creature npc, PlayerFaction playerFaction,
        FactionTemplateDB factionDB)
    {
        if (!factionDB.Factions.TryGetValue(npc.Faction, out int friendGroup))
        {
            return false;
        }

        return playerFaction switch
        {
            PlayerFaction.Alliance => (friendGroup & (AllPlayers | AlliancePlayers)) != 0,
            PlayerFaction.Horde => (friendGroup & (AllPlayers | HordePlayers)) != 0,
            // A Neutral player (Pandaren before the Wandering Isle is finished)
            // only counts NPCs friendly to every player as friendly.
            PlayerFaction.Neutral => (friendGroup & AllPlayers) != 0,
            _ => false
        };
    }

    /// <summary>
    /// Attackable by this player. Not simply <c>!FriendlyToPlayer</c> in spirit - a faction
    /// the template table does not know about is "not friendly" but also not a safe grind
    /// target, so an unknown faction is excluded from both sets.
    /// </summary>
    public static bool HostileToPlayer(in Creature npc, PlayerFaction playerFaction,
        FactionTemplateDB factionDB)
    {
        return factionDB.Factions.ContainsKey(npc.Faction) &&
            !FriendlyToPlayer(npc, playerFaction, factionDB);
    }
}
