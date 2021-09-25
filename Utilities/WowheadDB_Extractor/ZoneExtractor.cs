using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.IO;
using System.Net.Http;
using WowheadDB;

namespace WowheadDB_Extractor
{
    public class ZoneExtractor
    {
        private const string outputPath = "../../../../../Json/area/";
        private const string ZONE_CLASSIC_URL = "https://classic.wowhead.com/zone=";
        private const string ZONE_TBC_URL = "https://tbc.wowhead.com/zone=";

        public async Task Run()
        {
            await ExtractZones();
        }

        async Task ExtractZones()
        {
            foreach (KeyValuePair<string, int> entry in Areas.List)
            {
                if (entry.Value == 0) continue;
                try
                {
                    var p = GetPayloadFromWebpage(await LoadPage(entry.Value));
                    var z = ZoneFromJson(p);

                    PerZoneSkinnable perZoneSkinnable = new PerZoneSkinnable(entry.Value, z);
                    await perZoneSkinnable.Run();

                    SaveZone(z, entry.Value.ToString());

                    Console.WriteLine($"Saved {entry.Value,5}={entry.Key}");
                }
                catch(Exception e)
                {
                    Console.WriteLine($"Fail  {entry.Value,5}={entry.Key} -> '{e.Message}'");
                    Console.WriteLine(e);
                }

                await Task.Delay(50);
            }
        }

        async Task<string> LoadPage(int zoneId)
        {
            var url = ZONE_TBC_URL + zoneId;

            HttpClient client = new HttpClient();
            var response = await client.GetAsync(url);
            return await response.Content.ReadAsStringAsync();
        }

        string GetPayloadFromWebpage(string content)
        {
            string beginPat = "new ShowOnMap(";
            string endPat = ");</script>";

            int beginPos = content.IndexOf(beginPat);
            int endPos = content.IndexOf(endPat, beginPos);

            return content.Substring(beginPos + beginPat.Length, endPos - beginPos - beginPat.Length);
        }

        Area ZoneFromJson(string content)
        {
            return JsonConvert.DeserializeObject<Area>(content);
        }

        void SaveZone(Area zone, string name)
        {
            var output = JsonConvert.SerializeObject(zone);
            var file = Path.Join(outputPath, name + ".json");

            File.WriteAllText(file, output);
        }


        #region local tests

        void SerializeTest()
        {
            int zoneId = 40;
            var file = Path.Join(outputPath, zoneId + ".json");
            var zone = ZoneFromJson(File.ReadAllText(file));
        }

        void ExtractFromFileTest()
        {
            var file = Path.Join(outputPath, "a.html");
            var html = File.ReadAllText(file);

            string payload = GetPayloadFromWebpage(html);
            var zone = ZoneFromJson(payload);
        }

        #endregion

    }
}
