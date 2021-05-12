namespace Core
{
    public class PlayerBitValues
    {
        private long value;

        public PlayerBitValues(long value)
        {
            this.value = value;
        }

        public bool IsBitSet(int pos)
        {
            return (value & (1 << pos)) != 0;
        }

        public bool IsTagged { get => IsBitSet(22); }
        public bool TargetIsNormal { get => IsBitSet(21); }
        public bool IsAutoRepeatSpellOn_AutoAttack { get => IsBitSet(20); }
        public bool IsAutoRepeatSpellOn_Shoot { get => IsBitSet(19); }
        public bool IsMounted { get => IsBitSet(18); }
        public bool ProcessExitStatus { get => IsBitSet(17); }

        public bool TargetOfTargetIsPlayer { get => IsBitSet(15); }
        public bool PlayerInCombat { get => IsBitSet(14); }

        public bool IsSwimming { get => IsBitSet(11); }
        public bool IsFlying { get => IsBitSet(10); }
        public bool ItemsAreBroken { get => IsBitSet(9); }

        public bool HasPet { get => IsBitSet(6); }

        public bool IsInMeleeRange { get => IsBitSet(4); }

        public bool TalentPoints { get => IsBitSet(3); }
        public bool DeadStatus { get => IsBitSet(2); }
        public bool TargetIsDead { get => IsBitSet(1); }
        public bool TargetInCombat { get => IsBitSet(0); }

        public bool MainHandEnchant_Active { get => IsBitSet(7); }
        public bool OffHandEnchant_Active { get => IsBitSet(8); }
    }
}