using System;
using System.Drawing;

namespace Core.Addon
{
    public class ConfigAddonReader : IAddonReader
    {
        public PlayerReader PlayerReader { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public BagReader BagReader { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public EquipmentReader equipmentReader { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public bool Active { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public LevelTracker LevelTracker { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public ActionBarCostReader ActionBarCostReader { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        private void ConfigAddonReader_AddonDataChanged(object sender, EventArgs e)
        {
            throw new NotImplementedException();
        }

        public Color GetColorAt(int index)
        {
            throw new NotImplementedException();
        }

        public void Refresh()
        {
            throw new NotImplementedException();
        }
    }
}