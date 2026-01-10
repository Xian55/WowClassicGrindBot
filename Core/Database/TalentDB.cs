using Core.Talents;

using Microsoft.Extensions.Logging;

using Newtonsoft.Json;

using SharedLib;

using System;

using static Newtonsoft.Json.JsonConvert;
using static System.IO.File;
using static System.IO.Path;

namespace Core.Database;

public sealed class TalentDB
{
    private readonly SpellDB spellDB;

    private readonly TalentTab[] talentTabs;
    private readonly TalentTreeElement[] talentTreeElements;

    public TalentDB(ILogger<TalentDB> logger, DataConfig dataConfig, SpellDB spellDB)
    {
        this.spellDB = spellDB;

        talentTabs = LoadJsonSafe<TalentTab>(logger, Join(dataConfig.ExpDbc, "talenttab.json"));
        talentTreeElements = LoadJsonSafe<TalentTreeElement>(logger, Join(dataConfig.ExpDbc, "talent.json"));
    }

    private static T[] LoadJsonSafe<T>(ILogger<TalentDB> logger, string path)
    {
        try
        {
            if (!System.IO.File.Exists(path))
            {
                logger.LogWarning($"Missing file: {path}");
                return [];
            }

            var json = ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                logger.LogWarning($"Empty file: {path}");
                return [];
            }

            var data = DeserializeObject<T[]>(json);
            return data ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError($"Failed to read {path}: {ex.Message}");
            return [];
        }
    }

    public bool Update(ref Talent talent, UnitClass @class, out int spellId)
    {
        int classMask = (int)Math.Pow(2, (int)@class - 1);

        int tabId = -1;
        int tabIndex = talent.TabNum - 1;
        for (int i = 0; i < talentTabs.Length; i++)
        {
            var tab = talentTabs[i];
            if (tab.ClassMask == classMask &&
                tab.OrderIndex == tabIndex)
            {
                tabId = tab.Id;
                break;
            }
        }
        spellId = 1;
        if (tabId == -1) return false;

        int tierIndex = talent.TierNum - 1;
        int columnIndex = talent.ColumnNum - 1;
        int rankIndex = talent.CurrentRank - 1;

        int index = -1;
        for (int i = 0; i < talentTreeElements.Length; i++)
        {
            var treeElement = talentTreeElements[i];
            if (treeElement.TabID == tabId &&
                treeElement.TierID == tierIndex &&
                treeElement.ColumnIndex == columnIndex)
            {
                index = i;
                break;
            }
        }

        spellId = talentTreeElements[index].SpellIds[rankIndex];
        if (spellDB.Spells.TryGetValue(spellId, out Spell spell))
        {
            talent.Name = spell.Name;
            return true;
        }

        return false;
    }
}
