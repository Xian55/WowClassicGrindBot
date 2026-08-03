using Core.Goals;

using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;

namespace Core.GOAP;

/// <summary>
/// Explains a <c>NO PLAN</c> state.
///
/// <para>
/// <see cref="GoapAgent"/> plans against <see cref="GoapPlanner.EmptyGoalState"/>,
/// so <c>InState(goal, effectedState)</c> inside <see cref="GoapPlanner"/> is
/// satisfied by any single action. A plan therefore exists exactly when at least
/// one goal has <see cref="GoapGoal.CanRun"/> true and every precondition matching
/// the world state. Listing per goal whether it was usable plus which preconditions
/// were unmet is a complete explanation - not a heuristic.
/// </para>
///
/// <para>
/// The lines above the goal table carry the raw values behind the world state bits.
/// The bits alone rarely identify a new gap; which GUIDs are in
/// <see cref="CombatLog.DamageTaken"/>, whether the soft target is a corpse and the
/// counters in <see cref="GoapAgentState"/> do.
/// </para>
/// </summary>
public static class NoPlanReport
{
    private const int NameWidth = 20;

    public static string Build(
        GoapGoal[] goals,
        IReadOnlySet<GoapGoal> usable,
        BitVector32 worldState,
        PlayerReader playerReader,
        AddonBits bits,
        CombatLog combatLog,
        GoapAgentState state)
    {
        StringBuilder sb = new(1024);

        sb.Append("NO PLAN - nothing runnable");

        sb.Append("\n World : ");
        AppendWorldState(sb, worldState);

        sb.Append("\n Target: guid=").Append(playerReader.TargetGuid)
            .Append(" npc=").Append(playerReader.TargetId)
            .Append(" dead=").Append(bits.Target_Dead())
            .Append(" hostile=").Append(bits.Target_Hostile())
            .Append(" combat=").Append(bits.Target_Combat())
            .Append(" tagged=").Append(bits.Target_Tagged())
            .Append(" hp=").Append(playerReader.TargetHealthPercent()).Append('%')
            .Append(" targetTarget=").Append(playerReader.TargetTarget);

        sb.Append("\n Soft  : enabled=").Append(bits.SoftInteract_Enabled())
            .Append(" exists=").Append(bits.SoftInteract())
            .Append(" id=").Append(playerReader.SoftInteract_Id)
            .Append(" type=").Append(playerReader.SoftInteract_Type)
            .Append(" dead=").Append(bits.SoftInteract_Dead())
            .Append(" tagged=").Append(bits.SoftInteract_Tagged());

        // npc<id>#<packedGuid> - the id is what identifies the mob, the packed
        // guid is what the sets are actually keyed on. See GuidUtils.
        sb.Append("\n Combat: dmgTaken=");
        AppendGuids(sb, combatLog.DamageTaken);
        sb.Append(" dmgDone=");
        AppendGuids(sb, combatLog.DamageDone);
        sb.Append(" toPull=");
        AppendGuids(sb, combatLog.ToPull);
        sb.Append(" recentlyDead=");
        AppendGuids(sb, combatLog.RecentlyDead);
        sb.Append(" lastDmgDone=").Append(combatLog.LastDamageDoneTime.ElapsedMs()).Append("ms");

        sb.Append("\n Pet   : exists=").Append(bits.Pet())
            .Append(" alive=").Append(playerReader.PetAlive())
            .Append(" defensive=").Append(bits.Pet_Defensive())
            .Append(" petTarget=");
        AppendGuid(sb, playerReader.PetTargetGuid);
        sb.Append(" petTargetDead=").Append(bits.PetTarget_Dead());

        sb.Append("\n State : kills=").Append(state.LastCombatKillCount)
            .Append(" lootable=").Append(state.LootableCorpseCount)
            .Append(" consumable=").Append(state.ConsumableCorpseCount)
            .Append(" gatherable=").Append(state.GatherableCorpseCount)
            .Append(" consume=").Append(state.ShouldConsumeCorpse)
            .Append(" gathering=").Append(state.Gathering);

        sb.Append("\n Range : min=").Append(playerReader.MinRange())
            .Append(" max=").Append(playerReader.MaxRange())
            .Append(" pull=").Append(playerReader.WithInPullRange())
            .Append(" combat=").Append(playerReader.WithInCombatRange());

        sb.Append("\n Goals :");
        AppendGoals(sb, goals, usable, worldState);

        return sb.ToString();
    }

