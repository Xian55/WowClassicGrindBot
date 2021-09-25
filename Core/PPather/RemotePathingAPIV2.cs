using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Core.PPather;
using Core.Database;
using System.Threading;
using System.Diagnostics;

namespace Core
{
    public sealed class RemotePathingAPIV2 : CustomTcpClient, IPPather
    {
        private readonly ILogger logger;
        private readonly WorldMapAreaDB worldMapAreaDB;

        private List<LineArgs> lineArgs = new List<LineArgs>();

        private int targetMapId = 0;

        public RemotePathingAPIV2(ILogger logger, string ip, int port, WorldMapAreaDB worldMapAreaDB) : base(logger, ip, port)
        {
            this.logger = logger;
            this.worldMapAreaDB = worldMapAreaDB;
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
            if (!IsConnected)
                return new List<WowPoint>();

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
                //logger.LogInformation($"Finding route from {start} to {end}...");

                var area = worldMapAreaDB.Get(uiMapId);
                if (area == null)
                    return new List<WowPoint>();

                var request = new PathRequestWithLocationRequest(area.MapID, start, end, PathRequestFlags.ChaikinCurve);

                int typeSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(PathRequestWithLocationRequest));
                byte[]? response = SendData(request, typeSize);

                if (response == null || response.Length < typeSize)
                    return new List<WowPoint>();

                int vtypeSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Vector3));
                Vector3[] path;
                int nodeCount = response.Length / vtypeSize;
                unsafe
                {
                    fixed (byte* pResult = response)
                    {
                        path = new Span<Vector3>(pResult, nodeCount).ToArray();
                    }
                }

                if (path == null)
                    return new List<WowPoint>();

                var result = new List<WowPoint>();

                for (int i = 0; i < path.Length; i++)
                {
                    var p = worldMapAreaDB.ToMapAreaSpot(path[i].X, path[i].Y, path[i].Z, area.Continent, uiMapId);
                    result.Add(new WowPoint(p.X, p.Y));

                    logger.LogInformation($"new float[] {{ {path[i].X}f, {path[i].Y}f, {path[i].Z}f}},");
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
                    if (IsConnected)
                    {
                        break;
                    }
                }

                sw.Stop();

                logger.LogInformation($"{GetType().Name} PingServer {sw.ElapsedMilliseconds}ms {IsConnected}");

                return IsConnected;
            });

            return task;
        }

        #endregion old
    }
}