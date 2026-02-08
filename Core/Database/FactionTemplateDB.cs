using Microsoft.Extensions.Logging;

using SharedLib;
using SharedLib.Data;

using System;
using System.Collections.Frozen;

using static Newtonsoft.Json.JsonConvert;
using static System.IO.File;
using static System.IO.Path;

namespace Core.Database;

public sealed class FactionTemplateDB
{
    public FrozenDictionary<int, int> Factions { get; }

    public const string FileName = "factiontemplates.json";

    public FactionTemplateDB(ILogger<FactionTemplateDB> logger, DataConfig dataConfig)
    {
        string path = Join(dataConfig.ExpDbc, FileName);

        FactionTemplate[] data = [];

        try
        {
            string json = ReadAllText(path);
            if (!string.IsNullOrWhiteSpace(json))
            {
                data = DeserializeObject<FactionTemplate[]>(json) ?? [];
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Failed to load {FileName}: {Message}", FileName, ex.Message);
            logger.LogWarning("AdhocNPC profiles with 'Auto NPC Route' features will not work properly!");
        }

        Factions = data.ToFrozenDictionary(c => c.Id, c => c.FriendGroup);
    }
}
