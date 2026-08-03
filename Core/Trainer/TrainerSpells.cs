using Core.Database;

using SharedLib;

using System;
using System.Collections.Generic;

namespace Core;

/// <summary>
/// Which whitelisted spells the player is eligible to learn. Pure - no session state.
/// <para>
/// Lives apart from <see cref="TrainerPlanner"/> because the two callers cannot share an
/// instance: <see cref="RequirementFactory"/> is built from the root provider while the
/// profile is still loading, and the planner is a session-scoped goal component that
/// does not exist yet at that point.
/// </para>
/// </summary>
public static class TrainerSpells
{
    /// <summary>
    /// Every <c>IntVariables</c> key with this prefix is a training whitelist, its array
    /// being that spell's ranks in ascending order.
    /// </summary>
    public const string VAR_PREFIX = "SPELL_";

    public static int[][] BuildWhitelist(ClassConfiguration classConfig)
    {
        List<int[]> lists = [];

        foreach ((string key, int[] ids) in classConfig.IntVariables)
        {
            if (key.StartsWith(VAR_PREFIX, StringComparison.OrdinalIgnoreCase))
                lists.Add(ids);
        }

        return [.. lists];
    }

    public static int MaxCount(int[][] whitelist)
    {
        int total = 0;
        for (int i = 0; i < whitelist.Length; i++)
            total += whitelist[i].Length;

        return total;
    }

    /// <summary>
    /// Never true before <see cref="SpellBookReader.AllRanksReceived"/>: until the lower
    /// ranks land the spellbook cannot say which rank is owned, and every rank below the
    /// highest known one would look unlearnt.
    /// </summary>
    public static bool Any(int[][] whitelist,
        SpellBookReader spellBookReader, SpellDB spellDB, int playerLevel)
    {
        if (!spellBookReader.AllRanksReceived)
            return false;

        for (int i = 0; i < whitelist.Length; i++)
        {
            int[] ids = whitelist[i];
            for (int j = 0; j < ids.Length; j++)
            {
                if (IsTrainable(ids[j], spellBookReader, spellDB, playerLevel, out _))
                    return true;
            }
        }

        return false;
    }

    public static int Collect(int[][] whitelist,
        SpellBookReader spellBookReader, SpellDB spellDB, int playerLevel,
        Span<int> destination, ICollection<int>? unknownIds = null)
    {
        int written = 0;

        if (!spellBookReader.AllRanksReceived)
            return written;

        for (int i = 0; i < whitelist.Length; i++)
        {
            int[] ids = whitelist[i];
            for (int j = 0; j < ids.Length; j++)
            {
                if (written >= destination.Length)
                    return written;

                if (IsTrainable(ids[j], spellBookReader, spellDB, playerLevel, out bool missing))
                    destination[written++] = ids[j];
                else if (missing)
                    unknownIds?.Add(ids[j]);
            }
        }

        return written;
    }

    /// <summary>
    /// Where one whitelisted rank stands. Shared by the bot and the UI so the page
    /// cannot disagree with what the bot will actually do.
    /// </summary>
    public static TrainableState GetState(int id,
        SpellBookReader spellBookReader, SpellDB spellDB, int playerLevel,
        out Spell spell)
    {
        // Rank exact, not the name-matching Has(): every rank of a spell shares a name,
        // so Has() answers true for rank 1 the moment rank 7 is known.
        bool known = spellBookReader.HasExact(id);

        if (!spellDB.Spells.TryGetValue(id, out spell))
        {
            // A profile written for a later client than the one running.
            return known ? TrainableState.Known : TrainableState.NotInThisClient;
        }

        if (known)
            return TrainableState.Known;

        // A level of 0 means the DBC carries no requirement on that row - several
        // duplicate rows per spell name look like this - so it cannot gate anything.
        return spell.Level == 0 || spell.Level <= playerLevel
            ? TrainableState.Trainable
            : TrainableState.LevelLocked;
    }

    private static bool IsTrainable(int id,
        SpellBookReader spellBookReader, SpellDB spellDB, int playerLevel,
        out bool missingFromDB)
    {
        TrainableState state = GetState(id, spellBookReader, spellDB, playerLevel, out _);

        missingFromDB = state == TrainableState.NotInThisClient;

        return state == TrainableState.Trainable;
    }
}

public enum TrainableState
{
    /// <summary>Already in the spellbook at this exact rank.</summary>
    Known,

    /// <summary>Not known, and the player is high enough level to learn it.</summary>
    Trainable,

    /// <summary>Not known, but the player is below the level the spell requires.</summary>
    LevelLocked,

    /// <summary>The running client's spells.json has no such id.</summary>
    NotInThisClient
}
