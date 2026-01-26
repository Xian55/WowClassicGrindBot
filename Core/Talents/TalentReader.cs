using Core.Database;
using Core.Talents;

using System.Collections.Generic;

namespace Core;

public sealed class TalentReader : IReader
{
    private const int cTalent = 72;

    private readonly PlayerReader playerReader;
    private readonly TalentDB talentDB;
    public int Count { get; private set; }

    public Dictionary<int, Talent> Talents { get; } = new();
    public Dictionary<int, int> Spells { get; } = new();

    public TalentReader(PlayerReader playerReader, TalentDB talentDB)
    {
        this.playerReader = playerReader;
        this.talentDB = talentDB;
    }

    public void Update(IAddonDataProvider reader)
    {
        int hash = reader.GetInt(cTalent);
        if (hash == 0 || Talents.ContainsKey(hash)) return;

        //           1-3 +         1-11 +         1-4 +         1-5
        // tab * 1000000 + tier * 10000 + column * 10 + currentRank
        int tab = hash / 1000000;
        int tier = hash / 10000 % 100;
        int column = hash / 10 % 10;
        int rank = hash % 10;

        Talent talent = new()
        {
            Hash = hash,
            TabNum = tab,
            TierNum = tier,
            ColumnNum = column,
            CurrentRank = rank
        };

        if (talentDB.Update(ref talent, playerReader.Class, out int id))
        {
            Talents.Add(hash, talent);
            Spells.Add(hash, id);
            Count += talent.CurrentRank;
        }
    }

    public void Reset()
    {
        Count = 0;
        Talents.Clear();
        Spells.Clear();
    }

    public bool HasTalent(string name, int rank)
    {
        foreach ((int _, Talent t) in Talents)
        {
            if (t.CurrentRank >= rank &&
                t.Name.Contains(name, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
