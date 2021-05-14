using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Addon;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Core.Database
{
    public class ItemDB
    {
        private readonly ILogger logger;
        private readonly DataConfig dataConfig;

        public Dictionary<int, Item> Items { get; } = new Dictionary<int, Item>();
        public HashSet<int> FoodIds { get; } = new HashSet<int>();
        public HashSet<int> WaterIds { get; } = new HashSet<int>();

        public HashSet<int> ContainerIds { get; } = new HashSet<int>();


        public ItemDB(ILogger logger, DataConfig dataConfig)
        {
            this.logger = logger;
            this.dataConfig = dataConfig;

            var ahPrices = ReadAHPrices();

            var items = JsonConvert.DeserializeObject<List<Item>>(File.ReadAllText(Path.Join(dataConfig.Dbc, "items.json")));
            items.ForEach(i =>
            {
                Items.Add(i.Entry, i);
                i.AHPrice = ahPrices.ContainsKey(i.Entry) ? ahPrices[i.Entry] : i.SellPrice;
            });

            var foods = JsonConvert.DeserializeObject<List<ItemId>>(File.ReadAllText(Path.Join(dataConfig.Dbc, "foods.json")));
            foods.ForEach(x => FoodIds.Add(x.Id));

            var waters = JsonConvert.DeserializeObject<List<ItemId>>(File.ReadAllText(Path.Join(dataConfig.Dbc, "waters.json")));
            waters.ForEach(x => WaterIds.Add(x.Id));

            var containers = File.ReadAllLines(Path.Join(dataConfig.Dbc, "containers.txt"));
            Array.ForEach(containers, (id) =>
            {
                ContainerIds.Add(int.Parse(id));
            });
        }

        public bool IsContainer(int itemId)
        {
            return ContainerIds.Contains(itemId);
        }


        #region AH prices

        private Dictionary<int, int> ReadAHPrices()
        {
            var auctionHouseDictionary = new Dictionary<int, int>();

            var filePath = @"D:\GitHub\Auc-Stat-Simple.lua.location";
            if (File.Exists(filePath))
            {
                try
                {
                    // file will contain be something like D:\World of Warcraft Classic\World of Warcraft\_classic_\WTF\Account\XXXXXXX\SavedVariables\Auc-Stat-Simple.lua
                    var filename = File.ReadAllLines(filePath).FirstOrDefault();
                    File.ReadAllLines(filename)
                        .Select(l => l.Trim())
                        .Where(l => l.StartsWith("["))
                        .Where(l => l.Contains(";"))
                        .Where(l => l.Contains("="))
                        .Select(Process)
                        .GroupBy(l => l.Key)
                        .Select(g => g.First())
                        .ToList()
                        .ForEach(i => auctionHouseDictionary.Add(i.Key, i.Value));
                }
                catch (Exception ex)
                {
                    this.logger.LogError("Failed to read AH prices." + ex.Message);
                }
            }
            else
            {
                this.logger.LogWarning("AH prices unavailable, don't worry about this message!");
            }

            return auctionHouseDictionary;
        }

        public static KeyValuePair<int, int> Process(string line)
        {
            var parts = line.Split("=");
            var id = int.Parse(parts[0].Replace("[", "").Replace("]", "").Replace("\"", "").Trim());
            var valueParts = parts[1].Split("\"")[1].Split(";");

            double value = 0;
            if (valueParts.Length == 3 || valueParts.Length == 6)
            {
                value = double.Parse(valueParts[valueParts.Length - 1]);
            }
            else if (valueParts.Length == 4 || valueParts.Length == 7)
            {
                value = double.Parse(valueParts[valueParts.Length - 2]);
            }

            return new KeyValuePair<int, int>(id, (int)value);
        }

        #endregion
    }
}
