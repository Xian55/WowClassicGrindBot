using System.Numerics;

using static System.MathF;

namespace Core;

public static class DirectionCalculator
{
    public static float CalculateMapHeading(Vector3 mapFrom, Vector3 mapTo)
    {
        //logger.LogInformation($"from: ({from.X},{from.Y}) to: ({to.X},{to.Y})");

        float target = Atan2(mapTo.X - mapFrom.X, mapTo.Y - mapFrom.Y);
        return PI + target;
    }

    public static Vector2 ToNormalRadian(float wowRadian)
    {
        // wow origo is north side - shifted 90 degree
        return new(
            Cos(wowRadian + (PI / 2)),
            Sin(wowRadian - (PI / 2)));
    }

    public static Vector2 ToNormalRadianNoFlip(float wowRadian)
    {
        return new(
            Cos(wowRadian),
            Sin(wowRadian));
    }
}