using BenchmarkDotNet.Attributes;

namespace Benchmarks.PPather;

[MemoryDiagnoser]
public class PPather_HasHoles
{

    public bool hasholes = true;

    public readonly uint holes;

    // 0 ..3, 0 ..3
    private static readonly int[] old_holetab_h = [0x1111, 0x2222, 0x4444, 0x8888];
    private static readonly int[] old_holetab_v = [0x000F, 0x00F0, 0x0F00, 0xF000];

    private static readonly int[] new_holetab = [
        0x1111 & 0x000F, 0x1111 & 0x00F0, 0x1111 & 0x0F00, 0x1111 & 0xF000,
        0x2222 & 0x000F, 0x2222 & 0x00F0, 0x2222 & 0x0F00, 0x2222 & 0xF000,
        0x4444 & 0x000F, 0x4444 & 0x00F0, 0x4444 & 0x0F00, 0x4444 & 0xF000,
        0x8888 & 0x000F, 0x8888 & 0x00F0, 0x8888 & 0x0F00, 0x8888 & 0xF000
    ];

    [Benchmark(Baseline = true)]
    [ArgumentsSource(nameof(Inputs_Fast))]
    public bool Old_IsHole(int i, int j)
    {
        if (!hasholes)
            return false;

        i /= 2;
        j /= 2;

        return i <= 3 && j <= 3 && (holes & old_holetab_h[i] & old_holetab_v[j]) != 0;
    }

    [Benchmark]
    [ArgumentsSource(nameof(Inputs_Fast))]
    public bool New_IsHole(int i, int j)
    {
        if (!hasholes)
            return false;

        i >>= 1;
        j >>= 1;

        if (i > 3 || j > 3)
            return false;

        int index = (i << 2) | j;

        return (holes & new_holetab[index]) != 0;
    }

    public static IEnumerable<object[]> Inputs_All()
    {
        const int min = -1; // -1
        const int max = 5;  // 5

        // Generate a range of inputs from -1,-1 to 5,5
        for (int i = min; i < max; i++)
        {
            for (int j = min; j < max; j++)
            {
                yield return [i, j];
            }
        }
    }

    public static IEnumerable<object[]> Inputs_Fast()
    {
        yield return [0, 0];
    }
}
