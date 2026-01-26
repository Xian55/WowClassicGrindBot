using System;

using static System.Diagnostics.Stopwatch;


namespace Core;

public sealed class SessionStat
{
    public int Deaths { get; set; }
    public int Kills { get; set; }

    public long StartTime { get; set; }

    public int _Deaths() => Deaths;

    public int _Kills() => Kills;

    public int Seconds => (int)GetElapsedTime(StartTime).TotalSeconds;

    public int _Seconds() => Seconds;

    public int Minutes => (int)GetElapsedTime(StartTime).TotalMinutes;

    public int _Minutes() => Minutes;

    public int Hours => (int)GetElapsedTime(StartTime).TotalHours;

    public int _Hours() => Hours;

    public void Reset()
    {
        Deaths = 0;
        Kills = 0;
    }

    public void Start()
    {
        StartTime = GetTimestamp();
    }
}
