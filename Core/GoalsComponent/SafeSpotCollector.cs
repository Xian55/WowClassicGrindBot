using SharedLib.Extensions;

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace Core.Goals;

public sealed class SafeSpotCollector : IDisposable
{
    private readonly PlayerReader playerReader;
    private readonly AddonBits bits;

    private readonly Timer timer;

    public Stack<Vector3> MapLocations { get; } = new();

    public SafeSpotCollector(
        PlayerReader playerReader,
        AddonBits bits)
    {
        this.playerReader = playerReader;
        this.bits = bits;

        timer = new(Update, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public void Dispose()
    {
        timer.Dispose();
    }

    public void Update(object? obj)
    {
        if (bits.Combat())
            return;

        if (MapLocations.TryPeek(out Vector3 lastMapPos) &&
            lastMapPos == playerReader.MapPosNoZ)
            return;

        MapLocations.Push(playerReader.MapPosNoZ);
    }

    public void Reduce(Vector3 mapPosition)
    {
        lock (MapLocations)
        {
            Vector3 closestM = default;
            float distanceM = float.MaxValue;

            foreach (Vector3 p in MapLocations)
            {
                float d = mapPosition.MapDistanceXYTo(p);
                if (d < distanceM)
                {
                    closestM = p;
                    distanceM = d;
                }
            }

            while (MapLocations.TryPeek(out var p) && p != closestM)
            {
                MapLocations.Pop();
            }
        }
    }
}
