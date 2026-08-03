using Microsoft.Extensions.Logging;

using System;

using static System.MathF;

namespace Core.Goals;

/// <summary>
/// Re-acquires whatever is currently attacking the player once the previous
/// target is gone or dead.
///
/// <para>
/// Lifted out of <see cref="CombatGoal"/> so <see cref="FindThreatGoal"/> can
/// reach it. The recovery only ever ran while CombatGoal was the active goal,
/// which requires a live target - so the exact state that needs it most
/// (in combat, target dead, still being hit) could not reach it.
/// </para>
/// </summary>
public sealed class ThreatFinder
{
    private readonly ILogger<ThreatFinder> logger;
    private readonly ConfigurableInput input;
    private readonly Wait wait;
    private readonly PlayerReader playerReader;
    private readonly AddonBits bits;
    private readonly CombatLog combatLog;

    public ThreatFinder(ILogger<ThreatFinder> logger,
        ConfigurableInput input, Wait wait,
        PlayerReader playerReader, AddonBits bits,
        CombatLog combatLog)
    {
        this.logger = logger;
        this.input = input;
        this.wait = wait;
        this.playerReader = playerReader;
        this.bits = bits;
        this.combatLog = combatLog;
    }

    /// <param name="resetOnNewTarget">
    /// The combat key sequence whose <see cref="KeyAction.ResetOnNewTarget"/>
    /// entries are cleared once a new target is acquired, so per-target debuffs
    /// are not considered still on cooldown against a different mob.
    /// </param>
    public void FindPossibleThreats(ReadOnlySpan<KeyAction> resetOnNewTarget)
    {
        if (bits.Pet_Defensive())
        {
            float elapsedPetFoundTarget = wait.Until(CastingHandler.GCD,
                () => playerReader.PetTarget() && bits.PetTarget_Alive());

            if (elapsedPetFoundTarget < 0)
            {
                logger.LogWarning("Pet not found target!");
                input.PressClearTarget();
                return;
            }

            ResetCooldowns(resetOnNewTarget);

            input.PressTargetPet();
            wait.Update();
            input.PressTargetOfTarget();
            wait.Update();

            logger.LogWarning("Found new target by pet. {ElapsedMs}ms", elapsedPetFoundTarget);

            return;
        }

        // FindThreatGoal can own many consecutive ticks, unlike CombatGoal which
        // only reached this path on the tick its target died. Respect the key's
        // own cooldown so it cannot turn into Tab spam.
        if (input.TargetNearestTarget.OnCooldown())
        {
            wait.Update();
            return;
        }

        logger.LogInformation("Checking target in front...");
        input.PressNearestTarget();
        wait.Update();

        if (bits.Target() && !bits.Target_Dead() && bits.Target_Hostile())
        {
            if (!bits.Target_Combat())
            {
                logger.LogWarning("Dont pull non-hostile target!");
                input.PressClearTarget();
                wait.Update();
                return;
            }

            if (bits.TargetTarget_PlayerOrPet() || combatLog.DamageTaken.Contains(playerReader.TargetGuid))
            {
                ResetCooldowns(resetOnNewTarget);

                logger.LogWarning("Found new target!");
                wait.Update();
                return;
            }
        }

        logger.LogWarning("Possible threats {DamageTakenCount}!", combatLog.DamageTakenCount());

        if (bits.SoftInteract_Enabled())
        {
            UnstuckDeadSoftTargetLock();
        }
    }

    private static void ResetCooldowns(ReadOnlySpan<KeyAction> span)
    {
        for (int i = 0; i < span.Length; i++)
        {
            KeyAction keyAction = span[i];
            if (keyAction.ResetOnNewTarget)
            {
                keyAction.ResetCooldown();
                keyAction.ResetCharges();
            }
        }
    }

    public void UnstuckDeadSoftTargetLock()
    {
        if (!bits.SoftInteract() ||
            !bits.SoftInteract_Dead() ||
            !bits.Auto_Attack() ||
            combatLog.LastDamageDoneTime.ElapsedMs() < playerReader.MainHandSpeedMs() * 2 ||
            combatLog.DamageTakenCount() == 0)
        {
            return;
        }

        logger.LogWarning("Turn away from dead softTarget due locking current target interaction!");

        float startDirection = playerReader.Direction;
        float totalRotation = 0f;

        ConsoleKey turnKey = Random.Shared.Next(2) == 0
            ? input.TurnLeftKey
            : input.TurnRightKey;

        input.SetKeyState(turnKey, true, false);

        while (bits.SoftInteract() && bits.SoftInteract_Dead())
        {
            wait.Update();

            float currentDirection = playerReader.Direction;
            float delta = Abs(currentDirection - startDirection);
            if (delta > PI)
            {
                delta = Tau - delta;
            }

            totalRotation = delta;

            // Safety: if we've turned nearly 360°, soft target is everywhere - strafe instead
            if (totalRotation >= Tau - 0.2f)
            {
                input.SetKeyState(turnKey, false, false);
                logger.LogWarning("Full rotation without clearing soft target - strafe!");

                KeyAction strafeAction = Random.Shared.Next(2) == 0
                    ? input.StrafeLeft
                    : input.StrafeRight;

                input.PressFixed(strafeAction.ConsoleKey, 500, default);
                wait.Update();

                return;
            }
        }

        input.SetKeyState(turnKey, false, false);
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Cleared dead soft target after {TurnDegrees:F0} degree turn", totalRotation * 180f / PI);
        }
    }
}
