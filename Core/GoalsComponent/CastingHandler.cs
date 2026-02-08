using Game;

using Microsoft.Extensions.Logging;

using System;
using static System.Diagnostics.Stopwatch;
using System.Threading;

using static Core.AddonTicks;
using static System.Math;

namespace Core.Goals;

public sealed partial class CastingHandler
{
#if DEBUG
    private const bool DEBUG = true;
#else
    private const bool DEBUG = false;
#endif

    public const int GCD = 1500;
    public const int MIN_GCD = 1000;
    public const int SPELL_QUEUE = 400;

    public const int SPELL_QUEUE_HALF = 200;

    private const int MAX_WAIT_MELEE_RANGE = 10_000;

    private readonly ILogger<CastingHandler> logger;
    private readonly bool Log;
    private readonly ConfigurableInput input;

    private readonly Wait wait;
    private readonly PlayerReader playerReader;
    private readonly AddonReader addonReader;
    private readonly AddonBits bits;
    private readonly BagReader bagReader;

    private readonly CombatLog combatLog;

    private readonly ActionBarBits<IUsableAction> usableAction;
    private readonly ActionBarBits<ICurrentAction> currentAction;

    private readonly ClassConfiguration classConfig;
    private readonly FormKeyActions forms;
    private readonly StopMoving stopMoving;

    private readonly ReactCastError react;

    private readonly CastingHandlerInterruptWatchdog interruptWatchdog;

    public bool SpellInQueue()
    {
        if (playerReader.IsCasting())
        {
            return playerReader.RemainCastMs > playerReader.SpellQueueTimeMs;
        }
        else if (playerReader.GCD.Value != 0 &&
            playerReader.GCD.Value < playerReader.HalfSpellQueueTimeMs)
        {
            return true;
        }

        return false;
    }

    public static int _GCD() => GCD;

    public CastingHandler(
        ILogger<CastingHandler> logger,
        ConfigurableInput input,
        ClassConfiguration classConfig,
        AddonBits bits,
        ActionBarBits<IUsableAction> usableAction,
        ActionBarBits<ICurrentAction> currentAction,
        Wait wait,
        AddonReader addonReader,
        PlayerReader playerReader,
        BagReader bagReader,
        CombatLog combatLog,
        StopMoving stopMoving,
        ReactCastError react,
        CastingHandlerInterruptWatchdog interruptWatchdog)
    {
        this.logger = logger;
        this.input = input;

        this.wait = wait;
        this.bits = bits;
        this.addonReader = addonReader;
        this.playerReader = playerReader;
        this.bagReader = bagReader;

        this.combatLog = combatLog;

        this.usableAction = usableAction;
        this.currentAction = currentAction;

        this.classConfig = classConfig;
        Log = classConfig.Log;
        forms = classConfig.Form;

        this.stopMoving = stopMoving;

        this.react = react;

        this.interruptWatchdog = interruptWatchdog;
    }

    private int PressKeyAction(KeyAction item, CancellationToken token)
    {
        playerReader.LastUIError = UI_ERROR.NONE;

        if (item.AfterCastWaitSwing)
        {
            if (Log && item.Log)
                LogAfterCastWaitSwing(logger, item.Name);

            input.PressStopAttack();
        }

        long start = GetTimestamp();
        input.PressRandom(item, token);
        return (int)GetElapsedTime(start).TotalMilliseconds;
    }

    private static bool CastInstantSuccessful(int uiEvent)
    {
        return
            (UI_ERROR)uiEvent is
            UI_ERROR.CAST_START or
            UI_ERROR.CAST_SENT or
            UI_ERROR.CAST_SUCCESS or
            UI_ERROR.SPELL_FAILED_TARGETS_DEAD;
    }

    private static float WaitCurrentAction(int duration, Wait wait,
        PlayerReader playerReader,
        KeyAction item, ActionBarBits<ICurrentAction> currentAction,
        CancellationToken token)
    {
        return wait.Until(duration, Interrupt);

        bool Interrupt() =>
            currentAction.Is(item) ||
            playerReader.CastState == UI_ERROR.CAST_SENT ||
            token.IsCancellationRequested;
    }

