using BenchmarkDotNet.Attributes;

using System.Numerics;
using System.Runtime.CompilerServices;

using static System.Numerics.Vector3;

namespace Benchmarks.PPather;

public class PPather_Utils_PointDistanceToSegment
{
    private readonly Vector3 p0 = new(0, 0, 0);

    private readonly Vector3 x1 = new(1, 1, 1);
    private readonly Vector3 x2 = new(0, 0, 0);


    [Benchmark(Baseline = true)]
    public void Old_PointDistanceToSegment()
    {
        _ = PointDistanceToSegment_old(in p0, in x1, in x2);
    }

    [Benchmark]
    public void New_PointDistanceToSegment()
    {
        _ = PointDistanceToSegment(in p0, in x1, in x2);
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float PointDistanceToSegment(in Vector3 p0, in Vector3 x1, in Vector3 x2)
    {
        Vector3 L = x2 - x1; // the segment vector
        float l2 = Dot(L, L); // square length of the segment
        Vector3 D = p0 - x1; // vector from point to segment start
        float d = Dot(D, L); // projection factor [x2-x1].[p0-x1]lear

        // Optimized return for closest segment point
        if (d < 0.0f) return D.Length();
        return ((d > l2 ? D - L : D - (L * (d / l2))).Length());
    }

    [SkipLocalsInit]
    public static float PointDistanceToSegment_old(
    in Vector3 p0,
    in Vector3 x1, in Vector3 x2)
    {
        Vector3 L = Subtract(x2, x1); // the segment vector
        float l2 = Dot(L, L);   // square length of the segment

        Vector3 D = Subtract(p0, x1);   // vector from point to segment start
        float d = Dot(D, L);     // projection factor [x2-x1].[p0-x1]

        if (d < 0.0f) // closest to x1
            return D.Length();

        float eDotL = Dot(D - (L * (d / l2)), L);

        return eDotL > l2
            ? (D - L).Length()
            : (D - (L * (d / l2))).Length();
    }
}
