using Core.Database;
using Microsoft.Extensions.Logging;
using System;
using System.Drawing;
using Cyotek.Collections;
using Cyotek.Collections.Generic;

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
        public event EventHandler? ZoneChanged;

        private readonly AreaDB areaDb;
        public WorldMapAreaDB WorldMapAreaDb { get; set; }
        private readonly ItemDB itemDb;
        private readonly CreatureDB creatureDb;


        private int seq = 0;

        private long lastGlobalTime = 0;
        private DateTime lastGlobalTimeChange = DateTime.Now;
        private readonly CircularBuffer<double> UpdateLatencys;

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
            this.WorldMapAreaDb = new WorldMapAreaDB(logger, dataConfig);

            UpdateLatencys = new CircularBuffer<double>(10);
        }

        public void AddonRefresh()
        {
            int uiMapId = PlayerReader.UIMapId;
            Refresh();
            if(seq == 0 || uiMapId != PlayerReader.UIMapId)
                ZoneChanged?.Invoke(this, EventArgs.Empty);

            // 20 - 29
            BagReader.Read();

            // 30 - 31
            equipmentReader.Read();

            // 44
            ActionBarCostReader.Read();

            LevelTracker.Update();

            PlayerReader.UpdateCreatureLists();

            areaDb.Update(WorldMapAreaDb.GetAreaId(PlayerReader.UIMapId));

            seq++;

            if (PlayerReader.GlobalTime != lastGlobalTime)
            {
                UpdateLatencys.Put((DateTime.Now - lastGlobalTimeChange).TotalMilliseconds);

                lastGlobalTime = PlayerReader.GlobalTime;
                lastGlobalTimeChange = DateTime.Now;
            }

            if (seq >= 50) // Thread 10ms delay => 500ms
            {
                seq = 0;
                AddonDataChanged?.Invoke(this, new EventArgs());

                PlayerReader.AvgUpdateLatency = 0;
                for (int i = 0; i < UpdateLatencys.Size; i++)
                {
                    PlayerReader.AvgUpdateLatency += UpdateLatencys.PeekAt(i);
                }
                PlayerReader.AvgUpdateLatency /= UpdateLatencys.Size;
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