    private CastResult CastInstant(KeyAction item, CancellationToken token)
    {
        if (!playerReader.IsCasting() && bits.Moving() && item.BeforeCastStop)
        {
            stopMoving.Stop();
            wait.Update(token);
        }

        int beforeAuraHash = playerReader.AuraCount.Hash;
        int beforeCastEventTime = playerReader.UIErrorTime.Value;
        UI_ERROR beforeCastEventValue = playerReader.CastState;
        bool beforeUsable = usableAction.Is(item);
        bool beforeAction = currentAction.Is(item);

        int pressMs = PressKeyAction(item, token);
        if (item.BaseAction)
        {
            item.SetClicked();

            if (Log && item.Log)
                LogInstantBaseAction(logger, item.Name, pressMs);

            return CastResult.Success;
        }

        float elapsedMs = WaitCurrentAction(
            Max(playerReader.HalfSpellQueueTimeMs, playerReader.RemainCastMs)
            + playerReader.NetworkLatency,
            wait, playerReader, item, currentAction, token);

        if (DEBUG && Log && item.Log)
            if (logger.IsEnabled(LogLevel.Information))
                LogInstantInput(logger, item.Name, pressMs,
                    playerReader.CastState.ToStringF(), elapsedMs);

        if (elapsedMs < 0)
        {
            if (!DEBUG || (Log && item.Log))
                if (logger.IsEnabled(LogLevel.Information))
                    LogInstantInput(logger, item.Name, pressMs,
                        playerReader.CastState.ToStringF(), elapsedMs);

            return CastResult.CurrentActionNotDetected;
        }

        // Melee Swing
        if (item.AfterCastWaitSwing)
        {
            elapsedMs = AfterCastWaitSwing(
                playerReader.MainHandSpeedMs() + playerReader.NetworkLatency,
                wait, item, playerReader, currentAction,
                input.PressApproachOnCooldown, token);

            static float AfterCastWaitSwing(int duration, Wait wait,
                KeyAction item,
                PlayerReader playerReader,
                ActionBarBits<ICurrentAction> currentAction,
                Action repeat, CancellationToken token)
                => wait.Until(
                    playerReader.MainHandSpeedMs() + playerReader.NetworkLatency,
                interrupt: () =>
                    !currentAction.Is(item) ||
                    playerReader.MainHandSwing.ElapsedMs() < playerReader.SpellQueueTimeMs ||
                    token.IsCancellationRequested, // swing timer reset from any miss
                repeat: repeat);
        }
        // Trinkets / Consumables
        // Spells appears in CombatLog
        else
        {
            int waitTime = playerReader.SpellQueueTimeMs + playerReader.DoubleNetworkLatency;

            elapsedMs = InstantSpell(waitTime,
                beforeCastEventTime, beforeAuraHash, item,
                playerReader, wait, token);

            static float InstantSpell(int durationMs,
                int beforeCastEventTime,
                int beforeAuraHash,
                KeyAction item,
                PlayerReader playerReader, Wait wait,
                CancellationToken token)
            {
                return wait.Until(durationMs,
                    interrupt: () =>
                    beforeAuraHash != playerReader.AuraCount.Hash ||
                    beforeCastEventTime != playerReader.UIErrorTime.Value ||
                    token.IsCancellationRequested);
            }
        }

        if (DEBUG && Log && item.Log)
            if (logger.IsEnabled(LogLevel.Information))
                LogInstantUsableChange(logger, item.Name,
                    beforeUsable, usableAction.Is(item),
                    beforeAction, currentAction.Is(item),
                    beforeCastEventValue.ToStringF(),
                    playerReader.CastState.ToStringF());

        if (elapsedMs < 0)
        {
            if (!DEBUG || (Log && item.Log))
                if (logger.IsEnabled(LogLevel.Information))
                    LogInstantUsableChange(logger, item.Name,
                        beforeUsable, usableAction.Is(item),
                        beforeAction, currentAction.Is(item),
                        beforeCastEventValue.ToStringF(),
                        playerReader.CastState.ToStringF());

            return CastResult.UIFeedbackNotDetected;
        }

        if (!CastInstantSuccessful(playerReader.CastEvent.Value))
        {
            if (logger.IsEnabled(LogLevel.Warning))
                LogInstantInputFailed(logger, item.Name, pressMs, playerReader.CastState.ToStringF(), elapsedMs);
            return CastResult.UIError;
        }
        else if (!DEBUG && Log && item.Log)
        {
            if (logger.IsEnabled(LogLevel.Information))
                LogInstantInput(logger, item.Name, pressMs,
                    playerReader.CastState.ToStringF(), elapsedMs);
        }

        if (playerReader.CastState == UI_ERROR.CAST_SUCCESS)
        {
            item.SpellId = playerReader.CastSpellId.Value;
        }

        item.SetClicked();
        if (item.Item)
        {
            playerReader.ResetLastCastGCD();
            wait.Update(token);
        }
        else
            playerReader.ReadLastCastGCD();

        return CastResult.Success;
    }

