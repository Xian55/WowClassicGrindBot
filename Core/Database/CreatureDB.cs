using SharedLib;

using System.Collections.Frozen;

using static Newtonsoft.Json.JsonConvert;
using static System.IO.File;
using static System.IO.Path;

namespace Core.Database;

public sealed class CreatureDB
{
    public FrozenDictionary<int, Creature> Entries { get; }

    public CreatureDB(DataConfig dataConfig)
    {
        Creature[] creatures = DeserializeObject<Creature[]>(
            ReadAllText(Join(dataConfig.ExpDbc, "creatures.json")))!;

        Entries = creatures
            .ToFrozenDictionary(c => c.Entry, c => c);
    }
}
