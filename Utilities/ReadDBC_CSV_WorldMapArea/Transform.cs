using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ReadDBC_CSV_WorldMapArea
{
    public class Transform
    {
        public const string path = "../../../data/";

        List<string> acceptedOverride = new List<string> { "Hellfire", "Kalimdor" };

        public WorldMapArea CreateV2(string[] values)
        {
            //https://wow.tools/dbc/?dbc=worldmaparea&build=2.0.0.5610#page=1
            //https://wow.tools/dbc/?dbc=worldmaparea&build=2.4.3.8606#page=1
            return new WorldMapArea
            {
                ID = int.Parse(values[0]),
                MapID = int.Parse(values[1]),
                AreaID = int.Parse(values[2]),
                AreaName = values[3],
                LocLeft = float.Parse(values[4]),
                LocRight = float.Parse(values[5]),
                LocTop = float.Parse(values[6]),
                LocBottom = float.Parse(values[7]),
            };
        }
        public WorldMapArea CreateV1(string[] values)
        {
            //https://wow.tools/dbc/?dbc=worldmaparea&build=1.13.0.28377
            return new WorldMapArea
            {
                AreaName = values[0],
                LocLeft = float.Parse(values[1]),
                LocRight = float.Parse(values[2]),
                LocTop = float.Parse(values[3]),
                LocBottom = float.Parse(values[4]),

                MapID = int.Parse(values[6]),
                AreaID = int.Parse(values[7]),

                ID = int.Parse(values[15]),

            };
        }

        public List<WorldMapArea> Validate()
        {
            Console.WriteLine("\nResult:");

            var list = JsonConvert.DeserializeObject<List<WorldMapArea>>(File.ReadAllText(Path.Join(path, "WorldMapArea.json")));
            Console.WriteLine("Unsupported mini maps areas: " + string.Join(", ", list.Where(l => l.UIMapId == 0).Select(s => s.AreaName).OrderBy(s => s)));

            var duplicates = list.GroupBy(s => s.MapID).Where(g => g.Count() > 1).Select(g => g.Key);
            Console.WriteLine("Duplicated 'MapID' (continents accepted): " + string.Join(", ", duplicates.ToArray()));

            return list;
        }

        public List<WorldMapArea> CreateWorldMapAreaJson()
        {
            var list = File.ReadAllLines(Path.Join(path, "WorldMapArea.csv")).ToList().Skip(1).Select(l => l.Split(","))
                .Select(l => CreateV2(l))
                .ToList();

            CorrectTypos(ref list);

            var uimapLines = File.ReadAllLines(Path.Join(path, "uimap.csv")).ToList().Select(l => l.Split(","));
            list.ForEach(wmp => PopulateUIMap(wmp, uimapLines));
            File.WriteAllText(Path.Join(path, "WorldMapArea.json"), JsonConvert.SerializeObject(list, Formatting.Indented));

            return list;
        }

        public void PopulateUIMap(WorldMapArea area, IEnumerable<string[]> uimapLines)
        {
            var kalimdor = uimapLines.Where(s => s[0] == "Kalimdor").Select(s => s[1]).FirstOrDefault();

            // two outland occurrences need the last one
            var outland = uimapLines.Where(s => s[0] == "Outland").Select(s => s[1]).LastOrDefault();

            var matches = uimapLines.Where(s => Matches(area, s))
                .ToList();

            if (matches.Count > 1)
            {
                Console.WriteLine($"\n- WARN [{area.AreaName}] has more than one matches:\n {string.Join(",\n ", matches.Select(t => new { AreaName = t[0], UIMapId = t[1] }))}");
            }

            if (matches.Count == 0)
            {
                Console.WriteLine($"\n- WARN [{area.AreaName}] has no matches!");
            }

            matches.ForEach(a =>
            {
                if (area.UIMapId == 0 || acceptedOverride.Contains(area.AreaName))
                {
                    if(area.UIMapId != 0 && acceptedOverride.Contains(area.AreaName))
                    {
                        Console.WriteLine($" - Accepted override [{area.AreaName}] from [{area.UIMapId}] to [{int.Parse(a[1])}]");
                    }

                    area.UIMapId = int.Parse(a[1]);
                }
                else
                {
                    Console.WriteLine($" - Prevented override [{area.AreaName}] from [{area.UIMapId}] to [{int.Parse(a[1])}]");
                }

                area.Continent = a[2] == outland ? "Expansion01" : (a[2] == kalimdor ? "Kalimdor" : "Azeroth");
            });
        }

        /// <summary>
        /// Fix occurance where Stormwind and Stormwind city didn't match
        /// </summary>
        /// <param name="area"></param>
        /// <param name="s"></param>
        /// <returns></returns>
        private bool Matches(WorldMapArea area, string[] s)
        {
            var areaname = s[0].Replace(" ", "").Replace("'", "");
            var areaname1 = area.AreaName.Replace(" ", "").Replace("'", "");
            return areaname.StartsWith(areaname1, StringComparison.InvariantCultureIgnoreCase)
                 || areaname1.StartsWith(areaname, StringComparison.InvariantCultureIgnoreCase);
        }

        private void CorrectTypos(ref List<WorldMapArea> list)
        {
            // Unsupported mini maps areas: Aszhara, Barrens, Darnassis, Expansion01, Hilsbrad, Hinterlands, Ogrimmar, Sunwell
            // Unsupported mini maps areas: Expansion01, Sunwell
            for (int i =0; i<list.Count; i++)
            {
                // typo :dense:
                if (list[i].AreaName == "Aszhara")
                    list[i].AreaName = "Azshara";

                if (list[i].AreaName == "Darnassis")
                    list[i].AreaName = "Darnassus";

                if (list[i].AreaName == "Hilsbrad")
                    list[i].AreaName = "Hillsbrad";

                if (list[i].AreaName == "Ogrimmar")
                    list[i].AreaName = "Orgrimmar";

                // The
                if (list[i].AreaName == "Barrens")
                    list[i].AreaName = "TheBarrens";

                if (list[i].AreaName == "Hinterlands")
                    list[i].AreaName = "TheHinterlands";

                // have to test this later
                //if (list[i].AreaName == "Sunwell")
                //    list[i].AreaName = "Isle of Quel'Danas";
            }
        }

        public WorldMapArea? GetWorldMapArea(List<WorldMapArea> worldMapAreas, float x, float y, string continent, int mapHint)
        {
            var maps = worldMapAreas.Where(i => x <= i.LocTop)
                .Where(i => x >= i.LocBottom)
                .Where(i => y <= i.LocLeft)
                .Where(i => y >= i.LocRight)
                .Where(i => i.AreaName != "Azeroth")
                .Where(i => i.AreaName != "Kalimdor")
                .Where(i => i.Continent == continent)
                .ToList();

            if (!maps.Any())
            {
                throw new ArgumentOutOfRangeException(nameof(worldMapAreas), $"Failed to find map area for spot {x}, {y}");
            }

            if (maps.Count > 1)
            {
                // sometimes we end up with 2 map areas which a coord could be in which is rather unhelpful. e.g. Silithus and Feralas overlap.
                // If we are in a zone and not moving between then the mapHint should take care of the issue
                // otherwise we are not going to be able to work out which zone we are actually in...

                if (mapHint > 0)
                {
                    var map = maps.Where(m => m.UIMapId == mapHint).FirstOrDefault();
                    if (map != null)
                    {
                        return map;
                    }
                }

                throw new ArgumentOutOfRangeException(nameof(worldMapAreas), "Found many map areas for spot {x}, {y}: {string.Join(", ", maps.Select(s => s.AreaName))}");
            }

            return maps.First();
        }

    }
}