    private CastResult CastCastbar(KeyAction item, CancellationToken token)
    {
        wait.While(bits.Falling, token);

        if (!playerReader.IsCasting() && bits.Moving())
        {
            stopMoving.Stop();
            wait.Update(token);
        }

        bool beforeUsable = usableAction.Is(item);
        int beforeCastEventTime = playerReader.UIErrorTime.Value;
        UI_ERROR beforeCastEventValue = playerReader.CastState;

        int pressMs = PressKeyAction(item, token);

        if (item.BaseAction)
        {
            if (Log && item.Log)
                LogCastbarBaseAction(logger, item.Name, pressMs);

            item.SetClicked();
            return CastResult.Success;
        }

        float elapsedMs = WaitCurrentAction(
            playerReader.DoubleNetworkLatency + playerReader.SpellQueueTimeMs,
            wait, playerReader, item, currentAction, token);

        // Channeling spells like Cannibalize
        // does not trigger the IsUsableAction
        // less then 2 miliseconds
        if (elapsedMs < 2)
        {
            wait.Fixed(Max(playerReader.DoubleNetworkLatency, SPELL_QUEUE_HALF));
            wait.Update(token);
        }

        if (DEBUG && Log && item.Log)
            LogCastbarInput(logger, item.Name, pressMs, elapsedMs);

        if (elapsedMs < 0)
        {
            if (!DEBUG || (Log && item.Log))
                LogCastbarInput(logger, item.Name, pressMs, elapsedMs);

            return CastResult.CurrentActionNotDetected;
        }

        elapsedMs = WaitTilUIErrorTimeChange(
            playerReader.DoubleNetworkLatency,
            beforeCastEventTime,
            wait, playerReader, token);

        if (DEBUG && Log && item.Log)
            if (logger.IsEnabled(LogLevel.Information))
                LogCastbarUsableChange(logger, item.Name,
                    playerReader.IsCasting(), bits.Channeling(),
                    playerReader.CastCount, beforeUsable, usableAction.Is(item),
                    beforeCastEventValue.ToStringF(),
                    playerReader.CastState.ToStringF());

        if (playerReader.CastState is < UI_ERROR.MAX_ERROR_RANGE and
            not UI_ERROR.NONE)
        {
            return CastResult.UIError;
        }

        // at this point the player has castbar
        playerReader.ResetLastCastGCD();
        item.SpellId = playerReader.CastSpellId.Value;
        item.SetClicked();

        if (item.AfterCastWaitCastbar)
        {
            if (playerReader.IsCasting() || bits.Channeling())
            {
                int remainMs = playerReader.RemainCastMs - playerReader.SpellQueueTimeMs;
                if (Log && item.Log)
                    LogVisibleAfterCastWaitCastbar(logger, item.Name, remainMs);

                WaitTillNoLongerCastingOrChanneling(remainMs, wait, playerReader, bits, RepeatPetAttack, token);
                if (token.IsCancellationRequested)
                {
                    if (Log && item.Log)
                        LogVisibleAfterCastWaitCastbarInterrupted(logger, item.Name);

                    return CastResult.TokenInterrupted;
                }
            }
            else if (playerReader.CastState == UI_ERROR.CAST_START)
            {
                beforeCastEventValue = playerReader.CastState;

                int remainMs = playerReader.RemainCastMs - playerReader.SpellQueueTimeMs;
                if (Log && item.Log)
                    LogHiddenAfterCastWaitCastbar(logger, item.Name, remainMs);

                WaitTilCastStateChange(remainMs, beforeCastEventValue, wait, playerReader, RepeatPetAttack, token);

                if (token.IsCancellationRequested)
                {
                    if (Log && item.Log)
                        LogHiddenAfterCastWaitCastbarInterrupted(logger, item.Name);

                    return CastResult.TokenInterrupted;
                }
            }

            // Apply the Cooldown
            // Successfull Cast
            item.SetClicked();
        }
        else if (!DEBUG && Log && item.Log)
            LogCastbarInput(logger, item.Name, pressMs, elapsedMs);

        return CastResult.Success;
    }

