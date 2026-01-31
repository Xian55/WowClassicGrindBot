using System;

using static System.Diagnostics.Stopwatch;


namespace Core;

public sealed class SessionStat
{
    public event Action? OnDeath;

    public int Deaths { get; set; }
    public int Kills { get; set; }

    public long StartTime { get; set; }

<<<<<<< Updated upstream
    /// <summary>
    /// Set to true when vendor/repair (AdhocNPCGoal) completes successfully.
    /// Cleared when MailGoal completes successfully.
    /// Used to ensure Mail only runs after Vendor/Repair.
    /// </summary>
    public bool VendoredOrRepairedRecently { get; set; }
=======
    // Stuck tracking - updated by StuckDetector
    private long stuckStartTime;
    private bool isStuck;

    public int StuckSeconds => isStuck
        ? (int)GetElapsedTime(stuckStartTime).TotalSeconds
        : 0;

    public int _StuckSeconds() => StuckSeconds;

    public void SetStuck(bool stuck)
    {
        if (stuck && !isStuck)
        {
            stuckStartTime = GetTimestamp();
            isStuck = true;
        }
        else if (!stuck && isStuck)
        {
            isStuck = false;
        }
    }
>>>>>>> Stashed changes

    public int _Deaths() => Deaths;

    public int _Kills() => Kills;

    public void RecordDeath()
    {
        Deaths++;
        OnDeath?.Invoke();
    }

    public int Seconds => (int)GetElapsedTime(StartTime).TotalSeconds;

    public int _Seconds() => Seconds;

    public int Minutes => (int)GetElapsedTime(StartTime).TotalMinutes;

    public int _Minutes() => Minutes;

    public int Hours => (int)GetElapsedTime(StartTime).TotalHours;

    public int _Hours() => Hours;

    public bool _VendoredOrRepairedRecently() => VendoredOrRepairedRecently;

    public void Reset()
    {
        Deaths = 0;
        Kills = 0;
        VendoredOrRepairedRecently = false;
    }

    public void Start()
    {
        StartTime = GetTimestamp();
    }
}
