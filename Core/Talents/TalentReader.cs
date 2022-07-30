using System.Collections.Generic;
using Core.Talents;
using Core.Database;

namespace Core
{
    public class TalentReader
    {
        private readonly int cTalent;

        private readonly PlayerReader playerReader;
        private readonly TalentDB talentDB;
        public int Count { get; private set; }

        public Dictionary<int, Talent> Talents { get; } = new();
        public Dictionary<int, int> Spells { get; } = new();

        public TalentReader(int cTalent, PlayerReader playerReader, TalentDB talentDB)
        {
            this.cTalent = cTalent;

            this.playerReader = playerReader;
            this.talentDB = talentDB;
        }

        public void Read(AddonDataProvider reader)
        {
            int data = reader.GetInt(cTalent);
            if (data == 0 || Talents.ContainsKey(data)) return;

            int hash = data;

            int tab = (int)(data / 1000000f);
            data -= 1000000 * tab;

            int tier = (int)(data / 10000f);
            data -= 10000 * tier;

            int column = (int)(data / 10f);
            data -= 10 * column;

            var talent = new Talent
            {
                Hash = hash,
                TabNum = tab,
                TierNum = tier,
                ColumnNum = column,
                CurrentRank = data
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
            foreach (var kvp in Talents)
            {
                if (kvp.Value.CurrentRank >= rank &&
                    kvp.Value.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
