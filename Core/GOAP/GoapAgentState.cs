using System.Collections.Generic;

namespace Core.GOAP;

public sealed class GoapAgentState
{
    public bool ShouldConsumeCorpse { get; set; }
    public int LootableCorpseCount { get; set; }
    public int GatherableCorpseCount { get; set; }
    public int ConsumableCorpseCount { get; set; }
    public int LastCombatKillCount { get; set; }
    public bool Gathering { get; set; }

    public HashSet<int> RecentlyLooted { get; } = [];
}
