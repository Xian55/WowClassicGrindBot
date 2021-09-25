using System.Diagnostics;
using System.Text;

namespace Core
{
    public class ActionBarUsableStatus
    {
        private readonly long value;

        public ActionBarUsableStatus(long value)
        {
            this.value = value;
        }

        public bool IsBitSet(int pos)
        {
            return (value & (1 << (pos % 24))) != 0;
        }

        public bool HotKey1 { get => IsBitSet(0); }
        public bool HotKey2 { get => IsBitSet(1); }
        public bool HotKey3 { get => IsBitSet(2); }
        public bool HotKey4 { get => IsBitSet(3); }
        public bool HotKey5 { get => IsBitSet(4); }
        public bool HotKey6 { get => IsBitSet(5); }
        public bool HotKey7 { get => IsBitSet(6); }
        public bool HotKey8 { get => IsBitSet(7); }
        public bool HotKey9 { get => IsBitSet(8); }
        public bool HotKey10 { get => IsBitSet(9); }
        public bool HotKey11 { get => IsBitSet(10); }
        public bool HotKey12 { get => IsBitSet(11); }

        public bool HotKey13 { get => IsBitSet(12); }
        public bool HotKey14 { get => IsBitSet(13); }
        public bool HotKey15 { get => IsBitSet(14); }
        public bool HotKey16 { get => IsBitSet(15); }
        public bool HotKey17 { get => IsBitSet(16); }
        public bool HotKey18 { get => IsBitSet(17); }
        public bool HotKey19 { get => IsBitSet(18); }
        public bool HotKey20 { get => IsBitSet(19); }
        public bool HotKey21 { get => IsBitSet(20); }
        public bool HotKey22 { get => IsBitSet(21); }
        public bool HotKey23 { get => IsBitSet(22); }
        public bool HotKey24 { get => IsBitSet(23); }

        internal void Dump()
        {
            var sb = new StringBuilder();
            for (int i = 1; i < 12; i++)
            {
                sb.Append($"{i}:{IsBitSet(i - 1)},");
            }
            Debug.WriteLine(sb.ToString());
        }
    }
}