    private static float WaitTilUIErrorTimeChange(int durationMs, int beforeCastEventTime,
        Wait wait, PlayerReader playerReader, CancellationToken token)
    {
        return wait.Until(durationMs,
            interrupt: () =>
            beforeCastEventTime != playerReader.UIErrorTime.Value ||
            token.IsCancellationRequested);
    }

    private static void WaitTillNoLongerCastingOrChanneling(int remainMs, Wait wait,
        PlayerReader playerReader, AddonBits bits, Action repeat, CancellationToken token)
    {
        wait.Until(remainMs,
            interrupt: () =>
            (!playerReader.IsCasting() && !bits.Channeling()) ||
            token.IsCancellationRequested,
            repeat);
    }

    private static float WaitTilCastStateChange(int durationMs, UI_ERROR beforeCastEventValue,
        Wait wait, PlayerReader playerReader, Action repeat, CancellationToken token)
    {
        return wait.Until(durationMs,
            interrupt: () =>
            beforeCastEventValue != playerReader.CastState ||
            token.IsCancellationRequested,
            repeat);
    }

    public bool CastIfReady(KeyAction item, Func<bool> interrupt)
    {
        return item.CanRun() && Cast(item, interrupt);
    }

    private bool PreparedForCast(KeyAction item, CancellationToken token)
    {
        if (item.HasForm && playerReader.Form != item.FormValue)
        {
            bool beforeUsable = usableAction.Is(item);
            Form beforeForm = playerReader.Form;

            if (!SwitchForm(item, token))
            {
                return false;
            }

            if (!WaitForGCD(item, false, false, token))
            {
                return false;
            }

            //TODO: upon form change and GCD - have to check Usable state
            if (!beforeUsable && !usableAction.Is(item))
            {
                if (Log && item.Log)
                    if (logger.IsEnabled(LogLevel.Warning))
                        LogAfterFormSwitchNotUsable(logger, item.Name, beforeForm.ToStringF(), playerReader.Form.ToStringF());

                return false;
            }
        }

        if (bits.Shoot())
        {
            input.PressStopAttack();
            input.PressStopAttack();

            int waitTimeMs = playerReader.GCD.Value;

            float elapsedMs = wait.Until(waitTimeMs, token);
            logger.LogInformation("Stop Shoot and wait {WaitTimeMs}ms | {ElapsedMs}ms", waitTimeMs, elapsedMs);

            if (elapsedMs >= 0)
            {
                wait.Update(token);
                return false;
            }
        }

        if (!item.BaseAction && !WaitForGCD(item, true, false, token))
        {
            return false;
        }

        if (item.BeforeCastFaceTarget && !playerReader.IsCasting())
        {
            input.PressFastInteract();

            const int updateCount = 2;
            float e = wait.AfterEquals(playerReader.SpellQueueTimeMs,
                updateCount, playerReader._Direction);

            stopMoving.StopForward();

            if (Log && item.Log)
                LogBeforeCastFaceTarget(logger, item.Name, e);
        }

        if (item.BeforeCastDelay > 0)
        {
            if (!playerReader.IsCasting() && bits.Moving() && (item.BeforeCastStop || item.HasCastBar))
            {
                stopMoving.Stop();
                wait.Update(token);
            }
            int delay = Random.Shared.Next(item.BeforeCastDelay, item.BeforeCastMaxDelay);

            if (Log && item.Log)
                LogBeforeCastDelay(logger, item.Name, delay);

            wait.Until(delay, token);
        }

        return true;
    }

