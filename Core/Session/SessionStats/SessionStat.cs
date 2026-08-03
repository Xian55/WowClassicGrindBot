using System;

using static System.Diagnostics.Stopwatch;


namespace Core;

public sealed class SessionStat
{
    public int Deaths { get; set; }
    public int Kills { get; set; }

    public long StartTime { get; set; }

    /// <summary>
    /// Set to true when vendor/repair (AdhocNPCGoal) completes successfully.
    /// Cleared when MailGoal completes successfully.
    /// Used to ensure Mail only runs after Vendor/Repair.
    /// </summary>
    public bool VendoredOrRepairedRecently { get; set; }

    /// <summary>
    /// When the last successful vendor/repair and class trainer visit happened, and how
    /// many spells have been learnt this session.
    /// <para>
    /// Exposed as elapsed seconds rather than as another "recently" flag on purpose: a
    /// flag needs someone to clear it - <see cref="VendoredOrRepairedRecently"/> latches
    /// true until MailGoal happens to run - whereas elapsed time answers both "did this
    /// just happen" and "has it been a while" without any handshake, and needs no reset.
    /// A profile composes whatever window it wants.
    /// </para>
    /// </summary>
    public long LastVendoredTime { get; set; }

    public long LastTrainedTime { get; set; }

    public int SpellsTrained { get; set; }

    public int _Deaths() => Deaths;

    public int _Kills() => Kills;

    public int Seconds => (int)GetElapsedTime(StartTime).TotalSeconds;

    public int _Seconds() => Seconds;

    public int Minutes => (int)GetElapsedTime(StartTime).TotalMinutes;

    public int _Minutes() => Minutes;

    public int Hours => (int)GetElapsedTime(StartTime).TotalHours;

    public int _Hours() => Hours;

    public bool _VendoredOrRepairedRecently() => VendoredOrRepairedRecently;

    /// <summary>
    /// Seconds since the event, or <see cref="int.MaxValue"/> when it has not happened
    /// this session - so "it has been a while" reads true before the first one, which is
    /// what a profile gating on a cooldown wants.
    /// </summary>
    private static int SecondsSince(long timestamp) =>
        timestamp == 0 ? int.MaxValue : (int)GetElapsedTime(timestamp).TotalSeconds;

    public int SecondsSinceVendored => SecondsSince(LastVendoredTime);

    public int _SecondsSinceVendored() => SecondsSinceVendored;

    public int SecondsSinceTrained => SecondsSince(LastTrainedTime);

    public int _SecondsSinceTrained() => SecondsSinceTrained;

    public int _SpellsTrained() => SpellsTrained;

    public void OnVendored()
    {
        VendoredOrRepairedRecently = true;
        LastVendoredTime = GetTimestamp();
    }

    public void OnTrained(int spellCount)
    {
        SpellsTrained += spellCount;
        LastTrainedTime = GetTimestamp();
    }

    public void Reset()
    {
        Deaths = 0;
        Kills = 0;
        VendoredOrRepairedRecently = false;

        LastVendoredTime = 0;
        LastTrainedTime = 0;
        SpellsTrained = 0;
    }

    public void Start()
    {
        StartTime = GetTimestamp();
    }
}
