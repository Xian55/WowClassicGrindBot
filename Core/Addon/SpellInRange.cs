using System;

namespace Core
{
    public class SpellInRange
    {
        private readonly long value;

        public SpellInRange(long value)
        {
            this.value = value;
        }

        public bool IsBitSet(int pos)
        {
            return (value & (1 << pos)) != 0;
        }

        // Warrior
        public bool Warrior_Charge { get => IsBitSet(0); }

        public bool Warrior_Rend { get => IsBitSet(1); }
        public bool Warrior_ShootGun { get => IsBitSet(2); }

        // Rogue
        public bool Rogue_SinisterStrike { get => IsBitSet(0); }

        public bool Rogue_Throw { get => IsBitSet(1); }
        public bool Rogue_ShootGun { get => IsBitSet(2); }

        // Priest
        public bool Priest_ShadowWordPain { get => IsBitSet(0); }

        public bool Priest_MindBlast { get => IsBitSet(1); }
        public bool Priest_MindFlay { get => IsBitSet(2); }
        public bool Priest_Shoot { get => IsBitSet(3); }

        // Druid
        public bool Druid_Wrath { get => IsBitSet(0); }
        public bool Druid_Bash { get => IsBitSet(1); }
        public bool Druid_Rip { get => IsBitSet(2); }

        //Paladin
        public bool Paladin_Judgement { get => IsBitSet(0); }

        //Mage
        public bool Mage_Fireball { get => IsBitSet(0); }

        public bool Mage_Shoot { get => IsBitSet(1); }
        public bool Mage_Pyroblast { get => IsBitSet(2); }
        public bool Mage_Frostbolt { get => IsBitSet(3); }
        public bool Mage_Fireblast { get => IsBitSet(4); }

        //Hunter
        public bool Hunter_RaptorStrike { get => IsBitSet(0); }

        public bool Hunter_AutoShoot { get => IsBitSet(1); }

        public bool Hunter_SerpentSting { get => IsBitSet(2); }

        // Warlock
        public bool Warlock_ShadowBolt { get => IsBitSet(0); }

        public bool Warlock_Shoot { get => IsBitSet(1); }

        // Shaman
        public bool Shaman_LightningBolt { get => IsBitSet(0); }
        public bool Shaman_EarthShock { get => IsBitSet(1); }

        public bool WithinPullRange(PlayerReader playerReader, PlayerClassEnum playerClass) => playerClass switch
        {
            PlayerClassEnum.Warrior => Warrior_Charge,
            PlayerClassEnum.Rogue => Rogue_Throw,
            PlayerClassEnum.Priest => Priest_ShadowWordPain,
            PlayerClassEnum.Druid => playerReader.Druid_ShapeshiftForm == ShapeshiftForm.Druid_Bear ? Druid_Bash : playerReader.Druid_ShapeshiftForm == ShapeshiftForm.Druid_Cat ? Druid_Rip : Druid_Wrath,
            PlayerClassEnum.Mage => (playerReader.PlayerLevel >= 4 && Mage_Frostbolt) || Mage_Fireball,
            PlayerClassEnum.Hunter => (playerReader.PlayerLevel >=4 && Hunter_SerpentSting) || Hunter_AutoShoot,
            PlayerClassEnum.Warlock => Warlock_ShadowBolt,
            PlayerClassEnum.Shaman => (playerReader.PlayerLevel >= 4 && Shaman_EarthShock) || Shaman_LightningBolt,
            _ => true
        };

        public bool WithinCombatRange(PlayerReader playerReader, PlayerClassEnum playerClass) => playerClass switch
        {
            PlayerClassEnum.Warrior => Warrior_Rend,
            PlayerClassEnum.Rogue => Rogue_SinisterStrike,
            PlayerClassEnum.Priest => Priest_Shoot,
            PlayerClassEnum.Druid => playerReader.Druid_ShapeshiftForm == ShapeshiftForm.Druid_Bear ? Druid_Bash : playerReader.Druid_ShapeshiftForm == ShapeshiftForm.Druid_Cat ? Druid_Rip : Druid_Wrath,
            PlayerClassEnum.Paladin => Paladin_Judgement,
            PlayerClassEnum.Mage => Mage_Frostbolt || Mage_Fireball,
            PlayerClassEnum.Hunter => Hunter_SerpentSting || Hunter_AutoShoot || Hunter_RaptorStrike,
            PlayerClassEnum.Warlock => Warlock_ShadowBolt,
            PlayerClassEnum.Shaman => Shaman_LightningBolt,
            _ => true
        };
    }
}