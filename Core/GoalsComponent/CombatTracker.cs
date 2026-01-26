using Microsoft.Extensions.Logging;

using System;
using System.Diagnostics;

namespace Core;

public sealed partial class CombatTracker : IDisposable
{
    private const bool DEBUG = true;

    private readonly ILogger<CombatTracker> logger;
    private readonly AddonReader addonReader;
    private readonly PlayerReader playerReader;
    private readonly CombatLog combatLog;
    private readonly AddonBits bits;
    private readonly ConfigurableInput input;
    private readonly Wait wait;

    private bool inCombat;

    public long Started { get; private set; }

    public CombatTracker(ILogger<CombatTracker> logger,
        AddonReader addonReader,
        ConfigurableInput input,
        AddonBits bits, Wait wait, PlayerReader playerReader, CombatLog combatLog)
    {
        this.logger = logger;
        this.addonReader = addonReader;
        this.input = input;
        this.wait = wait;
        this.playerReader = playerReader;
        this.bits = bits;
        this.combatLog = combatLog;

        inCombat = bits.Combat();
        Started = Stopwatch.GetTimestamp();

        addonReader.GlobalTime.Changed += Update;
    }

    public void Dispose()
    {
        addonReader.GlobalTime.Changed -= Update;
    }

    private void Update()
    {
        if (inCombat && !bits.Combat())
        {
            LogLeftCombat(logger, Stopwatch.GetElapsedTime(Started).TotalSeconds);
        }
        else if (!inCombat && bits.Combat())
        {
            Started = Stopwatch.GetTimestamp();
            LogEnteredCombat(logger);
        }

        inCombat = bits.Combat();
    }

    public bool AcquiredTarget(int maxTimeMs = 400)
    {
        if (!bits.Combat())
            return false;

        if (playerReader.PetTarget())
        {
            input.PressTargetPet();
            wait.Update();
            Log($"Pets target {playerReader.TargetTarget}");
            if (playerReader.TargetTarget == UnitsTarget.PetHasATarget)
            {
                Log($"{nameof(AcquiredTarget)}: Found target by pet");
                input.PressTargetOfTarget();
                wait.Update();

                return true;
            }
        }

        input.PressNearestTarget();
        wait.Update();

        if (bits.Target() &&
            bits.Target_Combat() &&
            (bits.TargetTarget_PlayerOrPet() ||
            combatLog.DamageTaken.Contains(playerReader.TargetGuid)))
        {
            Log("Found new target");

            return true;
        }

        input.PressClearTarget();
        wait.Update();

        if (!wait.Till(maxTimeMs, PlayerOrPetHasTarget))
        {
            Log($"{nameof(AcquiredTarget)}: Someone started attacking me!");

            return true;
        }

        Log($"{nameof(AcquiredTarget)}: No target found after {maxTimeMs}ms");
        input.PressClearTarget();
        wait.Update();

        return false;
    }

    private bool PlayerOrPetHasTarget()
    {
        return bits.Target() || playerReader.PetTarget();
    }

    private void Log(string text)
    {
        if (DEBUG)
        {
            logger.LogDebug($"{text}");
        }
    }


    #region Logging

    [LoggerMessage(
        EventId = 1200,
        Level = LogLevel.Information,
        Message = "Entered Combat")]
    static partial void LogEnteredCombat(ILogger logger);

    [LoggerMessage(
        EventId = 1201,
        Level = LogLevel.Information,
        Message = "Left Combat after {elapsedSec:0.00}sec")]
    static partial void LogLeftCombat(ILogger logger, double elapsedSec);

    #endregion
}
