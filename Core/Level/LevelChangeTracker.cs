using Microsoft.Extensions.Logging;

using System;

using static System.Diagnostics.Stopwatch;

namespace Core;

/// <summary>
/// Announces level-ups the way <see cref="BagChangeTracker"/> and
/// <see cref="MoneyChangeTracker"/> announce loot and coin, with how long the level took.
/// <para>
/// Distinct from <see cref="LevelTracker"/>, which predicts when the next level will
/// arrive from the XP rate and says nothing when it actually does.
/// </para>
/// </summary>
public sealed partial class LevelChangeTracker : IDisposable
{
    private readonly ILogger<LevelChangeTracker> logger;
    private readonly PlayerReader playerReader;

    // The addon reports the current level, so the first change is the opening value
    // rather than a ding - announcing it would claim a level-up that never happened.
    // Same reason MoneyChangeTracker primes before reporting a delta.
    private bool primed;
    private int previous;
    private long levelStartTime;

    public LevelChangeTracker(ILogger<LevelChangeTracker> logger,
        PlayerReader playerReader)
    {
        this.logger = logger;
        this.playerReader = playerReader;

        levelStartTime = GetTimestamp();

        playerReader.Level.Changed += Level_Changed;
    }

    public void Dispose()
    {
        playerReader.Level.Changed -= Level_Changed;
    }

    private void Level_Changed()
    {
        int current = playerReader.Level.Value;

        if (!primed)
        {
            primed = true;
            previous = current;
            levelStartTime = GetTimestamp();
            return;
        }

        int from = previous;
        previous = current;

        TimeSpan elapsed = GetElapsedTime(levelStartTime);
        levelStartTime = GetTimestamp();

        // A level can go down - a death on a Hardcore-style server, or the bot attaching
        // to a different character - so this reports the direction rather than assuming.
        if (current <= from)
        {
            LogLevelChanged(logger, from, current);
            return;
        }

        // Local, not an inline argument: CA1873 does not track the guard for
        // source-generated log methods, only local variable access.
        if (logger.IsEnabled(LogLevel.Information))
        {
            string took = FormatElapsed(elapsed);
            LogLevelUp(logger, from, current, took);
        }
    }

    /// <summary>
    /// "1h 04m 12s", trimmed to the units that carry a value.
    /// </summary>
    private static string FormatElapsed(TimeSpan span)
    {
        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours}h {span.Minutes:00}m {span.Seconds:00}s";

        if (span.TotalMinutes >= 1)
            return $"{span.Minutes}m {span.Seconds:00}s";

        return $"{span.Seconds}s";
    }

    #region Logging

    [LoggerMessage(
        EventId = 1997,
        Level = LogLevel.Information,
        Message = "Level {from} -> {to}, took {elapsed}")]
    static partial void LogLevelUp(ILogger logger, int from, int to, string elapsed);

    [LoggerMessage(
        EventId = 1998,
        Level = LogLevel.Warning,
        Message = "Level changed {from} -> {to}")]
    static partial void LogLevelChanged(ILogger logger, int from, int to);

    #endregion
}
