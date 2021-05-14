using System.Linq;

namespace Core
{
    public enum InventorySlotId
    {
        Ammo = 0,
        Head = 1,
        Neck = 2,
        Shoulder = 3,
        Shirt = 4,
        Chest = 5,
        Waist = 6,
        Legs = 7,
        Feet = 8,
        Wrist = 9,
        Hands = 10,
        Finger_1 = 11,
        Finger_2 = 12,
        Trinket_1 = 13,
        Trinket_2 = 14,
        Back = 15,
        Main_hand = 16,
        Off_hand = 17,
        Ranged = 18,
        Tabard = 19,
        Bag_0 = 20,
        Bag_1 = 21,
        Bag_2 = 22,
        Bag_3 = 23
    }

    public class EquipmentReader
    {
        private const int MAX_EQUIPMENT_COUNT = 24;

        private readonly int cellStart;
        private readonly ISquareReader reader;

        private readonly long[] equipment = new long[MAX_EQUIPMENT_COUNT];

        public EquipmentReader(ISquareReader reader, int cellStart)
        {
            this.cellStart = cellStart;
            this.reader = reader;
        }

        public void Read()
        {
            var index = reader.GetLongAtCell(cellStart + 1);
            if (index < MAX_EQUIPMENT_COUNT && index >= 0)
            {
                equipment[index] = reader.GetLongAtCell(cellStart);
            }
        }

        public string ToStringList()
        {
            return string.Join(", ", equipment.Where(i => i > 0));
        }

        public bool HasRanged()
        {
            return equipment[(int)InventorySlotId.Ranged] != 0;
        }

        public bool HasItem(int itemId)
        {
            for(int i=0; i<equipment.Length; i++)
            {
                if (equipment[i] == itemId)
                    return true;
            }

            return false;
        }

        public int GetId(int slot)
        {
            return (int)equipment[slot];
        }
    }
}