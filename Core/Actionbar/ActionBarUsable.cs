namespace Core
{
    public class ActionBarUsable
    {
        private readonly ActionBarUsableStatus ActionBarUseable_1To24;
        private readonly ActionBarUsableStatus ActionBarUseable_25To48;
        private readonly ActionBarUsableStatus ActionBarUseable_49To72;
        private readonly ActionBarUsableStatus ActionBarUseable_73To96;

        public ActionBarUsable(ISquareReader reader, int idx1, int idx2, int idx3, int idx4)
        {
            ActionBarUseable_1To24 = new ActionBarUsableStatus(reader.GetLongAtCell(idx1));
            ActionBarUseable_25To48 = new ActionBarUsableStatus(reader.GetLongAtCell(idx2));
            ActionBarUseable_49To72 = new ActionBarUsableStatus(reader.GetLongAtCell(idx3));
            ActionBarUseable_73To96 = new ActionBarUsableStatus(reader.GetLongAtCell(idx4));
        }

        // https://wowwiki-archive.fandom.com/wiki/ActionSlot
        // valid range 1-96
        public bool ActionUsable(string keyName)
        {
            if (KeyReader.ActionBarSlotMap.TryGetValue(keyName, out var slot))
            {
                if (slot < 24)
                    return ActionBarUseable_1To24.IsBitSet(slot - 1);
                if (slot < 48)
                    return ActionBarUseable_25To48.IsBitSet(slot - 1);
                if (slot < 72)
                    return ActionBarUseable_49To72.IsBitSet(slot - 1);
                if (slot < 96)
                    return ActionBarUseable_73To96.IsBitSet(slot - 1);
            }

            return false;
        }

    }
}