    private void CheckPostCastFlags(KeyAction item, int auraHash, CancellationToken token)
    {
        int bagHash = bagReader.Hash;

        if (item.AfterCastWaitBuff)
        {
            int totalTime =
                Max(playerReader.GCD.Value, playerReader.RemainCastMs) +
                playerReader.SpellQueueTimeMs +
                playerReader.NetworkLatency;

            float elapsedMs = AfterCastWaitBuff(totalTime,
                auraHash, wait, playerReader, combatLog, token);

            if (Log && item.Log)
                if (logger.IsEnabled(LogLevel.Information))
                    LogAfterCastWaitBuff(logger,
                        item.Name, playerReader.AuraCount.ToString(),
                        ((MissType)combatLog.TargetMissType.Value).ToStringF(), elapsedMs);

            static float AfterCastWaitBuff(int totalTime, int auraHash, Wait wait,
                PlayerReader playerReader, CombatLog combatLog, CancellationToken token)
                => wait.Until(totalTime, () =>
                auraHash != playerReader.AuraCount.Hash ||
                (MissType)combatLog.TargetMissType.Value != MissType.NONE ||
                token.IsCancellationRequested);
        }

        if (item.AfterCastAuraExpected)
        {
            int delay = playerReader.SpellQueueTimeMs +
                Max(playerReader.RemainCastMs,
                item.Item ? 0 : playerReader.LastCastGCD);

            if (Log && item.Log)
                if (logger.IsEnabled(LogLevel.Information))
                    LogAfterCastAuraExpected(logger, item.Name,
                        ((MissType)combatLog.TargetMissType.Value).ToStringF(), delay);

            item.SetClicked(delay);
        }

        if (item.AfterCastWaitBag)
        {
            int waitTimeMs =
                playerReader.SpellQueueTimeMs +
                playerReader.DoubleNetworkLatency +
                (BAG_UPDATE * (int)addonReader.AvgUpdateLatency);

            float elapsedMs = AfterCastWaitBag(waitTimeMs, bagHash, wait, bagReader, token);
            if (Log && item.Log)
                LogAfterCastWaitBag(logger, item.Name, bagHash, bagReader.Hash, elapsedMs);

            static float AfterCastWaitBag(int totalTime, int bagHash, Wait wait,
                BagReader bagReader, CancellationToken token) =>
                wait.Until(totalTime,
                () => bagHash != bagReader.Hash || token.IsCancellationRequested);
        }

        if (item.AfterCastWaitCombat)
        {
            float elapsedMs = AfterCastWaitCombat(2 * GCD, wait, bits, token);

            if (Log && item.Log)
                LogAfterCastWaitCombat(logger, item.Name, elapsedMs);

            static float AfterCastWaitCombat(int timeMs, Wait wait, AddonBits bits,
                CancellationToken token)
                => wait.Until(timeMs,
                () => bits.Combat() || token.IsCancellationRequested);
        }

        if (item.AfterCastWaitMeleeRange)
        {
            int lastKnownHealth = playerReader.HealthCurrent();
            int initialMinRange = playerReader.MinRange();

            float elapsedMs = AfterCastWaitMeleeRange(MAX_WAIT_MELEE_RANGE,
                lastKnownHealth, initialMinRange,
                wait, playerReader, token);

            if (Log && item.Log)
                LogAfterCastWaitMeleeRange(logger, item.Name, elapsedMs);

            static float AfterCastWaitMeleeRange(int duration,
                int lastKnownHealth, int initialMinRange,
                Wait wait, PlayerReader playerReader,
                CancellationToken token)
                => wait.Until(duration,
                () =>
                playerReader.MinRange() > initialMinRange ||
                playerReader.IsInMeleeRange() ||
                playerReader.IsTargetCasting() ||
                playerReader.HealthCurrent() < lastKnownHealth ||
                token.IsCancellationRequested);
        }

        if (item.AfterCastStepBack != 0 && !token.IsCancellationRequested)
        {
            int pressDurationMs =
                item.AfterCastStepBack != -1
                ? item.AfterCastStepBack
                : playerReader.GCD.Value != 0
                ? playerReader.GCD.Value
                : MIN_GCD - playerReader.SpellQueueTimeMs;

            if (Log && item.Log)
                LogAfterCastStepBack(logger, item.Name, pressDurationMs);

            input.StartBackward(true);

            if (Random.Shared.Next(3) == 0)
                input.PressJump();

            float elapsedMs = wait.Until(pressDurationMs, token);

            // todo: does this necessary ?
            if (Log && item.Log)
                LogAfterCastStepBackInterrupted(logger, item.Name, elapsedMs);

            input.StopBackward(false);
        }

        if (item.AfterCastWaitGCD)
        {
            if (Log && item.Log)
                LogAfterCastWaitGCD(logger, item.Name, playerReader.GCD.Value);

            wait.Until(playerReader.GCD.Value, token);
        }

        if (item.AfterCastDelay > 0)
        {
            int delay = Random.Shared.Next(item.AfterCastDelay, item.AfterCastMaxDelay);

            if (Log && item.Log)
                LogAfterCastDelay(logger, item.Name, delay);

            float elapsedMs = wait.Until(delay, token);
            if (Log && item.Log && elapsedMs >= 0)
            {
                LogAfterCastDelayInterrupted(logger, item.Name, elapsedMs);
            }
        }
    }

