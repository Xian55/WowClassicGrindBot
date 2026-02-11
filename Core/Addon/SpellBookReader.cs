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

    public SpellDB SpellDB { get; }
    public int Count => spells.Count;
    public int Hash { get; private set; }
    public int[] SpellIds => spellIdsSnapshot;

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

        if (spells.Add(spellId))
        {
            Hash++;
            spellIdsSnapshot = [.. spells];
            if (TryGetValue(spellId, out Spell spell))
            {
                spellNames.Add(spell.Name);
            }
        }
    }

    public void Reset()
    {
        spells.Clear();
        spellNames.Clear();
        spellIdsSnapshot = [];
        Hash++;
    }

    public bool Has(int id)
    {
        return spells.Contains(id) || (SpellDB.Spells.TryGetValue(id, out Spell spell) && spellNames.Contains(spell.Name));
    }

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
