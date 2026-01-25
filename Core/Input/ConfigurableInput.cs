using Game;

using Microsoft.Extensions.Logging;

using SharedLib;

using System;
using System.Threading;

namespace Core;

public sealed partial class ConfigurableInput
{
    private readonly ILogger<ConfigurableInput> logger;
    private readonly WowProcessInput input;
    private readonly ClassConfiguration classConfig;

    private readonly bool Log;

    public ConfigurableInput(ILogger<ConfigurableInput> logger,
        WowProcessInput input, ClassConfiguration classConfig)
    {
        this.logger = logger;
        this.input = input;
        this.classConfig = classConfig;
        Log = classConfig.Log;

        input.ForwardKey = classConfig.ForwardKey;
        input.BackwardKey = classConfig.BackwardKey;
        input.TurnLeftKey = classConfig.TurnLeftKey;
        input.TurnRightKey = classConfig.TurnRightKey;

        input.InteractMouseover = classConfig.InteractMouseOver.ConsoleKey;
        input.InteractMouseoverModifier = classConfig.InteractMouseOver.Modifier;
        input.InteractMouseoverPress = classConfig.InteractMouseOver.PressDuration;
    }

    public void Reset() => input.Reset();

    public void StartForward(bool forced)
    {
        input.SetKeyState(ForwardKey, true, forced);
    }

    public void StopForward(bool forced)
    {
        if (input.IsKeyDown(ForwardKey))
            input.SetKeyState(ForwardKey, false, forced);
    }

    public void StartBackward(bool forced)
    {
        input.SetKeyState(BackwardKey, true, forced);
    }

    public void StopBackward(bool forced)
    {
        if (input.IsKeyDown(BackwardKey))
            input.SetKeyState(BackwardKey, false, forced);
    }

    public void SetKeyState(ConsoleKey key, bool state, bool forced)
    {
        input.SetKeyState(key, state, forced);
    }

    public void TurnRandomDir(int milliseconds, CancellationToken token = default)
    {
        input.PressRandom(
            Random.Shared.Next(2) == 0
            ? input.TurnLeftKey
            : input.TurnRightKey, milliseconds, token);
    }

    public int PressRandom(KeyAction keyAction, CancellationToken token = default)
    {
        int elapsedMs;

        // Use modifier-aware pressing if the keyAction has a modifier
        if (keyAction.HasModifier)
        {
            elapsedMs = input.PressRandomWithModifier(keyAction.ConsoleKey, keyAction.Modifier, keyAction.PressDuration, token);
        }
        else
        {
            elapsedMs = input.PressRandom(keyAction.ConsoleKey, keyAction.PressDuration, token);
        }

        keyAction.SetClicked();

        if (Log && keyAction.Log)
        {
            if (keyAction.BaseAction)
                LogBaseActionPressRandom(logger, keyAction.Name, keyAction.ConsoleKey, keyAction.Modifier.ToPrefix(), elapsedMs);
            else
                LogKeyActionPressRandom(logger, keyAction.Name, keyAction.ConsoleKey, keyAction.Modifier.ToPrefix(), elapsedMs);
        }

        return elapsedMs;
    }

    public void PressFixed(ConsoleKey key, int milliseconds, CancellationToken token)
    {
        input.PressFixed(key, milliseconds, token);
    }

    public void PressRandom(ConsoleKey key, int milliseconds)
    {
        input.PressRandom(key, milliseconds);
    }

    public bool IsKeyDown(ConsoleKey key) => input.IsKeyDown(key);

    public void PressInteract(CancellationToken token = default) => PressRandom(Interact, token);

    public void PressFastInteract(CancellationToken token = default)
    {
        if (Interact.HasModifier)
            input.PressRandomWithModifier(Interact.ConsoleKey, Interact.Modifier, InputDuration.FastPress, token);
        else
            input.PressRandom(Interact.ConsoleKey, InputDuration.FastPress, token);
        Interact.SetClicked();
    }

    public void PressVeryFastInteract()
    {
        if (Interact.HasModifier)
            input.PressRandomWithModifier(Interact.ConsoleKey, Interact.Modifier, InputDuration.VeryFastPress);
        else
            input.PressRandom(Interact.ConsoleKey, InputDuration.VeryFastPress);
        Interact.SetClicked();
    }

