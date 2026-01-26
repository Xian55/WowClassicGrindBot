using System.Numerics;
using System.Text.Json.Serialization;

namespace SharedLib;

public readonly record struct SubZoneArea
{
    public int Id { get; init; }
    public Vector3 Min { get; init; }
    public Vector3 Max { get; init; }

    [JsonIgnore]
    public Vector3 Center => (Min + Max) / 2;

    [JsonIgnore]
    public float MaxRange => Vector3.DistanceSquared(Min, Max);

    public readonly bool Contains(in Vector3 p)
    {
        return
            p.X >= Min.X && p.X <= Max.X &&
            p.Y >= Min.Y && p.Y <= Max.Y;
    }
}