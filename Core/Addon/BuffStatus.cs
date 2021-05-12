namespace Core
{
    public class BuffStatus
    {
        private long value;

        public BuffStatus(string name)
        {
            this.name = name;
            this.value = 0;
        }

        public BuffStatus(long value)
        {
            this.value = value;
        }

        public bool IsBitSet(int pos)
        {
            return (value & (1 << pos)) != 0;
        }

        public string name { get; set; } = string.Empty;

        // All
        public bool Eating { get => IsBitSet(0); }
        public bool Drinking { get => IsBitSet(1); }
        public bool WellFed { get => IsBitSet(2); }
        public bool ManaRegeneration { get => IsBitSet(3); }

        // Priest
        public bool Fortitude { get => IsBitSet(10); }

        public bool InnerFire { get => IsBitSet(11); }
        public bool Renew { get => IsBitSet(12); }
        public bool Shield { get => IsBitSet(13); }
        public bool DivineSpirit { get => IsBitSet(14); }

        // Druid
        public bool MarkOfTheWild { get => IsBitSet(10); }
        public bool Thorns { get => IsBitSet(11); }
        public bool TigersFury { get => IsBitSet(12); }

        // Paladin
        public bool Aura { get => IsBitSet(10); }

        public bool Blessing { get => IsBitSet(11); }
        public bool Seal { get => IsBitSet(12); }

        // Mage
        public bool FrostArmor { get => IsBitSet(10); }

        public bool ArcaneIntellect { get => IsBitSet(11); }
        public bool IceBarrier { get => IsBitSet(12); }
        public bool Ward { get => IsBitSet(13); }
        public bool FirePower { get => IsBitSet(14); }

        // Rogue
        public bool SliceAndDice { get => IsBitSet(10); }

        // Warrior
        public bool BattleShout { get => IsBitSet(10); }

        // Warlock
        public bool Demon { get => IsBitSet(10); } //Skin and Armor

        public bool SoulLink { get => IsBitSet(11); }
        public bool SoulstoneResurrection { get => IsBitSet(12); }
        public bool ShadowTrance { get => IsBitSet(13); }

        // Shaman
        public bool LightningShield { get => IsBitSet(10); }
    }
}