using Core.Goals;

using Microsoft.Extensions.Logging;

using System;
using System.Numerics;

using static System.MathF;

namespace Core;

public sealed partial class ReactCastError
{
    private readonly ILogger<ReactCastError> logger;
    private readonly PlayerReader playerReader;
    private readonly ActionBarBits<IUsableAction> usableAction;
    private readonly AddonBits bits;
    private readonly Wait wait;
    private readonly ConfigurableInput input;
    private readonly StopMoving stopMoving;
    private readonly PlayerDirection direction;
    private readonly AddonReader addonReader;

    public ReactCastError(ILogger<ReactCastError> logger,
        PlayerReader playerReader,
        AddonReader addonReader,
        ActionBarBits<IUsableAction> usableAction,
        AddonBits bits, Wait wait, ConfigurableInput input, StopMoving stopMoving,
        PlayerDirection direction)
    {
        this.logger = logger;
        this.playerReader = playerReader;
        this.addonReader = addonReader;
        this.usableAction = usableAction;
        this.bits = bits;
        this.wait = wait;
        this.input = input;
        this.stopMoving = stopMoving;
        this.direction = direction;
    }

    public void Do(KeyAction item)
    {
        UI_ERROR value = (UI_ERROR)playerReader.CastEvent.Value;
        switch (value)
        {
            case UI_ERROR.CAST_SUCCESS:
                WaitForCooldown(item, value);
                break;
            case UI_ERROR.CAST_SENT:
                UI_ERROR currentCastState = playerReader.CastState;
                int maxTime = Math.Min(playerReader.DoubleNetworkLatency, playerReader.RemainCastMs);
                LogReactCastSent(logger, value, maxTime);

                wait.Until(maxTime,
                    () => currentCastState != playerReader.CastState);
                break;
            case UI_ERROR.NONE:
            case UI_ERROR.CAST_START:
            case UI_ERROR.SPELL_FAILED_TARGETS_DEAD:
                break;
            case UI_ERROR.ERR_SPELL_FAILED_INTERRUPTED:
                item.SetClicked();
                break;
            case UI_ERROR.SPELL_FAILED_NOT_READY:
            /*
            int waitTime = Math.Max(playerReader.GCD.Value, playerReader.RemainCastMs);
            logger.LogInformation($"React to {value.ToString()} -- wait for GCD {waitTime}ms");
            if (waitTime > 0)
                wait.Fixed(waitTime);
            break;
            */
            case UI_ERROR.ERR_SPELL_COOLDOWN:
                WaitForCooldown(item, value);
                break;
            case UI_ERROR.ERR_ATTACK_PACIFIED:
            case UI_ERROR.ERR_SPELL_FAILED_STUNNED:
                int debuffCount = playerReader.AuraCount.PlayerDebuff;
                if (debuffCount != 0)
                {
                    LogReactWaitLoseDebuff(logger, value);

                    WaitDebuffChange(wait, debuffCount, playerReader);
                    static void WaitDebuffChange(Wait wait,
                        int debuffCount, PlayerReader playerReader) =>
                        wait.While(() =>
                        debuffCount == playerReader.AuraCount.PlayerDebuff);
                }
                else
                {
                    LogUnknownDebuffReaction(logger, value, debuffCount);
                }

                break;
            case UI_ERROR.ERR_SPELL_OUT_OF_RANGE:

                if (!bits.Target())
                    return;

                if (playerReader.Class == UnitClass.Hunter && playerReader.IsInMeleeRange())
                {
                    LogHunterUnknownReaction(logger, UnitClass.Hunter, value);
                    return;
                }

                int minRange = playerReader.MinRange();
                if (bits.Combat() && bits.Target() && !playerReader.IsTargetCasting())
                {
                    if (playerReader.TargetTarget == UnitsTarget.Me)
                    {
                        if (playerReader.InCloseMeleeRange())
                        {
                            LogReactWaitCloseMelee(logger, value, minRange);
                            wait.Update();
                            wait.Update();
                            return;
                        }

                        LogReactWaitTargetInRange(logger, value, minRange);

                        int duration = CastingHandler.GCD;
                        if (playerReader.MinRange() <= 5)
                            duration = CastingHandler.SPELL_QUEUE;

                        OutOfRange(duration, wait, minRange, playerReader);
                        static void OutOfRange(int duration, Wait wait,
                            int minRange, PlayerReader playerReader) =>
                            wait.Until(duration, () =>
                            minRange != playerReader.MinRange() || playerReader.IsTargetCasting());

                        wait.Update();
                    }
                }
                else
                {
                    double beforeDirection = playerReader.Direction;
                    input.PressInteract();
                    input.PressStopAttack();
                    stopMoving.Stop();
                    wait.Update();

                    if (beforeDirection != playerReader.Direction)
                    {
                        input.PressInteract();

                        MinRangeChanges(CastingHandler.GCD, wait, minRange, playerReader);
                        static void MinRangeChanges(int duration, Wait wait,
                            int minRange, PlayerReader playerReader) =>
                            wait.Until(duration, () =>
                            minRange != playerReader.MinRange());

                            if (logger.IsEnabled(LogLevel.Information))
                            {
                                int currentMinRange = playerReader.MinRange();
                                LogReactApproachedTarget(logger, value, minRange, currentMinRange);
                            }
                    }
                    else if (!playerReader.WithInPullRange())
                    {
                        LogReactStartMovingForward(logger, value);
                        input.StartForward(true);
                    }
                    else
                    {
                        input.PressInteract();
                    }
                }
                break;
            case UI_ERROR.ERR_BADATTACKFACING:

                bool wasAnyAuto = bits.Any_AutoAttack();
                bool turnedWithInteract = false;

                // Try fast interact if no invalid soft target exists
                if (!bits.SoftInteract_CombatBlocker())
                {
                    float beforeDir = playerReader.Direction;
                    input.PressFastInteract();

                    const int updateCount = 4;
                    float e = wait.AfterEquals(playerReader.SpellQueueTimeMs,
                        updateCount, playerReader._Direction);

                    float sampleTimeMs =
                        updateCount * (float)addonReader.AvgUpdateLatency;

                    if (e > sampleTimeMs)
                    {
                        stopMoving.Stop();
                        LogReactFastTurnInteract(logger, value, e);
                        turnedWithInteract = true;
                    }
                    else
                    {
                        LogUnableToReactFastTurn(logger, value, e);

                        // Check if we turned at all (even if slowly)
                        turnedWithInteract = beforeDir != playerReader.Direction;
                    }
                }

                // Fallback: slow turn 180 degrees if interact didn't work or was skipped
                if (!turnedWithInteract)
                {
                    stopMoving.Stop();

                    float targetDir = playerReader.Direction + PI;
                    if (targetDir > Tau)
                        targetDir -= Tau;

                    direction.SetDirection(targetDir, Vector3.Zero);

                    string reason = bits.SoftInteract_CombatBlocker()
                        ? "invalid soft target"
                        : "interact failed";
                    LogReactSlowTurn(logger, value, reason);
                }

                if (!wasAnyAuto)
                    input.PressStopAttack();

                break;
            case UI_ERROR.SPELL_FAILED_MOVING:
                wait.While(bits.Falling);
                stopMoving.Stop();
                float stopElapsedMs = wait.Until(CastingHandler.SPELL_QUEUE_HALF, bits.NotMoving);
                LogReactStopMoving(logger, value, stopElapsedMs);
                break;
            case UI_ERROR.ERR_SPELL_FAILED_ANOTHER_IN_PROGRESS:
                LogReactWaitTillCasting(logger, value);
                wait.While(playerReader.IsCasting);
                break;
            case UI_ERROR.ERR_BADATTACKPOS:
                if (bits.Auto_Attack())
                {
                    LogReactInteract(logger, value);
                    input.PressInteract();
                    stopMoving.Stop();
                    wait.Update();
                }
                else
                {
                    goto default;
                }
                break;
            case UI_ERROR.SPELL_FAILED_LINE_OF_SIGHT:
                if (!bits.Combat())
                {
                    LogReactStopAttackClearTarget(logger, value);
                    input.PressStopAttack();
                    input.PressClearTarget();
                    wait.Update();
                }
                else
                {
                    goto default;
                }
                break;
            default:
                LogUnknownReaction(logger, value);
                break;
        }
    }