    public void PressApproachOnCooldown()
    {
        if (Approach.OnCooldown())
        {
            return;
        }

        if (Approach.HasModifier)
            input.PressRandomWithModifier(Approach.ConsoleKey, Approach.Modifier, InputDuration.FastPress);
        else
            input.PressRandom(Approach.ConsoleKey, InputDuration.FastPress);
        Approach.SetClicked();
    }

    public bool PressedApproachOnCooldown()
    {
        if (Approach.OnCooldown())
        {
            return false;
        }

        if (Approach.HasModifier)
            input.PressRandomWithModifier(Approach.ConsoleKey, Approach.Modifier, InputDuration.FastPress);
        else
            input.PressRandom(Approach.ConsoleKey, InputDuration.FastPress);
        Approach.SetClicked();
        return true;
    }

    public void PressApproach(CancellationToken token = default) => PressRandom(Approach, token);

    public void PressLastTarget(CancellationToken token = default) => PressRandom(TargetLastTarget, token);

    /// <summary>
    /// Presses TargetLastTarget and waits for a target to appear.
    /// </summary>
    /// <returns>True if target appeared within timeout, false if timed out.</returns>
    public bool PressLastTargetAndWait(Wait wait, Func<bool> hasTarget, int timeoutMs = 300, CancellationToken token = default)
    {
        PressLastTarget(token);
        return wait.Until(timeoutMs, hasTarget) > 0;
    }

    public void PressFastLastTarget(CancellationToken token = default)
    {
        if (TargetLastTarget.HasModifier)
            input.PressRandomWithModifier(TargetLastTarget.ConsoleKey, TargetLastTarget.Modifier, InputDuration.FastPress, token);
        else
            input.PressRandom(TargetLastTarget.ConsoleKey, InputDuration.FastPress, token);
        TargetLastTarget.SetClicked();
    }

    /// <summary>
    /// Presses TargetLastTarget (fast) and waits for a target to appear.
    /// </summary>
    /// <returns>True if target appeared within timeout, false if timed out.</returns>
    public bool PressFastLastTargetAndWait(Wait wait, Func<bool> hasTarget, int timeoutMs = 300, CancellationToken token = default)
    {
        PressFastLastTarget(token);
        return wait.Until(timeoutMs, hasTarget) > 0;
    }

    public void PressStandUp(CancellationToken token = default) => PressRandom(StandUp, token);

    public void PressClearTarget(CancellationToken token = default) => PressRandom(ClearTarget, token);

    public void PressStopAttack(CancellationToken token = default) => PressRandom(StopAttack, token);

    public void PressNearestTarget(CancellationToken token = default) => PressRandom(TargetNearestTarget, token);

    public void PressTargetPet(CancellationToken token = default) => PressRandom(TargetPet, token);

    public void PressTargetOfTarget(CancellationToken token = default) => PressRandom(TargetTargetOfTarget, token);

    public void PressJump(CancellationToken token = default) => PressRandom(Jump, token);

    public void PressPetAttack(CancellationToken token = default) => PressRandom(PetAttack, token);

    public void PressMount(CancellationToken token = default) => PressRandom(Mount, token);

    public void PressDismount(CancellationToken token = default)
    {
        if (Mount.HasModifier)
            input.PressRandomWithModifier(Mount.ConsoleKey, Mount.Modifier, Mount.PressDuration, token);
        else
            input.PressRandom(Mount.ConsoleKey, Mount.PressDuration, token);
    }

    public void PressTargetFocus(CancellationToken token = default) => PressRandom(TargetFocus, token);

    public void PressFollowTarget(CancellationToken token = default) => PressRandom(FollowTarget, token);

    public void PressESC(CancellationToken token = default)
    {
        input.PressRandom(ConsoleKey.Escape, InputDuration.VeryFastPress, token);
    }

    #region Logging

    [LoggerMessage(
        EventId = 5000,
        Level = LogLevel.Trace,
        Message = @"[{name}] {modifierPrefix}{key} pressed {milliseconds}ms")]
    static partial void LogBaseActionPressRandom(ILogger logger, string name, ConsoleKey key, string modifierPrefix, int milliseconds);

    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Debug,
        Message = @"[{name}] {modifierPrefix}{key} pressed {milliseconds}ms")]
    static partial void LogKeyActionPressRandom(ILogger logger, string name, ConsoleKey key, string modifierPrefix, int milliseconds);

    #endregion
}
