using Microsoft.Extensions.Logging;

using SharedLib;

using System;
using System.Collections.Frozen;

using static Newtonsoft.Json.JsonConvert;
using static System.IO.File;
using static System.IO.Path;

namespace Core.Database;

public sealed class CreatureDB
{
    public FrozenDictionary<int, Creature> Entries { get; }

    public CreatureDB(ILogger<CreatureDB> logger, DataConfig dataConfig)
    {
        string path = Join(dataConfig.ExpDbc, "creatures.json");

        // Degrade rather than throw, matching TalentDB's LoadJsonSafe. This data is
        // optional enrichment - names for creature ids - and it is generated per client
        // from an emulator world database, so a newly supported client legitimately has
        // none yet. Throwing from the constructor kills the whole host at startup, which
        // is how a missing legacy_mop/creatures.json took BlazorServer down.
        // Qualified: both File and Path are imported statically and each has Exists.
        if (!System.IO.File.Exists(path))
        {
            logger.LogWarning("Missing file: {Path}", path);
            Entries = FrozenDictionary<int, Creature>.Empty;
            return;
        }

        try
        {
            Creature[]? creatures = DeserializeObject<Creature[]>(ReadAllText(path));

            Entries = creatures is null
                ? FrozenDictionary<int, Creature>.Empty
                : creatures.ToFrozenDictionary(c => c.Entry, c => c);
        }
        catch (Exception ex)
        {
            logger.LogError("Failed to read {Path}: {Message}", path, ex.Message);
            Entries = FrozenDictionary<int, Creature>.Empty;
        }
    }
}
