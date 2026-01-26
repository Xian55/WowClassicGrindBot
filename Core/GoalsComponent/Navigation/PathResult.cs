using System;
using System.Numerics;

using static System.Diagnostics.Stopwatch;

namespace Core.Goals;

internal readonly struct PathResult
{
    public readonly Vector3 StartW;
    public readonly Vector3 EndW;
    public readonly float Distance;
    public readonly Vector3[] Path;
    public readonly double ElapsedMs;
    public readonly Action<PathResult> Callback;

    public PathResult(in PathRequest request, Vector3[] path, Action<PathResult> callback)
    {
        StartW = request.StartW;
        EndW = request.EndW;
        Distance = request.Distance;
        Path = path;
        Callback = callback;
        ElapsedMs = GetElapsedTime(request.Time).TotalMilliseconds;
    }
}
