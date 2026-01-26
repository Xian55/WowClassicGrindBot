using Microsoft.Extensions.Logging;

using PPather.Graph;

using System;
using System.Numerics;

using WowTriangles;

namespace PPather;

public sealed class Search
{
    public PathGraph PathGraph { get; set; }
    public float MapId { get; set; }

    private readonly DataConfig dataConfig;
    private readonly ILogger logger;

    public Vector4 From { get; set; }
    public Vector4 Target { get; set; }

    private const float toonHeight = PathGraph.toonHeight;
    private const float toonSize = PathGraph.toonSize;

    private const float howClose = PathGraph.MaxStepLength;

    public Search(float mapId, ILogger logger, DataConfig dataConfig)
    {
        this.logger = logger;
        this.MapId = mapId;
        this.dataConfig = dataConfig;

        CreatePathGraph(mapId);
    }

    public void Clear()
    {
        MapId = 0;
        PathGraph.Clear();
        PathGraph = null;
    }

    private const float bigExtend = 2000;
    private const float smallExtend = 2 * toonHeight;

    public Vector4 CreateWorldLocation(float x, float y, float z, int mapId, bool? startIndoors)
    {
        const float canopyGap = 2.5f * toonHeight;   // treat small model-terrain gaps as tents/awnings
        const float nearlySame = 0.75f;  // Z difference considered “the same surface”
        const float minHeadroom = 2.6f;   // required clearance above toon head + jump
        const float waterBias = 0.15f;  // water must be meaningfully higher than terrain
        const float eps = 0.05f;

        float min_z = z == 0 ? z - bigExtend : z - smallExtend;
        float max_z = z == 0 ? z + bigExtend : z + smallExtend;

        float zTerrain = GetZValueAt(x, y, min_z, max_z, TriangleType.Terrain);
        float zWater = GetZValueAt(x, y, min_z, max_z, TriangleType.Water);

        // helper
        bool HasVerticalClearance(float X, float Y, float zFloor, float need)
        {
            float zCeil = GetZValueAt(X, Y, zFloor + eps, zFloor + need + 2.0f,
                                      TriangleType.Model | TriangleType.Object);
            return zCeil == float.MinValue || (zCeil - zFloor) >= need;
        }

        // Prefer water only if clearly above terrain
        if (zWater != float.MinValue && zTerrain != float.MinValue && (zWater - zTerrain) > waterBias)
            return new Vector4(x, y, zWater, mapId);

        float zModel = GetZValueAt(x, y, min_z, max_z, TriangleType.Model | TriangleType.Object);

        if (zModel != float.MinValue)
        {
            bool bModel = PathGraph.triangleWorld.IsSpotBlocked(x, y, zModel, toonHeight, 2 * toonSize);
            bool bTerrain = (zTerrain == float.MinValue) ? true :
                            PathGraph.triangleWorld.IsSpotBlocked(x, y, zTerrain, toonHeight, 2 * toonSize);

            bool modelClear = HasVerticalClearance(x, y, zModel, minHeadroom);
            bool terrainClear = (zTerrain != float.MinValue) && HasVerticalClearance(x, y, zTerrain, minHeadroom);

            float gap = (zTerrain != float.MinValue) ? (zModel - zTerrain) : float.PositiveInfinity;

            if (startIndoors.HasValue)
            {
                if (startIndoors.Value == false)
                {
                    // OUTDOORS: prefer terrain unless the model is a real usable surface

                    // Tent/awning case: prefer terrain if gap small, model blocked, or no headroom
                    if (zTerrain != float.MinValue &&
                        !bTerrain && terrainClear &&
                        (gap <= canopyGap || !modelClear || bModel))
                    {
                        return new Vector4(x, y, zTerrain, mapId);
                    }

                    // If both usable and near-equal, prefer terrain
                    if (zTerrain != float.MinValue && !bTerrain && terrainClear &&
                        !bModel && modelClear &&
                        MathF.Abs(zModel - zTerrain) <= nearlySame)
                    {
                        return new Vector4(x, y, zTerrain, mapId);
                    }

                    // Otherwise, pick the highest usable
                    bool modelUsable = !bModel && modelClear;
                    bool terrainUsable = !bTerrain && terrainClear;
                    if (modelUsable && (!terrainUsable || zModel > zTerrain))
                        return new Vector4(x, y, zModel, mapId);
                    if (terrainUsable)
                        return new Vector4(x, y, zTerrain, mapId);

                    // fallback
                    return new Vector4(x, y, zTerrain != float.MinValue ? zTerrain : zModel, mapId);
                }
                else
                {
                    // INDOORS: prefer lower usable surface (under a roof)
                    bool modelUsable = !bModel && modelClear;
                    bool terrainUsable = !bTerrain && terrainClear;

                    if (modelUsable && terrainUsable)
                        return new Vector4(x, y, (zModel <= zTerrain ? zModel : zTerrain), mapId);
                    if (modelUsable) return new Vector4(x, y, zModel, mapId);
                    if (terrainUsable) return new Vector4(x, y, zTerrain, mapId);

                    // fallback
                    return new Vector4(x, y, zModel != float.MinValue ? zModel : zTerrain, mapId);
                }
            }
            else
            {
                // Legacy logic, unchanged except with headroom consideration
                if (zTerrain != float.MinValue && !bTerrain && terrainClear &&
                    MathF.Abs(zModel - zTerrain) > PathGraph.toonHeightHalf)
                {
                    return new Vector4(x, y, zTerrain, mapId);
                }
                else
                {
                    return new Vector4(x, y, zModel, mapId);
                }
            }
        }
        else
        {
            // If zTerrain missing, retry with bigger extend
            if (zTerrain == float.MinValue)
            {
                min_z = z - bigExtend;
                max_z = z + bigExtend;
                zTerrain = GetZValueAt(x, y, min_z, max_z, TriangleType.Terrain);
            }

            return new Vector4(x, y, zTerrain, mapId);
        }
    }


    private float GetZValueAt(float x, float y, float min_z, float max_z, TriangleType allowedFlags)
    {
        return PathGraph.triangleWorld.FindStandableAt1
            (x, y, min_z, max_z, out float z1, out _, toonHeight, toonSize, true, allowedFlags)
            ? z1
            : float.MinValue;
    }

    public void CreatePathGraph(float mapId)
    {
        this.MapId = mapId;

        MPQTriangleSupplier mpq = new(logger, dataConfig, mapId);
        ChunkedTriangleCollection triangleWorld = new(logger, 64, mpq);
        PathGraph = new(mapId, triangleWorld, logger, dataConfig);
    }

    public Path DoSearch(SearchStrategy searchScoreSpot)
    {
        try
        {
            return PathGraph.CreatePath(From.AsVector3(), Target.AsVector3(), searchScoreSpot, howClose);
        }
        catch (Exception ex)
        {
            logger.LogError(ex.Message);
        }
        return null;
    }

    public (int, float) GetAreaIdAndZ(Vector3 location)
    {
        return PathGraph.triangleWorld.GetAreaIdAndZ(location);
    }
}