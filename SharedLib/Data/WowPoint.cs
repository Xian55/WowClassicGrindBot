using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

public class WowPoint : IEquatable<WowPoint>
{
    public double X { get; set; }
    public double Y { get; set; }

    public Vector2 Vector2() => new Vector2((float)X, (float)Y);

    public WowPoint() {}

    public WowPoint(double x, double y)
    {
        this.X = x;
        this.Y = y;
    }

    public static List<WowPoint> ShortenRouteFromLocation(WowPoint location, List<WowPoint> pointsList)
    {
        var result = new List<WowPoint>();

        var closestDistance = pointsList.Select(p => (point: p, distance: DistanceTo(location, p)))
            .OrderBy(s => s.distance);

        var closestPoint = closestDistance.First();

        var startPoint = 0;
        for (int i = 0; i < pointsList.Count; i++)
        {
            if (pointsList[i] == closestPoint.point)
            {
                startPoint = i;
                break;
            }
        }

        for (int i = startPoint; i < pointsList.Count; i++)
        {
            result.Add(pointsList[i]);
        }

        return result;
    }

    public static double DistanceTo(WowPoint l1, WowPoint l2)
    {
        var x = l1.X - l2.X;
        var y = l1.Y - l2.Y;
        x = x * 100;
        y = y * 100;
        var distance = Math.Sqrt((x * x) + (y * y));

        //logger.LogInformation($"distance:{x} {y} {distance.ToString()}");
        return distance;
    }

    public override string ToString()
    {
        return $"{X},{Y}";
    }

    public bool Equals(WowPoint other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return other.X.Equals(X) && other.Y.Equals(Y);
    }

    public override bool Equals(object obj)
    {
        return Equals((WowPoint)obj);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (X.GetHashCode() * 397) ^ Y.GetHashCode();
        }
    }

    public static List<WowPoint> FromList(List<List<double>> points)
    {
        var output = new List<WowPoint>();
        points.ForEach(p => output.Add(new WowPoint(p[0], p[1])));
        return output;
    }
}