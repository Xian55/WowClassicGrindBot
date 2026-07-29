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
        // 5.0 and later: talents belong to the class, not to one of its tabs, and
        // form a single six-tier grid. TalentTab still exists on those clients but
        // its ids are the specialisations and no talent row references them, so
        // the per-tab path below would hand back three empty trees.
        TalentTreeElement[] classTalents = GetClassTalents(@class);
        if (classTalents.Length > 0)
            return [classTalents];

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

    // The 5.0 grid: six tiers of three picks, and it is dense - every class has
    // all 18. That is what lets the result be filled by slot instead of sorted.
    private const int MopTalentTiers = 6;
    private const int MopTalentColumns = 3;

    /// <summary>
    /// 5.0 and later rows, ordered tier then column. Empty on earlier clients.
    /// UnitClass shares the numbering of the client's ClassID.
    /// </summary>
    private TalentTreeElement[] GetClassTalents(UnitClass @class)
    {
        int classId = (int)@class;

        int count = 0;
        for (int i = 0; i < talentTreeElements.Length; i++)
        {
            if (talentTreeElements[i].ClassID == classId)
                count++;
        }

        if (count == 0)
            return [];

        // Only the returned array is allocated: no LINQ iterators, no sort
        // comparer, no intermediate buffer.
        TalentTreeElement[] result = new TalentTreeElement[count];
        bool dense = count == MopTalentTiers * MopTalentColumns;

        int next = 0;
        for (int i = 0; i < talentTreeElements.Length; i++)
        {
            ref readonly TalentTreeElement e = ref talentTreeElements[i];
            if (e.ClassID != classId)
                continue;

            // A tier/column pair addresses its own slot, so the result comes out
            // ordered without sorting. Data that does not fit the grid keeps
            // source order rather than landing out of bounds.
            int slot = dense ? (e.TierID * MopTalentColumns) + e.ColumnIndex : next;
            if ((uint)slot >= (uint)count)
                slot = next;

            result[slot] = e;
            next++;
        }

        return result;
    }

    public bool Update(ref Talent talent, UnitClass @class, out int spellId)
    {
        spellId = 1;

        int tierIndex = talent.TierNum - 1;
        int columnIndex = talent.ColumnNum - 1;
        int rankIndex = talent.CurrentRank - 1;

        int index = IndexOfClassTalent(@class, tierIndex, columnIndex);
        if (index == -1)
            index = IndexOfTabTalent(@class, talent.TabNum - 1, tierIndex, columnIndex);

        if (index == -1)
            return false;

        int[] spellIds = talentTreeElements[index].SpellIds;
        if (rankIndex < 0 || rankIndex >= spellIds.Length)
            return false;

        spellId = spellIds[rankIndex];
        if (spellDB.Spells.TryGetValue(spellId, out Spell spell))
        {
            talent.Name = spell.Name;
            return true;
        }

        return false;
    }

    // 5.0+ layout: the tab the addon reports carries no meaning, the class does.
    private int IndexOfClassTalent(UnitClass @class, int tierIndex, int columnIndex)
    {
        for (int i = 0; i < talentTreeElements.Length; i++)
        {
            TalentTreeElement e = talentTreeElements[i];
            if (e.ClassID == (int)@class &&
                e.TierID == tierIndex &&
                e.ColumnIndex == columnIndex)
                return i;
        }

        return -1;
    }

    private int IndexOfTabTalent(UnitClass @class, int tabIndex, int tierIndex, int columnIndex)
    {
        int classMask = (int)Math.Pow(2, (int)@class - 1);

        int tabId = -1;
        for (int i = 0; i < talentTabs.Length; i++)
        {
            TalentTab tab = talentTabs[i];
            if (tab.ClassMask == classMask &&
                tab.OrderIndex == tabIndex)
            {
                tabId = tab.Id;
                break;
            }
        }

        if (tabId == -1)
            return -1;

        for (int i = 0; i < talentTreeElements.Length; i++)
        {
            TalentTreeElement e = talentTreeElements[i];
            if (e.TabID == tabId &&
                e.TierID == tierIndex &&
                e.ColumnIndex == columnIndex)
                return i;
        }

        return -1;
    }
}
