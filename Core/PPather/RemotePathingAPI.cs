using Newtonsoft.Json;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Core.PPather;
using System.Text;

namespace Core
{
    public class RemotePathingAPI : IPPather
    {
        private readonly ILogger logger;

        private string host = "localhost";

        private int port = 5001;

        private string api => $"http://{host}:{port}/api/PPather/";

        private List<LineArgs> lineArgs = new List<LineArgs>();

        private int targetMapId = 0;

        public RemotePathingAPI(ILogger logger, string host="", int port=0)
        {
            this.logger = logger;
            this.host = host;
            this.port = port;
        }

        public async Task DrawLines(List<LineArgs> lineArgs)
        {
            this.lineArgs = lineArgs;

            using (var handler = new HttpClientHandler())
            {
                using (var client = new HttpClient(handler))
                {
                    using (var content = new StringContent(JsonConvert.SerializeObject(lineArgs), Encoding.UTF8, "application/json"))
                    {
                        logger.LogInformation($"Drawing lines '{string.Join(", ", lineArgs.Select(l => l.MapId))}'...");
                        await client.PostAsync($"{api}Drawlines", content);
                    }
                }
            }
        }

        public async Task DrawLines()
        {
            await DrawLines(lineArgs);
        }

        public async Task DrawSphere(SphereArgs args)
        {
            using (var handler = new HttpClientHandler())
            {
                using (var client = new HttpClient(handler))
                {
                    using (var content = new StringContent(JsonConvert.SerializeObject(args), Encoding.UTF8, "application/json"))
                    {
                        await client.PostAsync($"{api}DrawSphere", content);
                    }
                }
            }
        }

        public async Task<List<WowPoint>> FindRoute(int map, WowPoint fromPoint, WowPoint toPoint)
        {
            if (targetMapId == 0)
            {
                targetMapId = map;
            }

            try
            {
                logger.LogInformation($"Finding route from {fromPoint} map {map} to {toPoint} map {targetMapId}...");
                var url = $"{api}MapRoute?map1={map}&x1={fromPoint.X}&y1={fromPoint.Y}&map2={targetMapId}&x2={toPoint.X}&y2={toPoint.Y}";
                var sw = new Stopwatch();
                sw.Start();

                using (var handler = new HttpClientHandler())
                {
                    using (var client = new HttpClient(handler))
                    {
                        var responseString = await client.GetStringAsync(url);
                        logger.LogInformation($"Finding route from {fromPoint} map {map} to {toPoint} took {sw.ElapsedMilliseconds} ms.");
                        var path = JsonConvert.DeserializeObject<IEnumerable<WorldMapAreaSpot>>(responseString);
                        var result = path.Select(l => new WowPoint(l.X, l.Y)).ToList();
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Finding route from {fromPoint} to {toPoint}");
                Console.WriteLine(ex);
                return new List<WowPoint>();
            }
        }

        public Task<List<WowPoint>> FindRouteTo(PlayerReader playerReader, WowPoint destination)
        {
            return FindRoute(playerReader.UIMapId, playerReader.PlayerLocation, destination);
        }

        public async Task<bool> PingServer()
        {
            try
            {
                var url = $"{api}SelfTest";

                using (var handler = new HttpClientHandler())
                {
                    using (var client = new HttpClient(handler))
                    {
                        var responseString = await client.GetStringAsync(url);
                        var result = JsonConvert.DeserializeObject<bool>(responseString);
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning($"INFO: Pathing API is not running remotely, this means the local one will be used. {api} Gave({ex.Message}).");
                return false;
            }
        }
    }
}