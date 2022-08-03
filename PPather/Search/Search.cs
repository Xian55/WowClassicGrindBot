using Microsoft.Extensions.Logging;
using PPather.Graph;
using System;
using WowTriangles;
using System.Numerics;
using SharedLib.Extensions;

namespace PPather
{
    public class Search
    {
        public PathGraph PathGraph { get; set; }
        public float MapId { get; set; }

        private readonly DataConfig dataConfig;
        private readonly ILogger logger;

        public Vector4 locationFrom { get; set; }
        public Vector4 locationTo { get; set; }

        private const float toonHeight = 2.0f;
        private const float toonSize = 0.5f;

        public Search(float mapId, ILogger logger, DataConfig dataConfig)
        {
            this.logger = logger;
            this.MapId = mapId;
            this.dataConfig = dataConfig;

            CreatePathGraph(mapId);
        }

        public Vector4 CreateWorldLocation(float x, float y, float z, int mapId)
        {
            // find model 0 i.e. terrain
            var z0 = GetZValueAt(x, y, new int[] { (int)z });

            // if no z value found then try any model
            if (z0 == float.MinValue) { z0 = GetZValueAt(x, y, null); }

            if (z0 == float.MinValue) { z0 = 0; }

            return new Vector4(x, y, z0 - toonHeight, mapId);
        }

        private float GetZValueAt(float x, float y, int[] allowedModels)
        {
            float z0 = float.MinValue;
            int flags;

            if (allowedModels != null &&
                PathGraph.triangleWorld.FindStandableAt1(x, y, -1000, 2000, out float z1, out flags, toonHeight, toonSize, true, null))
            {
                z0 = z1;
            }

            if (z0 == float.MinValue &&
                PathGraph.triangleWorld.FindStandableAt1(x, y, -1000, 2000, out float z2, out flags, toonHeight, toonSize, true, allowedModels))
            {
                z0 = z2;
                // try to find a standable just under where we are just in case we are on top of a building.
                if (PathGraph.triangleWorld.FindStandableAt1(x, y, -1000, z0 - toonHeight - 1, out float z3, out flags, toonHeight, toonSize, true, allowedModels))
                {
                    z0 = z3;
                }
            }

            return z0;
        }

        public void CreatePathGraph(float mapId)
        {
            this.MapId = mapId;

            MPQTriangleSupplier mpq = new(logger, dataConfig, mapId);
            ChunkedTriangleCollection triangleWorld = new(logger, 64, mpq);
            PathGraph = new PathGraph(mapId, triangleWorld, null, logger, dataConfig);
        }

        public Path DoSearch(PathGraph.eSearchScoreSpot searchType)
        {
            PathGraph.SearchEnabled = true;

            // tell the pathgraph which type of search to do
            PathGraph.searchScoreSpot = searchType;

            //slow down the search if required.
            //PathGraph.sleepMSBetweenSpots = 0;

            try
            {
                return PathGraph.CreatePath(locationFrom.AsVector3(), locationTo.AsVector3(), 5, null);
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
                return null;
            }
        }
    }
}