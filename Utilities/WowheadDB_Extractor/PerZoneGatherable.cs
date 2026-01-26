using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Linq;
using System;

namespace WowheadDB_Extractor
{
    public enum GatherFilter
    {
        Skinnable = 10,
        Gatherable = 15,
        Minable = 16,
        Salvegable = 44,
    }

    public class PerZoneGatherable
    {
        private const int LootRewardFilter = 6;

        private readonly string url;
        private readonly GatherFilter filter;

        public PerZoneGatherable(string baseUrl, int zoneId, GatherFilter filter)
        {
            this.filter = filter;

            url = $"{baseUrl}/npcs?filter={LootRewardFilter}:{(int)filter};{zoneId}:1;0:0";
        }

        public async Task<int[]> Run()
        {
            try
            {
                string content = GetPayloadFromWebpage(await LoadPage());

                var definition = new { data = new[] { new { id = 0 } } };
                var skinnableNpcIds = JsonConvert.DeserializeAnonymousType(content, definition);

                var listofIds = skinnableNpcIds.data.Select(i => i.id);
                if (listofIds != null)
                {
                    var ids = listofIds.ToArray();
                    Array.Sort(ids);
                    Console.WriteLine($"     - {nameof(PerZoneGatherable)}: Found {listofIds.Count()} {filter}");
                    return ids;
                }
            }
            catch (Exception e)
            {
                if (e is not ArgumentOutOfRangeException)
                    Console.WriteLine($" - {nameof(PerZoneGatherable)}\n {e}");
            }

            return [];
        }


        private async Task<string> LoadPage()
        {
            HttpClient client = new();
            var response = await client.GetAsync(url);
            return await response.Content.ReadAsStringAsync();
        }

        private static string GetPayloadFromWebpage(string content)
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
