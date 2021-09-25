using System.Net.Http;
using System.Threading.Tasks;
using WowheadDB;
using Newtonsoft.Json;
using System.Linq;
using System.Collections.Generic;
using System;

namespace WowheadDB_Extractor
{
    public class PerZoneSkinnable
    {
        private int zoneId;
        private Area area;

        private string url;

        public PerZoneSkinnable(int zoneId, Area area)
        {
            this.zoneId = zoneId;
            this.area = area;
            url = $"https://tbc.wowhead.com/npcs?filter=6:10;{zoneId}:1;0:0";
        }

        public async Task Run()
        {
            try
            {
                area.skinnable = new List<int>();

                var content = GetPayloadFromWebpage(await LoadPage());

                var definition = new { data = new[] { new { id = 0 } } };
                var skinnableNpcIds = JsonConvert.DeserializeAnonymousType(content, definition);

                var listofIds = skinnableNpcIds.data.Select(i => i.id);
                if (listofIds != null)
                {
                    area.skinnable.AddRange(listofIds);
                    area.skinnable.Sort();
                }
            }

            catch (Exception e)
            {
                Console.WriteLine($" - PerZoneSkinnable\n {e}");
            }
        }


        async Task<string> LoadPage()
        {
            HttpClient client = new HttpClient();
            var response = await client.GetAsync(url);
            return await response.Content.ReadAsStringAsync();
        }

        string GetPayloadFromWebpage(string content)
        {
            string beginPat = "new Listview(";
            string endPat = "</script>";

            int beginPos = content.IndexOf(beginPat);
            int endPos = content.IndexOf(endPat, beginPos);

            string payload = content.Substring(beginPos + beginPat.Length, endPos - beginPos - beginPat.Length);

            payload = payload.Replace(",\"extraCols\":[Listview.extraCols.popularity]});", "}");

            return payload;
        }
    }
}
