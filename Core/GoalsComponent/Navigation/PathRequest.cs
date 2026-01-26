using System;
using System.Numerics;

using static System.Diagnostics.Stopwatch;

namespace Core.Goals;

internal readonly struct PathRequest
{
    public readonly int MapId;
    public readonly Vector3 StartW;
    public readonly Vector3 EndW;
    public readonly bool StartIndoors;
    public readonly float Distance;
    public readonly Action<PathResult> Callback;
    public readonly long Time;

    public PathRequest(int mapId, bool startIndoors, Vector3 startW, Vector3 endW, float distance, Action<PathResult> callback)
    {
        MapId = mapId;
        StartW = startW;
        EndW = endW;
        StartIndoors = startIndoors;
        Distance = distance;
        Callback = callback;
        Time = GetTimestamp();
    }
}
