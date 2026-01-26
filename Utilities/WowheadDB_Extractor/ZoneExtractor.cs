using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.IO;
using System.Net.Http;
using WowheadDB;
using System.Numerics;
using System.Diagnostics;
using System.Linq;
using SharedLib;

using static System.Diagnostics.Stopwatch;

namespace WowheadDB_Extractor
{
    public class ZoneExtractor
    {
        public const string EXP = "cata";

        private const string RetailUrl = "https://www.wowhead.com";

        public static string BaseUrl()
        {
            return EXP switch
            {
                "som" => "https://classic.wowhead.com",
                "tbc" => "https://tbc.wowhead.com",
                "wrath" => "https://www.wowhead.com/wotlk",
                "cata" => "https://www.wowhead.com/cata",
                _ => RetailUrl,
            };
        }

        private const string parentPath = $"../../../../../Json";
        private const string outputPath = $"{parentPath}/area/{EXP}/";
        private const string outputNodePath = "../path/";
        private static string ZONE_URL = $"{BaseUrl()}/zone=";

        private static string GetRetailZoneUrl() => $"{RetailUrl}/zone=";


        public static async Task Run()
        {
            await ExtractZones();
        }

        static Dictionary<string, int> GetZonesByContient(int contientId)
        {
            Dictionary<string, int> result = new();

            string location = $"{parentPath}\\dbc\\{EXP}\\";

            ReadOnlySpan<WorldMapArea> span =
                JsonConvert.DeserializeObject<WorldMapArea[]>(
                    File.ReadAllText(Path.Join(location, "WorldMapArea.json")));

            for (int i = 0; i < span.Length; i++)
            {
                WorldMapArea wma = span[i];

                if (span[i].MapID == contientId)
                {
                    if (!result.TryAdd(wma.AreaName, wma.AreaID))
                    {
                        Console.WriteLine($"Already exits! {wma.AreaName}");
                    }
                }
            }

            return result;
        }

        static async Task ExtractZones()
        {
            // bad
            //Dictionary<string, int> temp = new() { { "Isle of Quel'Danas", 4080 } };

            // test
            //Dictionary<string, int> temp = new() { { "Elwynn Forest", 12 } };
            //Dictionary<string, int> temp = new() { { "Zangarmarsh", 3521 } };
            //foreach (var entry in temp)
            //foreach (KeyValuePair<string, int> entry in Areas.List)

            foreach (string key in Continents.Map.Keys)
            {
                foreach (KeyValuePair<string, int> entry in GetZonesByContient(Continents.Map[key]))
                {
                    if (entry.Value == 0) continue;

                    try
                    {
                        var p = GetPayloadFromWebpage(await LoadPage(entry.Value));
                        string baseUrl = BaseUrl();
                        //string p;
                        //string baseUrl;

                        // empty then fall back to retail
                        if (p == "[]")
                        {
                            var url = GetRetailZoneUrl() + entry.Value;

                            HttpClient client = new HttpClient();
                            var response = await client.GetAsync(url);
                            var c = await response.Content.ReadAsStringAsync();
                            p = GetPayloadFromWebpage(c);

                            baseUrl = RetailUrl;
                        }

                        var z = ZoneFromJson(p);

                        PerZoneGatherable skin = new(baseUrl, entry.Value, GatherFilter.Skinnable);
                        z.skinnable = await skin.Run();

                        PerZoneGatherable g = new(baseUrl, entry.Value, GatherFilter.Gatherable);
                        z.gatherable = await g.Run();

                        PerZoneGatherable m = new(baseUrl, entry.Value, GatherFilter.Minable);
                        z.minable = await m.Run();

                        PerZoneGatherable salv = new(baseUrl, entry.Value, GatherFilter.Salvegable);
                        z.salvegable = await salv.Run();

                        SaveZone(z, entry.Value.ToString());

                        // TSP generation
                        //SaveZoneNode(entry, z.herb, nameof(z.herb), false, true);
                        //SaveZoneNode(entry, z.vein, nameof(z.vein), false, true);

                        Console.WriteLine($"Saved {entry.Value,5}={entry.Key}");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Fail  {entry.Value,5}={entry.Key} -> '{e.Message}'");
                        Console.WriteLine(e);
                    }

                    await Task.Delay(50);
                }
            }
        }

        static async Task<string> LoadPage(int zoneId)
        {
            var url = ZONE_URL + zoneId;

            HttpClient client = new HttpClient();
            var response = await client.GetAsync(url);
            return await response.Content.ReadAsStringAsync();
        }

        static string GetPayloadFromWebpage(string content)
        {
            string beginPat = "new ShowOnMap(";
            string endPat = ");</script>";

            int beginPos = content.IndexOf(beginPat);
            int endPos = content.IndexOf(endPat, beginPos);

            return content.Substring(beginPos + beginPat.Length, endPos - beginPos - beginPat.Length);
        }

        static Area ZoneFromJson(string content)
        {
            return JsonConvert.DeserializeObject<Area>(content);
        }

        static void SaveZone(Area zone, string name)
        {
            var output = JsonConvert.SerializeObject(zone);
            var file = Path.Join(outputPath, name + ".json");

            File.WriteAllText(file, output);
        }

        static void SaveZoneNode(KeyValuePair<string, int> zonekvp, Dictionary<string, List<Node>> nodes, string type, bool saveImage, bool onlyOptimalPath)
        {
            if (nodes == null)
                return;

            List<Vector2> points = [];
            foreach (var kvp in nodes)
            {
                points.AddRange(Array.ConvertAll([.. kvp.Value[0].MapCoords], (Vector3 v3) => new Vector2(v3.X, v3.Y)));
            }

            GeneticTSPSolver solver = new(points);
            long startTime = GetTimestamp();
            while (solver.UnchangedGens < solver.Length)
            {
                solver.Evolve();
            }
            var elapsed = GetElapsedTime(startTime);
            Console.WriteLine($" - TSP Solver {points.Count} {type} nodes {elapsed.TotalMilliseconds} ms");

            string prefix = $"{zonekvp.Value}_{zonekvp.Key}_{type}";

            if (saveImage)
                //solver.Draw($"{prefix}.bmp");
                solver.Draw(Path.Join(outputPath, outputNodePath, $"_{type}", $"{prefix}.bmp"));

            if (!onlyOptimalPath)
            {
                var output_points = JsonConvert.SerializeObject(points);
                var file_points = Path.Join(outputPath, outputNodePath, $"_{type}", $"{prefix}.json");
                File.WriteAllText(file_points, output_points);
            }

            var output_tsp = JsonConvert.SerializeObject(solver.Result);
            var file_tsp = Path.Join(outputPath, outputNodePath, $"_{type}", $"{prefix}_optimal.json");
            File.WriteAllText(file_tsp, output_tsp);
        }



        #region local tests

        static void SerializeTest()
        {
            int zoneId = 40;
            var file = Path.Join(outputPath, zoneId + ".json");
            var zone = ZoneFromJson(File.ReadAllText(file));
        }

        static void ExtractFromFileTest()
        {
            var file = Path.Join(outputPath, "a.html");
            var html = File.ReadAllText(file);

            string payload = GetPayloadFromWebpage(html);
            var zone = ZoneFromJson(payload);
        }

        #endregion

    }
}