    private void WaitForCooldown(KeyAction item, UI_ERROR value)
    {
        LogReactWaitUntilReady(logger, value);
        int waitTime = Math.Max(playerReader.GCD.Value, playerReader.RemainCastMs);
        bool before = usableAction.Is(item);

        WaitCooldown(waitTime, before, wait, usableAction, item);
        static void WaitCooldown(int duration, bool before, Wait wait,
            ActionBarBits<IUsableAction> usableAction, KeyAction item) =>
            wait.Until(duration, () =>
            before != usableAction.Is(item) || usableAction.Is(item));

    }

    [LoggerMessage(EventId = 3000, Level = LogLevel.Information, Message = "React to {UiError} -- by waiting {MaxTime}ms!")]
    private static partial void LogReactCastSent(ILogger logger, UI_ERROR uiError, int maxTime);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "React to {UiError} -- Wait till losing debuff!")]
    private static partial void LogReactWaitLoseDebuff(ILogger logger, UI_ERROR uiError);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "Didn't know how to react {UiError} when PlayerDebuffCount: {DebuffCount}")]
    private static partial void LogUnknownDebuffReaction(ILogger logger, UI_ERROR uiError, int debuffCount);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Information, Message = "As a {UnitClass} didn't know how to react {UiError}")]
    private static partial void LogHunterUnknownReaction(ILogger logger, UnitClass unitClass, UI_ERROR uiError);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Information, Message = "React to {UiError} -- ({MinRange}) wait for close melee range.")]
    private static partial void LogReactWaitCloseMelee(ILogger logger, UI_ERROR uiError, int minRange);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Information, Message = "React to {UiError} -- ({MinRange}) Just wait for the target to get in range.")]
    private static partial void LogReactWaitTargetInRange(ILogger logger, UI_ERROR uiError, int minRange);

    [LoggerMessage(EventId = 3006, Level = LogLevel.Information, Message = "React to {UiError} -- Approached target {OldMinRange}->{NewMinRange}")]
    private static partial void LogReactApproachedTarget(ILogger logger, UI_ERROR uiError, int oldMinRange, int newMinRange);

    [LoggerMessage(EventId = 3007, Level = LogLevel.Information, Message = "React to {UiError} -- Start moving forward as outside of pull range.")]
    private static partial void LogReactStartMovingForward(ILogger logger, UI_ERROR uiError);

    [LoggerMessage(EventId = 3008, Level = LogLevel.Information, Message = "React to {UiError} - Fast turn with Interact {ElapsedMs}ms")]
    private static partial void LogReactFastTurnInteract(ILogger logger, UI_ERROR uiError, float elapsedMs);

    [LoggerMessage(EventId = 3009, Level = LogLevel.Warning, Message = "Unable to react to {UiError} - Fast turn with Interact {ElapsedMs}ms")]
    private static partial void LogUnableToReactFastTurn(ILogger logger, UI_ERROR uiError, float elapsedMs);

    [LoggerMessage(EventId = 3010, Level = LogLevel.Information, Message = "React to {UiError} - Slow turn 180deg ({Reason})")]
    private static partial void LogReactSlowTurn(ILogger logger, UI_ERROR uiError, string reason);

    [LoggerMessage(EventId = 3011, Level = LogLevel.Information, Message = "React to {UiError} -- Stop moving! Took {StopElapsedMs}ms")]
    private static partial void LogReactStopMoving(ILogger logger, UI_ERROR uiError, float stopElapsedMs);

    [LoggerMessage(EventId = 3012, Level = LogLevel.Information, Message = "React to {UiError} -- Wait till casting!")]
    private static partial void LogReactWaitTillCasting(ILogger logger, UI_ERROR uiError);

    [LoggerMessage(EventId = 3013, Level = LogLevel.Information, Message = "React to {UiError} -- Interact!")]
    private static partial void LogReactInteract(ILogger logger, UI_ERROR uiError);

    [LoggerMessage(EventId = 3014, Level = LogLevel.Information, Message = "React to {UiError} -- Stop attack and clear target!")]
    private static partial void LogReactStopAttackClearTarget(ILogger logger, UI_ERROR uiError);

    [LoggerMessage(EventId = 3015, Level = LogLevel.Information, Message = "Didn't know how to React to {UiError}")]
    private static partial void LogUnknownReaction(ILogger logger, UI_ERROR uiError);

    [LoggerMessage(EventId = 3016, Level = LogLevel.Information, Message = "React to {UiError} -- wait until its ready")]
    private static partial void LogReactWaitUntilReady(ILogger logger, UI_ERROR uiError);
}
