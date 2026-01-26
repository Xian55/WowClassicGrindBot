using SharedLib;

using System.Collections.Frozen;

using static Newtonsoft.Json.JsonConvert;
using static System.IO.File;
using static System.IO.Path;

namespace Core.Database;

public sealed class SpellDB
{
    public FrozenDictionary<int, Spell> Spells { get; }

    public SpellDB(DataConfig dataConfig)
    {
        Spell[] spells = DeserializeObject<Spell[]>(
            ReadAllText(Join(dataConfig.ExpDbc, "spells.json")))!;

        this.Spells = spells
            .ToFrozenDictionary(spell => spell.Id);
    }
}
