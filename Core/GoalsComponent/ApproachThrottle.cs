using System;

namespace Core.Goals;

/// <summary>
/// Interact-with-target moves the character to the target on a single press: the
/// client finishes the run on its own and keeps the player facing the target the
/// whole way. Re-pressing restarts that run, and a press landing in the final
/// yards carries a melee class straight past the mob. This narrows Approach down
/// to a keepalive: slow at a target holding still, at the base cadence for one
/// that moved and invalidated the run's heading. Every press goes through that
/// cadence - a caller that wants to bypass it wants the overshoot back.
/// </summary>
public sealed class ApproachThrottle(
    ConfigurableInput input, PlayerReader playerReader)
{
    /// <summary>
    /// A run at a stationary target keeps its heading, so re-aiming is wasted.
    /// Still a keepalive rather than zero: something other than the interact run
    /// can be carrying the player (the blind StartForward after
    /// ERR_AUTOFOLLOW_TOO_FAR, knockback), and that needs re-engaging.
    /// </summary>
    public const int STATIONARY_TARGET_REPEAT_MS = 1000;

    /// <summary>
    /// A moving target invalidates the run's heading - re-aim at the base cadence.
    /// </summary>
    public const int MOVING_TARGET_REPEAT_MS = CastingHandler.SPELL_QUEUE;

    /// <summary>
    /// Bearing drift since the last press that counts as "the mob moved". Closing
    /// straight on a fixed point leaves only convergence noise, so this is a
    /// deadband rather than an exact compare. ~0.10 rad is ~6 degrees.
    /// </summary>
    public const float MOVING_TARGET_DRIFT_RAD = 0.10f;

    private float directionAtPress;
    private int minRangeAtPress;

    /// <summary>
    /// Re-seeds the reference bearing. Call from OnEnter. A stale pair costs at
    /// most one extra press, never a decision that stays wrong.
    /// </summary>
    public void Reset()
    {
        directionAtPress = playerReader.Direction;
        minRangeAtPress = playerReader.MinRange();
    }

    /// <summary>
    /// Re-seeds, and lets the next press through without waiting out the keepalive.
    /// For a caller starting a fresh chase: the first press of an approach cannot be
    /// a re-press, so nothing is being restarted and nothing can be overshot - the
    /// goal only runs while out of combat range. Making that one wait leaves the
    /// player standing still through a stuck check, which then clears a target the
    /// bot never actually walked towards.
    /// </summary>
    public void ResetForNewChase()
    {
        input.Approach.ResetCooldown();
        Reset();
    }

    /// <summary>
    /// Whether to press Interact now. The cadence is the whole decision: bearing
    /// drift picks the rate, and nothing bypasses it.
    ///
    /// <para>Not-moving is deliberately not a reason to press immediately. A run that
    /// ended because the player arrived reads exactly like one that never started -
    /// both are "not moving" - so an immediate restart on that condition fires at the
    /// key cooldown, 400ms, right where the player is standing on the mob. That is the
    /// overshoot this class exists to stop. A stalled run instead waits out the
    /// keepalive below, which costs at most a second.</para>
    ///
    /// <para>Suppressing the press outright once inside melee is also wrong: it removed
    /// the only thing that recovers from ERR_BADATTACKFACING while auto-attacking,
    /// which is the state issue #827 describes - on top of the mob, in combat, never
    /// turning, dead. A mob that circles the player drifts the bearing and earns a
    /// prompt re-face; a mob standing still gets the slow keepalive.</para>
    /// </summary>
    public bool ShouldPress()
    {
        if (input.Approach.OnCooldown())
        {
            return false;
        }

        return input.Approach.SinceLastClickMs >
            (TargetMoved()
                ? MOVING_TARGET_REPEAT_MS
                : STATIONARY_TARGET_REPEAT_MS);
    }

    /// <param name="arrived">
    /// The reason to press is already satisfied - for <see cref="ApproachTargetGoal"/>,
    /// WithInCombatRange(), which is that goal's own effect. Callers with no arrival
    /// condition of their own - a pull that chases, a swing keepalive - want
    /// <see cref="ShouldPress()"/>.
    /// </param>
    public bool ShouldPress(bool arrived) => !arrived && ShouldPress();

    /// <summary>
    /// Call right after a press so the next comparison is against the bearing the
    /// run was actually started with - the press itself snaps the facing on target.
    /// </summary>
    public void OnPressed()
    {
        Reset();
    }

    /// <summary>
    /// The interact run keeps the player facing the target, so the player's own
    /// facing is a free bearing-to-target read. A mob that strafes drifts the
    /// bearing; a mob fleeing straight away leaves the bearing alone and grows
    /// the range instead.
    /// </summary>
    private bool TargetMoved()
    {
        float drift = MathF.Abs(
            SplineFollowerCore.WrapPi(playerReader.Direction - directionAtPress));

        return drift > MOVING_TARGET_DRIFT_RAD ||
            playerReader.MinRange() > minRangeAtPress;
    }
}
