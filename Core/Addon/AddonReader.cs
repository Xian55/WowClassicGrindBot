using Core.Database;
using Microsoft.Extensions.Logging;
using SharedLib;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Core
{
    public sealed class AddonReader : IAddonReader, IDisposable
    {
        private readonly ILogger logger;
        private readonly ISquareReader squareReader;
        private readonly IAddonDataProvider addonDataProvider;
        public bool Active { get; set; } = true;
        public PlayerReader PlayerReader { get; set; }
        public BagReader BagReader { get; set; }
        public EquipmentReader equipmentReader { get; set; }

        public ActionBarCostReader ActionBarCostReader { get; set; }
        public LevelTracker LevelTracker { get; set; }

        public event EventHandler? AddonDataChanged;

        private readonly AreaDB areaDb;
        private readonly WorldMapAreaDB worldMapAreaDb;
        private readonly ItemDB itemDb;
        private readonly CreatureDB creatureDb;

        public AddonReader(ILogger logger, DataConfig dataConfig, AreaDB areaDb, IAddonDataProvider addonDataProvider)
        {
            this.logger = logger;
            this.addonDataProvider = addonDataProvider;

            this.squareReader = new SquareReader(this);

            this.itemDb = new ItemDB(logger, dataConfig);
            this.creatureDb = new CreatureDB(logger, dataConfig);

            this.equipmentReader = new EquipmentReader(squareReader, 30);
            this.BagReader = new BagReader(squareReader, 20, itemDb, equipmentReader);
            this.ActionBarCostReader = new ActionBarCostReader(squareReader, 44);
            this.PlayerReader = new PlayerReader(squareReader, creatureDb);
            this.LevelTracker = new LevelTracker(PlayerReader);

            this.areaDb = areaDb;
            this.worldMapAreaDb = new WorldMapAreaDB(logger, dataConfig);
        }

        private int seq = 0;

        public void AddonRefresh()
        {
            Refresh();

            // 20 - 29
            BagReader.Read();

            // 30 - 31
            equipmentReader.Read();

            // 44
            ActionBarCostReader.Read();

            LevelTracker.Update();

            PlayerReader.UpdateCreatureLists();

            areaDb.Update(worldMapAreaDb.GetAreaId(PlayerReader.ZoneId));

            seq++;

            if (seq >= 10)
            {
                seq = 0;
                AddonDataChanged?.Invoke(this, new EventArgs());
            }
        }

        public void Refresh()
        {
            addonDataProvider.Update();
            PlayerReader.Updated();
        }

        public Color GetColorAt(int index)
        {
            return addonDataProvider.GetColor(index);
        }

        public void Dispose()
        {
            addonDataProvider?.Dispose();
        }
    }
}