    /// <summary>Every <see cref="GoapKey"/> with its current value.</summary>
    public static void AppendWorldState(StringBuilder sb, BitVector32 worldState)
    {
        for (int i = 0; i < (int)GoapKey.LENGTH; i++)
        {
            GoapKey key = (GoapKey)i;

            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(key.ToStringF(worldState[1 << i]));
        }
    }

    private static void AppendGuids(StringBuilder sb, HashSet<int> guids)
    {
        sb.Append('[');

        bool first = true;
        foreach (int guid in guids)
        {
            if (!first)
            {
                sb.Append(' ');
            }

            AppendGuid(sb, guid);
            first = false;
        }

        sb.Append(']');
    }

    /// <summary>
    /// <c>npc&lt;id&gt;#&lt;packedGuid&gt;</c> - a bare 0 stays 0 so an empty
    /// slot does not read as a creature.
    /// </summary>
    private static void AppendGuid(StringBuilder sb, int packedGuid)
    {
        if (packedGuid == 0)
        {
            sb.Append('0');
            return;
        }

        sb.Append("npc").Append(GuidUtils.GetNpcId(packedGuid))
            .Append('#').Append(packedGuid);
    }

    /// <summary>
    /// One line per goal: cost, name, whether <see cref="GoapGoal.CanRun"/> passed
    /// and which preconditions did not match. Nearest miss first.
    /// </summary>
    public static void AppendGoals(StringBuilder sb,
        GoapGoal[] goals,
        IReadOnlySet<GoapGoal> usable,
        BitVector32 worldState)
    {
        // Nearest miss first - the goal a single flipped bit away from running
        // is the one worth reading, and it is the line the reporter pastes.
        List<(GoapGoal goal, bool canRun, int unmet)> rows = new(goals.Length);
        for (int i = 0; i < goals.Length; i++)
        {
            GoapGoal goal = goals[i];
            rows.Add((goal, usable.Contains(goal), UnmetCount(goal, worldState)));
        }

        rows.Sort(static (a, b) =>
        {
            if (a.canRun != b.canRun)
            {
                return a.canRun ? -1 : 1;
            }

            int byUnmet = a.unmet.CompareTo(b.unmet);
            return byUnmet != 0
                ? byUnmet
                : a.goal.Cost.CompareTo(b.goal.Cost);
        });

        for (int i = 0; i < rows.Count; i++)
        {
            (GoapGoal goal, bool canRun, _) = rows[i];

            sb.Append("\n  ").Append(goal.Cost.ToString("0.00").PadLeft(6))
                .Append(' ').Append(goal.Name.PadRight(NameWidth))
                .Append(" run=").Append(canRun ? "True" : "False");

            if (!canRun)
            {
                sb.Append("  (CanRun)");
            }

            AppendUnmet(sb, goal, worldState);
        }
    }

    private static int UnmetCount(GoapGoal goal, BitVector32 worldState)
    {
        int count = 0;
        foreach ((GoapKey key, bool value) in goal.Preconditions)
        {
            if (worldState[1 << (int)key] != value)
            {
                count++;
            }
        }
        return count;
    }

    private static void AppendUnmet(StringBuilder sb, GoapGoal goal, BitVector32 worldState)
    {
        bool first = true;
        foreach ((GoapKey key, bool value) in goal.Preconditions)
        {
            if (worldState[1 << (int)key] == value)
            {
                continue;
            }

            sb.Append(first ? "  unmet: " : ", ");
            sb.Append(key.ToStringF(value));

            first = false;
        }
    }
}
