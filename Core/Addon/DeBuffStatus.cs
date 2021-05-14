namespace Core
{
    public class DebuffStatus
    {
        private long value;

        public DebuffStatus(string name)
        {
            this.name = name;
            this.value = 0;
        }

        public DebuffStatus(long value)
        {
            this.value = value;
        }

        public bool IsBitSet(int pos)
        {
            return (value & (1 << pos)) != 0;
        }

        public string name { get; set; } = string.Empty;

        // Priest
        public bool ShadowWordPain { get => IsBitSet(0); }

        // Druid
        public bool Roar { get => IsBitSet(0); }
        public bool FaerieFire { get => IsBitSet(1); }
        public bool Rip { get => IsBitSet(2); }

        // Paladin

        // Mage
        public bool Frostbite { get => IsBitSet(0); }

        // Rogue

        // Warrior
        public bool Rend { get => IsBitSet(0); }

        // Warlock
        public bool Curseof { get => IsBitSet(0); }
        public bool Corruption { get => IsBitSet(1); }
        public bool Immolate { get => IsBitSet(2); }
        public bool SiphonLife { get => IsBitSet(3); }

        // Hunter
        public bool SerpentSting { get => IsBitSet(0); }
    }
}