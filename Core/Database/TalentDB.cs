using Core.Talents;

using Microsoft.Extensions.Logging;

using Newtonsoft.Json;

using SharedLib;

using System;
using System.Linq;

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
                logger.LogWarning("Missing file: {Path}", path);
                return [];
            }

            var json = ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                logger.LogWarning("Empty file: {Path}", path);
                return [];
            }

            var data = DeserializeObject<T[]>(json);
            return data ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError("Failed to read {Path}: {Message}", path, ex.Message);
            return [];
        }
    }

    /// <summary>
    /// Gets all talent tree elements for a specific class, organized by tree index.
    /// Returns array of 3 trees, each containing talents sorted by tier and column.
    /// </summary>
    public TalentTreeElement[][] GetTalentTreesForClass(UnitClass @class)
    {
        int classMask = (int)Math.Pow(2, (int)@class - 1);

        // Get tab IDs for this class, ordered by OrderIndex (0, 1, 2)
        var classTabs = talentTabs
            .Where(t => t.ClassMask == classMask)
            .OrderBy(t => t.OrderIndex)
            .ToArray();

        var result = new TalentTreeElement[classTabs.Length][];

        for (int i = 0; i < classTabs.Length; i++)
        {
            int tabId = classTabs[i].Id;
            result[i] = talentTreeElements
                .Where(e => e.TabID == tabId)
                .OrderBy(e => e.TierID)
                .ThenBy(e => e.ColumnIndex)
                .ToArray();
        }

        return result;
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
