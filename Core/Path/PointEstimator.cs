using SharedLib;

using System.Numerics;

namespace Core;

public static class PointEstimator
{
    public static Vector3 GetMapPos(WorldMapArea wma, Vector3 playerPosW, float wowRad, float distance)
    {
        Vector2 dir = DirectionCalculator.ToNormalRadianNoFlip(wowRad);

        Vector3 corpsePosW = new(playerPosW.X + (distance * dir.X), playerPosW.Y + (distance * dir.Y), playerPosW.Z);

        return WorldMapAreaDB.ToMap_FlipXY(corpsePosW, wma);
    }

}
