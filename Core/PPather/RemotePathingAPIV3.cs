using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Core.PPather;
using Core.Database;
using System.Threading;
using System.Diagnostics;
using AnTCP.Client;

namespace Core
{
    public sealed class RemotePathingAPIV3 : IPPather
    {
        public enum EMessageType
        {
            PATH,
            MOVE_ALONG_SURFACE,
            RANDOM_POINT,
            RANDOM_POINT_AROUND,
            CAST_RAY,
            RANDOM_PATH,
            PATH_LOCATIONS
        }

        private readonly ILogger logger;
        private readonly WorldMapAreaDB worldMapAreaDB;

        // TODO remove this
        private int watchdogPollMs = 1000;

        private List<LineArgs> lineArgs = new List<LineArgs>();

        private int targetMapId = 0;

        private AnTcpClient Client { get; }
        private Thread ConnectionWatchdog { get; }

        private bool ShouldExit { get; set; }

        public RemotePathingAPIV3(ILogger logger, string ip, int port, WorldMapAreaDB worldMapAreaDB)
        {
            this.logger = logger;
            this.worldMapAreaDB = worldMapAreaDB;

            Client = new AnTcpClient(ip, port);
            ConnectionWatchdog = new Thread(ObserveConnection);
            ConnectionWatchdog.Start();
        }

        #region old

        public async Task DrawLines(List<LineArgs> lineArgs)
        {
            await Task.Delay(0);
        }

        public async Task DrawLines()
        {
            await DrawLines(lineArgs);
        }

        public async Task DrawSphere(SphereArgs args)
        {
            await Task.Delay(0);
        }



        public async Task<List<WowPoint>> FindRoute(int uiMapId, WowPoint fromPoint, WowPoint toPoint)
        {
            if (!Client.IsConnected)
            {
                return new List<WowPoint>();
            }

            if (targetMapId == 0)
            {
                targetMapId = uiMapId;
            }

            try
            {
                await Task.Delay(0);

                Vector3 start = worldMapAreaDB.GetWorldLocation(uiMapId, fromPoint, true);
                Vector3 end = worldMapAreaDB.GetWorldLocation(uiMapId, toPoint, true);

                logger.LogInformation($"Finding route from {fromPoint} map {uiMapId} to {toPoint} map {targetMapId}...");

                var result = new List<WowPoint>();

                var area = worldMapAreaDB.Get(uiMapId);
                if (area == null)
                    return result;

                var path = Client.Send((byte)EMessageType.PATH_LOCATIONS, (area.MapID, start, end, 2)).AsArray<Vector3>();
                if (path == null)
                    return result;

                for (int i = 0; i < path.Length; i++)
                {
                    // Z X Y -> X Y Z
                    var p = worldMapAreaDB.ToMapAreaSpot(path[i].Z, path[i].X, path[i].Y, area.Continent, uiMapId);
                    result.Add(new WowPoint(p.X, p.Y));

                    logger.LogInformation($"new float[] {{ {path[i].Z}f, {path[i].X}f, {path[i].Y}f }},");
                }

                return result;
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


        public Task<bool> PingServer()
        {
            CancellationTokenSource cts = new CancellationTokenSource();

            var task = Task.Run(() =>
            {
                cts.CancelAfter(2 * watchdogPollMs);
                Stopwatch sw = Stopwatch.StartNew();

                while (!cts.IsCancellationRequested)
                {
                    if (Client.IsConnected)
                    {
                        break;
                    }
                }

                sw.Stop();

                logger.LogInformation($"{GetType().Name} PingServer {sw.ElapsedMilliseconds}ms {Client.IsConnected}");

                return Client.IsConnected;
            });

            return task;
        }

        public void RequestDisconnect()
        {
            ShouldExit = true;
            ConnectionWatchdog.Join();
        }

        #endregion old

        private void ObserveConnection()
        {
            while (!ShouldExit)
            {
                if (!Client.IsConnected)
                {
                    try
                    {
                        Client.Connect();
                    }
                    catch
                    {
                        // ignored, will happen when we cant connect
                    }
                }

                Thread.Sleep(watchdogPollMs);
            }
        }

    }
}