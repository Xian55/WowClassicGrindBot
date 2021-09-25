using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SharedLib;
using SharedLib.Data;

namespace Core.Database
{
    public class WorldMapAreaDB
    {
        private readonly ILogger logger;
        private Dictionary<int, WorldMapArea> areas = new Dictionary<int, WorldMapArea>();

        public WorldMapAreaDB(ILogger logger, DataConfig dataConfig)
        {
            this.logger = logger;

            var list = JsonConvert.DeserializeObject<List<WorldMapArea>>(File.ReadAllText(Path.Join(dataConfig.WorldToMap, "WorldMapArea.json")));
            list.ForEach(x =>
            {
                if (!areas.ContainsKey(x.UIMapId))
                    areas.Add(x.UIMapId, x);
            });
        }

        public int GetAreaId(int uiMapId)
        {
            if(areas.TryGetValue(uiMapId, out var map))
            {
                return map.AreaID;
            }

            return -1;
        }

        public Vector3 GetWorldLocation(int uiMapId, WowPoint p, bool flipXY)
        {
            var worldMapArea = areas[uiMapId];
            if (worldMapArea == null)
            {
                logger.LogError($"Unsupported mini map area, UIMapId {uiMapId} not found in WorldMapArea.json");
                return Vector3.Zero;
            }

            if(flipXY)
            {
                return new Vector3(worldMapArea.ToWorldX((float)p.Y), worldMapArea.ToWorldY((float)p.X), 0);
            }
            else
            {
                var worldX = worldMapArea.ToWorldX((float)p.X);
                var worldY = worldMapArea.ToWorldY((float)p.Y);
                return new Vector3(worldX, worldY, 0);
            }
        }

        public WorldMapAreaSpot ToMapAreaSpot(float x, float y, float z, string continent, int mapHint)
        {
            var area = WorldMapAreaFactory.GetWorldMapArea(areas.Values.ToList(), x, y, continent, mapHint);
            if(area == null)
            {
                return new WorldMapAreaSpot();
            }

            return new WorldMapAreaSpot
            {
                Y = area.ToMapX(x),
                X = area.ToMapY(y),
                Z = z,
                MapID = area.UIMapId
            };
        }
        
        public WorldMapArea? Get(int uiMapId)
        {
            if (areas.TryGetValue(uiMapId, out var map))
            {
                return map;
            }

            return null;
        }
    }
}
