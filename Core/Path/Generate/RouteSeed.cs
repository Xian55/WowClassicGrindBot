namespace Core;

/// <summary>
/// Derives the seed for one generated route from the session seed, so laps differ from one
/// another while the whole run still replays identically from the same
/// <see cref="ClassConfiguration.Seed"/>.
/// </summary>
public static class RouteSeed
{
    /// <summary>
    /// Deliberately <b>not</b> <c>HashCode.Combine</c>. That is seeded randomly per process
    /// as a hash-flooding defence, so it returns different values for the same inputs in
    /// every run - which made a pinned seed reproduce a route only within one session and
    /// silently produce a different one the next time the bot started.
    ///
    /// <para>This is the finalizer from splitmix64, truncated: cheap, well-mixed, and fixed
    /// for all time.</para>
    /// </summary>
    public static int For(int sessionSeed, int pathId, int lap)
    {
        ulong x = (ulong)(uint)sessionSeed;
        x = (x * 0x9E3779B97F4A7C15UL) + (ulong)(uint)pathId;
        x = (x * 0xBF58476D1CE4E5B9UL) + (ulong)(uint)lap;

        x ^= x >> 30;
        x *= 0xBF58476D1CE4E5B9UL;
        x ^= x >> 27;
        x *= 0x94D049BB133111EBUL;
        x ^= x >> 31;

        return (int)(uint)x;
    }
}
