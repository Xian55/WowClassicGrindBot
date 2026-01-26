using BenchmarkDotNet.Attributes;

using System.Diagnostics;

namespace Benchmarks.Time;

#pragma warning disable CA1822 

[MemoryDiagnoser]
public class Time_Compare
{
    private readonly static DateTime Now = DateTime.UtcNow;
    private readonly static long NowTicks = Stopwatch.GetTimestamp();

    [Benchmark]
    public long DateTimeUtcNow()
    {
        return DateTime.UtcNow.Ticks;
    }

    [Benchmark]
    public long GetTimeStamp()
    {
        return Stopwatch.GetTimestamp();
    }

    [Benchmark]
    public long DateTimeUtcNow_Difference()
    {
        return (DateTime.UtcNow - Now).Ticks;
    }

    [Benchmark]
    public long GetTimeStamp_Difference()
    {
        return Stopwatch.GetElapsedTime(NowTicks).Ticks;
    }
}
