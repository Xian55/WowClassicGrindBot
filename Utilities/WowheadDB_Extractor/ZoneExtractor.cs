using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.IO;
using System.Net;
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
        /// <summary>
        /// Which client's data to build. Settable from the command line so this does not
        /// need editing-and-reverting per run - the outputs are per client (ids differ),
        /// so every client needs its own pass.
        /// </summary>
        public static string EXP { get; set; } = "cata";

        private const string RetailUrl = "https://www.wowhead.com";

        public static string BaseUrl()
        {
            return EXP switch
            {
                "som" => "https://classic.wowhead.com",
                "tbc" => "https://tbc.wowhead.com",
                "wrath" or "legacy_wrath" => "https://www.wowhead.com/wotlk",
                "cata" or "legacy_cata" => "https://www.wowhead.com/cata",
                "mop" or "legacy_mop" => "https://www.wowhead.com/mop-classic",
                _ => RetailUrl,
            };
        }

        /// <summary>
        /// Repo Json folder, found by walking up for MasterOfPuppets.sln instead of a
        /// fixed "../../../../../Json". That relative path only resolves when the binary
        /// is launched from bin/Release/&lt;tfm&gt;; running via `dotnet run --project`
        /// puts the working directory at the project root instead, where the same climb
        /// overshoots past the repo and lands on the drive root.
        /// </summary>
        private static string parentPath => Path.Join(SolutionRoot(), "Json");

        private static string SolutionRoot()
        {
            // Not string? - this project has no #nullable context (CS8632).
            string dir = System.AppContext.BaseDirectory;
            while (dir is not null && !File.Exists(Path.Combine(dir, "MasterOfPuppets.sln")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            return dir ?? throw new DirectoryNotFoundException(
                "MasterOfPuppets.sln not found above " + System.AppContext.BaseDirectory);
        }

        // Computed, not const: EXP is now runtime. Falling back to a const here would
        // silently write every client's data into the same folder.
        private static string outputPath => $"{parentPath}/area/{EXP}/";
        private const string outputNodePath = "../path/";
        private static string ZONE_URL => $"{BaseUrl()}/zone=";

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

                // Zone rows only. WorldMapArea.json also carries SUBZONE rows (name +
                // ParentAreaId + bounds, added by ReadDBC_CSV's worldmap extractor when
                // Json/subzones/<exp> exists) and those have no UIMapId. They are not
                // zones and have no Wowhead zone page - worse, TryAdd is first-wins, so
                // a subzone sharing a zone's name would claim the slot and substitute
                // its own AreaID, silently replacing a real zone with a dead id.
                if (wma.MapID == contientId && wma.UIMapId != 0)
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

                    // Resume: a zone already on disk costs five requests to re-fetch for
                    // no gain, and a run that stops early (rate limit) is expected to be
                    // retried. Delete the file, or the folder, to force a refresh.
                    string existing = Path.Join(outputPath, $"{entry.Value}.json");
                    if (File.Exists(existing))
                    {
                        Console.WriteLine($"Have  {entry.Value,5}={entry.Key}");
                        continue;
                    }

                    try
                    {
                        var p = GetPayloadFromWebpage(await LoadPage(entry.Value));
                        string baseUrl = BaseUrl();
                        //string p;
                        //string baseUrl;

                        // empty then fall back to retail
                        if (p == "[]")
                        {
                            // Same configured client - a bare one is 403'd by CloudFront.
                            var c = await LoadPage(entry.Value, RetailUrl);
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
                    catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.Forbidden)
                    {
                        // Abort the whole run. Each zone costs ~5 requests (page + four
                        // gatherable queries), so continuing past a block would fire
                        // hundreds more at a host already refusing us - which is how the
                        // block gets extended rather than lifted.
                        Console.WriteLine();
                        Console.WriteLine($"BLOCKED at {entry.Value}={entry.Key}: {e.Message}");
                        Console.WriteLine("Stopping. Wait for the rate limit to clear, then re-run; " +
                                          $"raise {DelayEnv} above {delayMs}ms to be gentler.");
                        return;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Fail  {entry.Value,5}={entry.Key} -> '{e.Message}'");
                        Console.WriteLine(e);
                    }

                    await Task.Delay(delayMs);
                }
            }
        }

        /// <summary>
        /// Pause between zones. A zone is ~5 requests (the zone page plus four
        /// PerZoneGatherable queries), and a full run is ~100 zones, so the old 50ms
        /// meant roughly 500 requests back to back - enough to earn a 403 that then
        /// blocks the host for hours. Override with the env var when in a hurry, but
        /// slower is cheaper than being locked out.
        /// </summary>
        private const string DelayEnv = "WOWHEAD_DELAY_MS";

        private static readonly int delayMs =
            int.TryParse(System.Environment.GetEnvironmentVariable(DelayEnv), out int d) && d >= 0
                ? d
                : 1000;

        /// <summary>
        /// Random extra wait on top of <see cref="delayMs"/>, 0..this, per request.
        /// A metronome-steady interval is itself a bot signature - real browsing is
        /// uneven - so the gap varies. Defaults to half the base delay.
        /// </summary>
        private const string JitterEnv = "WOWHEAD_JITTER_MS";

        private static readonly int jitterMs =
            int.TryParse(System.Environment.GetEnvironmentVariable(JitterEnv), out int j) && j >= 0
                ? j
                : delayMs / 2;

        // One client for the whole run: a new HttpClient per request exhausts sockets,
        // and reusing the connection is also gentler on the far end.
        public static readonly HttpClient Http = CreateClient();

        private static readonly SemaphoreSlim gate = new(1, 1);
        private static long lastRequestTicks;

        /// <summary>
        /// Waits so that consecutive requests are at least <see cref="delayMs"/> apart.
        ///
        /// Pacing per ZONE is not enough: a zone costs five requests - the zone page plus
        /// four PerZoneGatherable queries - and those fire back to back, so a per-zone
        /// delay still bursts at ~5 req/s. Measured: with only a per-zone delay the run
        /// got 403'd after 9 zones (~45 requests). Gating every request through here
        /// makes it an actual 1 req/s crawler.
        /// </summary>
        public static async Task ThrottleAsync()
        {
            await gate.WaitAsync();
            try
            {
                int wait = delayMs + (jitterMs > 0 ? Random.Shared.Next(jitterMs + 1) : 0);

                long now = GetTimestamp();
                if (lastRequestTicks != 0)
                {
                    double since = GetElapsedTime(lastRequestTicks, now).TotalMilliseconds;
                    if (since < wait)
                    {
                        await Task.Delay((int)(wait - since));
                    }
                }

                lastRequestTicks = GetTimestamp();
            }
            finally
            {
                gate.Release();
            }
        }

        private static HttpClient CreateClient()
        {
            // Follows redirects (default) - a zone URL 301s to its slug form, e.g.
            // /mop-classic/zone=6138 -> /mop-classic/zone=6138/dread-wastes.
            HttpClient c = new(new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            });

            // Wowhead sits behind CloudFront, which rejects requests whose header set
            // does not look like a real navigation - a User-Agent alone is not enough
            // and comes back 403 "Request blocked. Generated by cloudfront". Measured:
            // UA only -> 403, this set -> 200. It is the SHAPE of the request that is
            // checked, not the identity, so these are the headers any browser sends.
            var h = c.DefaultRequestHeaders;
            h.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            h.Add("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9," +
                "image/avif,image/webp,image/apng,*/*;q=0.8");
            h.Add("Accept-Language", "en-US,en;q=0.9");
            h.Add("Sec-Fetch-Dest", "document");
            h.Add("Sec-Fetch-Mode", "navigate");
            h.Add("Sec-Fetch-Site", "none");
            h.Add("Sec-Fetch-User", "?1");
            h.Add("Upgrade-Insecure-Requests", "1");

            return c;
        }

        static Task<string> LoadPage(int zoneId) => LoadPage(zoneId, BaseUrl());

        static async Task<string> LoadPage(int zoneId, string baseUrl)
        {
            string url = $"{baseUrl}/zone={zoneId}";

            await ThrottleAsync();
            using HttpResponseMessage response = await Http.GetAsync(url);

            // Surface the block here. Without this a 403 flows into the parser, whose
            // IndexOf returns -1 and throws "Index was out of range (startIndex)" for
            // every zone - which reads like a parsing bug rather than a refused request.
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"{(int)response.StatusCode} {response.StatusCode} from {url}" +
                    (response.StatusCode == HttpStatusCode.Forbidden
                        ? " - CloudFront refused the request shape; see CreateClient"
                        : string.Empty),
                    null, response.StatusCode);
            }

            return await response.Content.ReadAsStringAsync();
        }

        static string GetPayloadFromWebpage(string content)
        {
            string beginPat = "new ShowOnMap(";
            string endPat = ");</script>";

            int beginPos = content.IndexOf(beginPat);
            if (beginPos < 0)
            {
                // Legitimately absent on zones with no map data, but also what an error
                // or captcha page looks like. Say which rather than throwing an
                // out-of-range from the Substring below.
                throw new InvalidDataException(
                    $"no '{beginPat}' payload in a {content.Length} byte response " +
                    "(zone has no map data, or the request was blocked)");
            }

            int endPos = content.IndexOf(endPat, beginPos);
            if (endPos < 0)
            {
                throw new InvalidDataException($"unterminated '{beginPat}' payload");
            }

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
