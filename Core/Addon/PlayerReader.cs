using Core.Database;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Core
{
    public partial class PlayerReader
    {
        private readonly ISquareReader reader;
        private readonly CreatureDB creatureDb;

        public PlayerReader(ISquareReader reader, CreatureDB creatureDb)
        {
            this.reader = reader;
            this.creatureDb = creatureDb;
        }

        public int Sequence { get; private set; } = 0;
        public List<CombatCreature> TargetHistory { get; } = new List<CombatCreature>();
        public List<CombatCreature> CombatCreatures { get; } = new List<CombatCreature>();

        public WowPoint PlayerLocation => new WowPoint(XCoord, YCoord);

        public double XCoord => reader.GetFixedPointAtCell(1) * 100;
        public double YCoord => reader.GetFixedPointAtCell(2) * 100;
        public double Direction => reader.GetFixedPointAtCell(3);

        public string Zone => reader.GetStringAtCell(4) + reader.GetStringAtCell(5); // Checks current geographic zone

        public WowPoint CorpseLocation => new WowPoint(CorpseX, CorpseY);

        // gets the position of your corpse where you died
        public double CorpseX => reader.GetFixedPointAtCell(6) * 10;

        public double CorpseY => reader.GetFixedPointAtCell(7) * 10;

        public PlayerBitValues PlayerBitValues => new PlayerBitValues(reader.GetLongAtCell(8));

        // Player health and mana
        public long HealthMax => reader.GetLongAtCell(10); // Maximum amount of health of player

        public long HealthCurrent => reader.GetLongAtCell(11); // Current amount of health of player
        public long HealthPercent => HealthMax == 0 || HealthCurrent == 1 ? 0 : (HealthCurrent * 100) / HealthMax; // Health in terms of a percentage

        public long ManaMax => reader.GetLongAtCell(12); // Maximum amount of mana
        public long ManaCurrent => reader.GetLongAtCell(13); // Current amount of mana
        public long ManaPercentage => ManaMax == 0 ? 0 : (ManaCurrent * 100) / ManaMax; // Mana in terms of a percentage

        public long PlayerLevel => reader.GetLongAtCell(14); // Level is our character's exact level ranging from 1-60

        public bool IsInMeleeRange => MinRange == 0 && MaxRange == 5;
        public bool IsInDeadZone => MinRange == 5 && MaxRange == 15;

        public long MinRange => (long)(reader.GetLongAtCell(15) / 100000f);
        public long MaxRange => (long)((reader.GetLongAtCell(15)-(MinRange*100000f)) / 100f);

        public string Target
        {
            get
            {
                if (TargetId > 0 && creatureDb.Entries.ContainsKey(this.TargetId))
                {
                    return creatureDb.Entries[this.TargetId].Name;
                }
                return reader.GetStringAtCell(16) + (reader.GetStringAtCell(17));
            }
        }

        public long TargetMaxHealth => reader.GetLongAtCell(18);

        // Targets current percentage of health
        public long TargetHealthPercentage => TargetMaxHealth == 0 || TargetHealth == 1 ? 0 : (TargetHealth * 100) / TargetMaxHealth;

        public long TargetHealth => reader.GetLongAtCell(19);

        public bool HasTarget => !string.IsNullOrEmpty(Target) || TargetHealth > 0;

        // 32 - 33
        public long Gold => reader.GetLongAtCell(32) + reader.GetLongAtCell(33) * 1000000;

        // 34 -36 Loops through binaries of three pixels. Currently does 24 slots. 1-12 and 61-72.
        public ActionBarUsable ActionBarUsable => new ActionBarUsable(reader, 34, 35, 36, 42);

        // 37- 40 Bag Slots - BadReader handles it

        public BuffStatus Buffs => new BuffStatus(reader.GetLongAtCell(41));
        public DebuffStatus Debuffs => new DebuffStatus(reader.GetLongAtCell(55));

        public long TargetLevel => reader.GetLongAtCell(43);

        // cell 44 reserved to ActionBarCostReader

        public PlayerClassEnum PlayerClass => (PlayerClassEnum)reader.GetLongAtCell(46);

        public bool Unskinnable => reader.GetLongAtCell(47) != 0; // Returns 1 if creature is unskinnable

        public ShapeshiftForm Druid_ShapeshiftForm => (ShapeshiftForm)reader.GetLongAtCell(48);

        public long PlayerXp => reader.GetLongAtCell(50);
        public long PlayerMaxXp => reader.GetLongAtCell(51);
        public long PlayerXpPercentage => (PlayerXp * 100) / (PlayerMaxXp == 0 ? 1 : PlayerMaxXp);

        private long UIErrorMessage => reader.GetLongAtCell(52);
        public UI_ERROR LastUIErrorMessage { get; set; }

        private SpellInRange spellInRange = new SpellInRange(0);

        public SpellInRange SpellInRange
        {
            get
            {
                var x = reader.GetLongAtCell(49);
                if (x < 1024) // ignore odd values
                {
                    spellInRange = new SpellInRange(x);
                }
                return spellInRange;
            }
        }

        public bool WithInPullRange => SpellInRange.WithinPullRange(this, this.PlayerClass);
        public bool WithInCombatRange => SpellInRange.WithinCombatRange(this, this.PlayerClass);

        public bool IsAutoAttacking => PlayerBitValues.IsAutoRepeatSpellOn_AutoAttack;
        public bool IsShooting => PlayerBitValues.IsAutoRepeatSpellOn_Shoot;

        public long SpellBeingCast => reader.GetLongAtCell(53);
        public long ComboPoints => reader.GetLongAtCell(54);

        public int TargetId => (int)reader.GetLongAtCell(56);
        public long TargetGuid => reader.GetLongAtCell(57);

        public int ZoneId => (int)reader.GetLongAtCell(58);

        public bool IsCasting => SpellBeingCast != 0;

        public TargetTargetEnum TargetTarget => (TargetTargetEnum)reader.GetLongAtCell(59);

        public bool TargetIsFrostbitten => this.PlayerClass == PlayerClassEnum.Mage && this.Debuffs.Frostbite;

        public int LastDamageDealerGuid => (int)reader.GetLongAtCell(66);

        public int LastKilledGuid => (int)reader.GetLongAtCell(67);

        public int PetGuid => (int)reader.GetLongAtCell(68);
        public int PetTargetGuid => (int)reader.GetLongAtCell(69);
        public bool PetHasTarget => PetTargetGuid != 0;

        #region Combat Creatures
        public int CombatCreatureCount => CombatCreatures.Count;

        public void UpdateCreatureLists()
        {
            CombatCreature.UpdateCombatCreatureCount((int)reader.GetLongAtCell(65), CombatCreatures);
            CombatCreature.UpdateCombatCreatureCount((int)TargetGuid, TargetHistory);
        }

        #endregion


        #region Last Combat Kill Count

        private int lastCombatKillCount;
        public int LastCombatKillCount => lastCombatKillCount;

        public void IncrementKillCount()
        {
            lastCombatKillCount++;
        }

        public void DecrementKillCount()
        {
            lastCombatKillCount--;
            if(lastCombatKillCount < 0)
            {
                ResetKillCount();
            }
        }

        public void ResetKillCount()
        {
            lastCombatKillCount = 0;
        }

        #endregion


        #region Corpse Consumption

        private bool shouldConsumeCorpse;
        public bool ShouldConsumeCorpse => shouldConsumeCorpse;

        public void ProduceCorpse()
        {
            shouldConsumeCorpse = true;
        }

        public void ConsumeCorpse()
        {
            shouldConsumeCorpse = false;
        }

        #endregion


        internal void Updated()
        {
            Sequence++;

            if (UIErrorMessage > 0)
            {
                LastUIErrorMessage = (UI_ERROR)UIErrorMessage;
            }
        }

        internal async Task WaitForUpdate()
        {
            var s = this.Sequence;
            while (this.Sequence == s)
            {
                await Task.Delay(100);
            }
        }
        public async Task WaitForNUpdate(int n)
        {
            var s = this.Sequence;
            while (this.Sequence <= s + n)
            {
                await Task.Delay(100);
            }
        }

    }
}