    public bool Cast(KeyAction item, Func<bool> interrupt)
    {
        CancellationToken token = CancellationToken.None;

        if (item.PressDuration > InputDuration.DefaultPress ||
            item.HasCastBar)
            token = interruptWatchdog.Set(interrupt);

        if (!PreparedForCast(item, token))
            return false;

        int auraHash = playerReader.AuraCount.Hash;

        Func<KeyAction, CancellationToken, CastResult> castStrategy =
            item.HasCastBar
            ? CastCastbar
            : CastInstant;

        CastResult result = castStrategy(item, token);
        if (result != CastResult.Success)
        {
            if (result != CastResult.UIError)
            {
                if (result == CastResult.TokenInterrupted &&
                    item.CancelOnInterrupt)
                {
                    input.PressESC();
                    WaitForGCD(item, false, true, CancellationToken.None);
                    wait.Fixed(playerReader.NetworkLatency);
                }

                if (logger.IsEnabled(LogLevel.Error))
                    LogFailedDueReason(logger, item.Name, result.ToStringF());
                return false;
            }

            // instant cast token interrupted?
            if (token.IsCancellationRequested)
            {
                return false;
            }

            LogFailedAttemptTryAgain(logger, item.Name);
            react.Do(item);

            if (token.IsCancellationRequested ||
                castStrategy(item, token) != CastResult.Success)
            {
                return false;
            }
        }

        if (!item.BaseAction)
        {
            int durationMs = UpdateGCD();

            if (DEBUG && Log && item.Log)
                LogWaitForGCD(logger, item.Name, playerReader.LastCastGCD,
                    playerReader.GCD.Value, playerReader.RemainCastMs, durationMs);
        }

        CheckPostCastFlags(item, auraHash, token);

        item.ConsumeCharge();

        return true;
    }

    public bool WaitForGCD(KeyAction item, bool spellQueue, bool ignoreRemainCast, CancellationToken token)
    {
        int duration = ignoreRemainCast
            ? playerReader.GCD.Value
            : Max(playerReader.GCD.Value, playerReader.RemainCastMs);

        duration += playerReader.NetworkLatency;

        if (spellQueue)
            duration -= playerReader.SpellQueueTimeMs;

        if (duration <= 0)
            return true;

        float elapsedMs = wait.Until(duration, token);

        if (DEBUG && Log && item.Log)
            LogGCD(logger, item.Name, usableAction.Is(item), duration, elapsedMs);

        return elapsedMs < 0;
    }

    private int UpdateGCD()
    {
        int durationMs =
            Max(playerReader.LastCastGCD, playerReader.GCD.Value)
            - playerReader.SpellQueueTimeMs;

        return durationMs;
    }

    public bool SwitchForm(KeyAction item, CancellationToken token)
    {
        return
            forms.Get(item.FormValue, out KeyAction? formAction)
            && CastInstant(formAction!, token) == CastResult.Success;
    }

    private void RepeatPetAttack()
    {
        if (classConfig.AutoPetAttack &&
            bits.Combat() &&
            bits.Pet() &&
            (!playerReader.PetTarget() ||
            playerReader.TargetGuid != playerReader.PetTargetGuid) &&
            !input.PetAttack.OnCooldown())
        {
            if (!playerReader.IsCasting() && !bits.Channeling())
                input.PressStopAttack();

            input.PressPetAttack();
        }
    }

    #region Logging

    [LoggerMessage(
        EventId = 0070,
        Level = LogLevel.Information,
        Message = "[{name,-17}] ... AfterCastWaitSwing")]
    static partial void LogAfterCastWaitSwing(ILogger logger, string name);

    [LoggerMessage(
        EventId = 0071,
        Level = LogLevel.Information,
        Message = "[{name,-17}] instant input {pressTime}ms")]
    static partial void LogInstantBaseAction(ILogger logger, string name, int pressTime);

