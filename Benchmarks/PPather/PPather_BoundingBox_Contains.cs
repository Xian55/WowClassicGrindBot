using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

using BenchmarkDotNet.Attributes;

namespace Benchmarks.PPather;

public readonly record struct BoundingBox3D
{
    public Vector3 Min { get; init; }
    public Vector3 Max { get; init; }

    public readonly bool ContainsOriginal(Vector3 p) =>
        p.X >= Min.X && p.X <= Max.X &&
        p.Y >= Min.Y && p.Y <= Max.Y &&
        p.Z >= Min.Z && p.Z <= Max.Z;

    public readonly bool ContainsBitwise(Vector3 p) =>
        p.X >= Min.X & p.X <= Max.X &
        p.Y >= Min.Y & p.Y <= Max.Y &
        p.Z >= Min.Z & p.Z <= Max.Z;

    public readonly bool ContainsSIMD(Vector3 p)
    {
        if (Sse.IsSupported)
        {
            var vecP = Vector128.Create(p.X, p.Y, p.Z, 0f);
            var vecMin = Vector128.Create(Min.X, Min.Y, Min.Z, 0f);
            var vecMax = Vector128.Create(Max.X, Max.Y, Max.Z, 0f);

            var cmpMin = Sse.CompareGreaterThanOrEqual(vecP, vecMin);
            var cmpMax = Sse.CompareLessThanOrEqual(vecP, vecMax);

            return Sse.MoveMask(Sse.And(cmpMin, cmpMax)) == 0b0111;
        }
        return ContainsBitwise(p);
    }

    public readonly bool ContainsClamp(Vector3 p) => Vector3.Clamp(p, Min, Max) == p;
}

public class PPather_BoundingBox_Contains
{
    private readonly BoundingBox3D box = new() { Min = new Vector3(-1, -1, -1), Max = new Vector3(1, 1, 1) };
    private readonly Vector3 testPoint = new(0.5f, 0.5f, 0.5f);

    [Benchmark] public bool TestOriginal() => box.ContainsOriginal(testPoint);
    [Benchmark] public bool TestBitwise() => box.ContainsBitwise(testPoint);
    [Benchmark] public bool TestSIMD() => box.ContainsSIMD(testPoint);
    [Benchmark] public bool TestClamp() => box.ContainsClamp(testPoint);
}