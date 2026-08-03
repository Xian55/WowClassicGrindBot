using Core.Database;

using SharedLib;

using System;
using System.Collections.Generic;

namespace Core;

public sealed class SpellBookReader : IReader
{
    private const int cSpellId = 71;

    private readonly HashSet<int> spells = [];
    private readonly HashSet<string> spellNames = new(StringComparer.OrdinalIgnoreCase);
    private int[] spellIdsSnapshot = [];

    private int expectedCount = -1;
    private int receivedCount;
    private bool snapshotDirty;

    public SpellDB SpellDB { get; }
    public int Count => spells.Count;
    public int ExpectedCount => expectedCount;
    public int ReceivedCount => receivedCount;

    /// <summary>
    /// True once the highest rank of every spell has arrived. Deliberately not tied to
    /// the lower ranks that follow: AddonReader withholds DataReady until this flips, so
    /// waiting for the full set would pause the agent for hundreds of extra ticks after
    /// every /dcflush and on every SPELLS_CHANGED resend.
    /// </summary>
    public bool IsInitialized => expectedCount >= 0 && receivedCount >= expectedCount;

    /// <summary>
    /// True once every rank has arrived, not just the highest of each. Only then can
    /// <see cref="HasExact"/> answer which rank is known.
    /// </summary>
    public bool AllRanksReceived { get; private set; }

    public int Hash { get; private set; }

    public int[] SpellIds
    {
        get
        {
            if (snapshotDirty)
            {
                spellIdsSnapshot = [.. spells];
                snapshotDirty = false;
            }

            return spellIdsSnapshot;
        }
    }

    public SpellBookReader(SpellDB spellDB)
    {
        this.SpellDB = spellDB;

        // Set static reference for KeyReader spell checking
        KeyReader.SpellBookReader = this;
    }

    public void Update(IAddonDataProvider reader)
    {
        int spellId = reader.GetInt(cSpellId);
        if (spellId == 0) return;

        if (spellId >= AddonTicks.QUEUE_COUNT_MARKER)
        {
            expectedCount = spellId - AddonTicks.QUEUE_COUNT_MARKER;
            receivedCount = 0;
            AllRanksReceived = false;
            return;
        }

        // Closes the lower-rank block that follows the counted one.
        if (spellId == AddonTicks.SPELLBOOK_ALL_RANKS_END)
        {
            AllRanksReceived = true;
            return;
        }

        receivedCount++;

        if (!spells.Add(spellId))
            return;

        Hash++;
        snapshotDirty = true;
        if (TryGetValue(spellId, out Spell spell))
        {
            spellNames.Add(spell.Name);
        }
    }

    public void Reset()
    {
        spells.Clear();
        spellNames.Clear();
        spellIdsSnapshot = [];
        snapshotDirty = false;
        expectedCount = -1;
        receivedCount = 0;
        AllRanksReceived = false;
        Hash++;
    }

    /// <summary>
    /// Rank-blind: true when any rank of the spell is known, because the name fallback
    /// matches every rank of a spell against one another. This is what the Spell:
    /// requirement wants - "can I cast this at all".
    /// </summary>
    public bool Has(int id)
    {
        return spells.Contains(id) || (SpellDB.Spells.TryGetValue(id, out Spell spell) && spellNames.Contains(spell.Name));
    }

    /// <summary>
    /// True only for the exact rank. Meaningful once <see cref="AllRanksReceived"/> is
    /// set - before that the lower ranks simply have not arrived yet and this reports
    /// false for ranks the player does own.
    /// </summary>
    public bool HasExact(int id) => spells.Contains(id);

    public bool TryGetValue(int id, out Spell spell)
    {
        return SpellDB.Spells.TryGetValue(id, out spell);
    }

    public int GetId(ReadOnlySpan<char> name)
    {
        foreach (int id in spells)
        {
            if (TryGetValue(id, out Spell spell) &&
                name.Contains(spell.Name, StringComparison.OrdinalIgnoreCase))
            {
                return spell.Id;
            }
        }

        return 0;
    }

    /// <summary>
    /// Checks if a spell name is known by the player (case-insensitive).
    /// Supports partial matching for ranked spells (e.g., "Create Healthstone" matches "Create Healthstone (Minor)").
    /// </summary>
    public bool KnowsSpell(string name)
    {
        // Fast path: exact match
        if (spellNames.Contains(name))
            return true;

        // Partial match: check if any known spell starts with the given name
        // This handles ranked spells like "Create Healthstone (Minor)" matching "Create Healthstone"
        foreach (string knownSpell in spellNames)
        {
            if (knownSpell.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