    [LoggerMessage(
        EventId = 0072,
        Level = LogLevel.Information,
        Message = "[{name,-17}] instant input {pressTime}ms {castState} {inputElapsedMs}ms")]
    static partial void LogInstantInput(ILogger logger, string name, int pressTime, string castState, float inputElapsedMs);

    [LoggerMessage(
        EventId = 0073,
        Level = LogLevel.Warning,
        Message = "[{name,-17}] instant input {pressTime}ms {castState} {inputElapsedMs}ms")]
    static partial void LogInstantInputFailed(ILogger logger, string name, int pressTime, string castState, float inputElapsedMs);

    [LoggerMessage(
        EventId = 0074,
        Level = LogLevel.Information,
        Message = "[{name,-17}] " +
        "instant usable: {beforeUsable}->{afterUsable} | " +
        "current: {beforeCurrent}->{afterCurrent} | " +
        "{beforeCastEvent}->{afterCastEvent}")]
    static partial void LogInstantUsableChange(ILogger logger, string name,
        bool beforeUsable, bool afterUsable,
        bool beforeCurrent, bool afterCurrent,
        string beforeCastEvent, string afterCastEvent);

    [LoggerMessage(
        EventId = 0075,
        Level = LogLevel.Information,
        Message = "[{name,-17}] castbar input {pressTime}ms")]
    static partial void LogCastbarBaseAction(ILogger logger, string name, int pressTime);

    [LoggerMessage(
        EventId = 0076,
        Level = LogLevel.Information,
        Message = "[{name,-17}] castbar input {pressTime}ms {inputElapsedMs}ms")]
    static partial void LogCastbarInput(ILogger logger, string name, int pressTime, float inputElapsedMs);

    [LoggerMessage(
        EventId = 0077,
        Level = LogLevel.Information,
        Message = "[{name,-17}] ... count: {castCount} | casting: {casting} | channeling: {channeling} | usable: {beforeUsable}->{afterUsable} | {beforeCastEvent}->{afterCastEvent}")]
    static partial void LogCastbarUsableChange(ILogger logger, string name, bool casting, bool channeling, int castCount, bool beforeUsable, bool afterUsable, string beforeCastEvent, string afterCastEvent);

    [LoggerMessage(
        EventId = 0078,
        Level = LogLevel.Information,
        Message = "[{name,-17}] ... AfterCastWaitCastbar(V) {remain}ms or interrupt...")]
    static partial void LogVisibleAfterCastWaitCastbar(ILogger logger, string name, int remain);

    [LoggerMessage(
        EventId = 0079,
        Level = LogLevel.Warning,
        Message = "[{name,-17}] ... AfterCastWaitCastbar(v) interrupted!")]
    static partial void LogVisibleAfterCastWaitCastbarInterrupted(ILogger logger, string name);

    [LoggerMessage(
        EventId = 0080,
        Level = LogLevel.Information,
        Message = "[{name,-17}] ... AfterCastWaitCastbar(h) {remain}ms or interrupt...")]
    static partial void LogHiddenAfterCastWaitCastbar(ILogger logger, string name, int remain);

    [LoggerMessage(
        EventId = 0081,
        Level = LogLevel.Warning,
        Message = "[{name,-17}] ... AfterCastWaitCastbar(h) interrupted!")]
    static partial void LogHiddenAfterCastWaitCastbarInterrupted(ILogger logger, string name);


    [LoggerMessage(
        EventId = 0082,
        Level = LogLevel.Warning,
        Message = "[{name,-17}] ... after {before}->{after} form switch still not usable!")]
    static partial void LogAfterFormSwitchNotUsable(ILogger logger, string name, string before, string after);

    [LoggerMessage(
        EventId = 0083,
        Level = LogLevel.Information,
        Message = "[{name,-17}] ... BeforeCastDelay {delayBeforeCast}ms")]
    static partial void LogBeforeCastDelay(ILogger logger, string name, int delayBeforeCast);

    [LoggerMessage(
        EventId = 0084,
        Level = LogLevel.Information,
        Message = "[{name,-17}] ... AfterCastWaitBuff count: {auraCount} | miss: {missType} | {elapsedMs}ms")]
    static partial void LogAfterCastWaitBuff(ILogger logger, string name, string auraCount, string missType, float elapsedMs);

