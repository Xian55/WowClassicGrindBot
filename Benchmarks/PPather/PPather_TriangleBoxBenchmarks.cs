using BenchmarkDotNet.Attributes;

using System.Numerics;

using WowTriangles;

namespace Benchmarks.PPather;
public class PPather_TriangleBoxBenchmarks
{
    private Vector3 a, b, c, boxCenter, boxExtents;

    [GlobalSetup]
    public void Setup()
    {
        // Initialize with representative test data
        a = new Vector3(1, 2, 3);
        b = new Vector3(4, 5, 6);
        c = new Vector3(7, 8, 9);
        boxCenter = new Vector3(5, 5, 5);
        boxExtents = new Vector3(3, 3, 3);
    }

    [Benchmark]
    public bool Original()
    {
        return Utils.TriangleBoxIntersect(a, b, c, boxCenter, boxExtents);
    }

    [Benchmark]
    public bool SIMD()
    {
        return Utils.TriangleBoxIntersect_SIMD(a, b, c, boxCenter, boxExtents);
    }
}