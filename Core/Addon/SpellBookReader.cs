using Core.Database;

using SharedLib;

using System;
using System.Collections.Generic;

namespace Core;

public sealed class SpellBookReader : IReader
{
    private const int cSpellId = 71;

    private readonly HashSet<int> spells = [];
    private readonly HashSet<string> spellNames = [];

    public SpellDB SpellDB { get; }
    public int Count => spells.Count;

    public SpellBookReader(SpellDB spellDB)
    {
        this.SpellDB = spellDB;
    }

    public void Update(IAddonDataProvider reader)
    {
        int spellId = reader.GetInt(cSpellId);
        if (spellId == 0) return;

        spells.Add(spellId);
        if (TryGetValue(spellId, out Spell spell))
        {
            spellNames.Add(spell.Name);
        }
    }

    public void Reset()
    {
        spells.Clear();
        spellNames.Clear();
    }

    public bool Has(int id)
    {
        return spells.Contains(id) || spellNames.Contains(SpellDB.Spells[id].Name);
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
}