    [LoggerMessage(
        EventId = 0085,
        Level = LogLevel.Information,
        Message = "[{name,-17}] ... AfterCastWaitCombat {delayAfterCast}ms")]
    static partial void LogAfterCastWaitCombat(ILogger logger, string name, float delayAfterCast);

    [LoggerMessage(
        EventId = 0086,
        Level = LogLevel.Information,
        Message = "[{name,-17}] ... AfterCastDelay {delayAfterCast}ms")]
    static partial void LogAfterCastDelay(ILogger logger, string name, int delayAfterCast);

    [LoggerMessage(
        EventId = 0087,
        Level = LogLevel.Information,
        Message = "[{name,-17}] ... AfterCastDelay interrupted {delayElaspedMs}ms")]
    static partial void LogAfterCastDelayInterrupted(ILogger logger, string name, float delayElaspedMs);

    [LoggerMessage(
        EventId = 0088,
        Level = LogLevel.Information,
        Message = "[{name,-17}] ... AfterCastStepBack {stepBackAfterCast}ms")]
    static partial void LogAfterCastStepBack(ILogger logger, string name, int stepBackAfterCast);

    [LoggerMessage(
        EventId = 0089,
        Level = LogLevel.Information,
        Message = "[{name,-17}] .... AfterCastStepBack {stepbackElapsedMs}ms")]
    static partial void LogAfterCastStepBackInterrupted(ILogger logger, string name, float stepbackElapsedMs);

    [LoggerMessage(
        EventId = 0090,
        Level = LogLevel.Information,
        Message = "[{name,-17}] ... GCD usable: {usable} | remain: {remain}ms | {elapsedMs}ms")]
    static partial void LogGCD(ILogger logger, string name, bool usable, int remain, float elapsedMs);

    [LoggerMessage(
        EventId = 0091,
        Level = LogLevel.Information,
        Message = "[{name,-17}] ... form {before}->{after} | {elapsedMs}ms")]
    static partial void LogFormChanged(ILogger logger, string name, string before, string after, float elapsedMs);

    [LoggerMessage(
        EventId = 0092,
        Level = LogLevel.Information,
        Message = "[{name,-17}] ... AfterCastWaitBag {before}->{after} {elapsedMs}ms")]
    static partial void LogAfterCastWaitBag(ILogger logger, string name, int before, int after, float elapsedMs);

    [LoggerMessage(
        EventId = 0093,
        Level = LogLevel.Information,
        Message = "[{name,-17}] PrevGCD: {prevGCD}ms | GCD: {gcd}ms | Cast: {remainCastMs}ms | Next spell {duration}ms")]
    static partial void LogWaitForGCD(ILogger logger, string name, int prevGCD, int gcd, int remainCastMs, float duration);

    [LoggerMessage(
        EventId = 0094,
        Level = LogLevel.Information,
        Message = "[{name,-17}] ... AfterCastAuraExpected miss: {miss} {delay}ms")]
    static partial void LogAfterCastAuraExpected(ILogger logger, string name, string miss, int delay);

    [LoggerMessage(
        EventId = 0095,
        Level = LogLevel.Information,
        Message = "[{name,-17}] ... AfterCastWaitGCD {gcd}ms")]
    static partial void LogAfterCastWaitGCD(ILogger logger, string name, int gcd);

    [LoggerMessage(
        EventId = 0096,
        Level = LogLevel.Information,
        Message = "[{name,-17}] ... AfterCastWaitMeleeRange {elapsedMs}ms")]
    static partial void LogAfterCastWaitMeleeRange(ILogger logger, string name, float elapsedMs);

    [LoggerMessage(
        EventId = 0097,
        Level = LogLevel.Warning,
        Message = "[{name,-17}] ... Failed try again!")]
    static partial void LogFailedAttemptTryAgain(ILogger logger, string name);

    [LoggerMessage(
        EventId = 0098,
        Level = LogLevel.Error,
        Message = "[{name,-17}] ... Cast failed due {reason}!")]
    static partial void LogFailedDueReason(ILogger logger, string name, string reason);

    [LoggerMessage(
        EventId = 0099,
        Level = LogLevel.Information,
        Message = "[{name,-17}] ... BeforeCastFaceTarget {elapsedMs}ms")]
    static partial void LogBeforeCastFaceTarget(ILogger logger, string name, float elapsedMs);


    #endregion
}