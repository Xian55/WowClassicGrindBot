using Core.Goals;

using Microsoft.Extensions.Logging;

using System;
using System.Numerics;

using static System.MathF;

namespace Core;

public sealed class ReactCastError
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
                if (logger.IsEnabled(LogLevel.Information))
                    logger.LogInformation($"React to {value.ToStringF()} -- by waiting {maxTime}ms!");

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
            logger.LogInformation($"React to {value.ToStringF()} -- wait for GCD {waitTime}ms");
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
                    if (logger.IsEnabled(LogLevel.Information))
                        logger.LogInformation($"React to {value.ToStringF()} -- Wait till losing debuff!");

                    WaitDebuffChange(wait, debuffCount, playerReader);
                    static void WaitDebuffChange(Wait wait,
                        int debuffCount, PlayerReader playerReader) =>
                        wait.While(() =>
                        debuffCount == playerReader.AuraCount.PlayerDebuff);
                }
                else
                {
                    if (logger.IsEnabled(LogLevel.Information))
                        logger.LogInformation($"Didn't know how to react {value.ToStringF()} when PlayerDebuffCount: {debuffCount}");
                }

                break;
            case UI_ERROR.ERR_SPELL_OUT_OF_RANGE:

                if (!bits.Target())
                    return;

                if (playerReader.Class == UnitClass.Hunter && playerReader.IsInMeleeRange())
                {
                    if (logger.IsEnabled(LogLevel.Information))
                        logger.LogInformation($"As a {UnitClass.Hunter.ToStringF()} didn't know how to react {value.ToStringF()}");
                    return;
                }

                int minRange = playerReader.MinRange();
                if (bits.Combat() && bits.Target() && !playerReader.IsTargetCasting())
                {
                    if (playerReader.TargetTarget == UnitsTarget.Me)
                    {
                        if (playerReader.InCloseMeleeRange())
                        {
                            if (logger.IsEnabled(LogLevel.Information))
                                logger.LogInformation($"React to {value.ToStringF()} -- ({minRange}) wait for close melee range.");
                            wait.Update();
                            wait.Update();
                            return;
                        }

                        if (logger.IsEnabled(LogLevel.Information))
                            logger.LogInformation($"React to {value.ToStringF()} -- ({minRange}) Just wait for the target to get in range.");

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
                            logger.LogInformation($"React to {value.ToStringF()} -- Approached target {minRange}->{playerReader.MinRange()}");
                    }
                    else if (!playerReader.WithInPullRange())
                    {
                        if (logger.IsEnabled(LogLevel.Information))
                            logger.LogInformation($"React to {value.ToStringF()} -- Start moving forward as outside of pull range.");
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
                        if (logger.IsEnabled(LogLevel.Information))
                            logger.LogInformation(
                                $"React to {value.ToStringF()} - " +
                                $"Fast turn with Interact {e}ms");
                        turnedWithInteract = true;
                    }
                    else
                    {
                        if (logger.IsEnabled(LogLevel.Warning))
                            logger.LogWarning(
                                $"Unable to react to {value.ToStringF()} - " +
                                $"Fast turn with Interact {e}ms");

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
                    if (logger.IsEnabled(LogLevel.Information))
                        logger.LogInformation(
                            $"React to {value.ToStringF()} - Slow turn 180deg ({reason})");
                }

                if (!wasAnyAuto)
                    input.PressStopAttack();

                break;
            case UI_ERROR.SPELL_FAILED_MOVING:
                if (logger.IsEnabled(LogLevel.Information))
                    logger.LogInformation($"React to {value.ToStringF()} -- Stop moving!");
                wait.While(bits.Falling);
                stopMoving.Stop();
                wait.Update();
                break;
            case UI_ERROR.ERR_SPELL_FAILED_ANOTHER_IN_PROGRESS:
                if (logger.IsEnabled(LogLevel.Information))
                    logger.LogInformation($"React to {value.ToStringF()} -- Wait till casting!");
                wait.While(playerReader.IsCasting);
                break;
            case UI_ERROR.ERR_BADATTACKPOS:
                if (bits.Auto_Attack())
                {
                    if (logger.IsEnabled(LogLevel.Information))
                        logger.LogInformation($"React to {value.ToStringF()} -- Interact!");
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
                    if (logger.IsEnabled(LogLevel.Information))
                        logger.LogInformation($"React to {value.ToStringF()} -- Stop attack and clear target!");
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
                if (logger.IsEnabled(LogLevel.Information))
                    logger.LogInformation($"Didn't know how to React to {value.ToStringF()}");
                break;
        }
    }

    private void WaitForCooldown(KeyAction item, UI_ERROR value)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation($"React to {value.ToStringF()} -- wait until its ready");
        int waitTime = Math.Max(playerReader.GCD.Value, playerReader.RemainCastMs);
        bool before = usableAction.Is(item);

        WaitCooldown(waitTime, before, wait, usableAction, item);
        static void WaitCooldown(int duration, bool before, Wait wait,
            ActionBarBits<IUsableAction> usableAction, KeyAction item) =>
            wait.Until(duration, () =>
            before != usableAction.Is(item) || usableAction.Is(item));

    }
}
