using System;

namespace Core.Goals;

/// <summary>
/// Interact-with-target moves the character to the target on a single press: the
/// client finishes the run on its own and keeps the player facing the target the
/// whole way. Re-pressing restarts that run, and a press landing in the final
/// yards carries a melee class straight past the mob. This narrows Approach down
/// to the presses that do something - (re)starting a stalled run, and re-aiming
/// at a target that moved.
/// </summary>
public sealed class ApproachThrottle(
    ConfigurableInput input, PlayerReader playerReader, AddonBits bits)
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
    /// Whether to re-press Interact while already engaged and standing on the target,
    /// waiting out a swing.
    ///
    /// <para>There is no run left to restart at this range, so the overshoot the
    /// approach path guards against cannot happen - the press is here to re-face a mob
    /// that has moved around the player. Treating close melee range as "arrived" and
    /// suppressing it outright removed the only thing that recovers from
    /// ERR_BADATTACKFACING while auto-attacking, which is the state issue #827
    /// describes: on top of the mob, in combat, never turning, dead.</para>
    ///
    /// <para>Bearing drift does the deciding. A mob that circles the player drifts it and
    /// earns a prompt re-face; a mob standing still gets the slow keepalive.</para>
    /// </summary>
    public bool ShouldPressEngaged()
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
    /// The reason to press is already satisfied - for the approach goals,
    /// WithInCombatRange(). Callers already engaged with the target want
    /// <see cref="ShouldPressEngaged"/> instead.
    /// </param>
    public bool ShouldPress(bool arrived)
    {
        if (input.Approach.OnCooldown())
        {
            return false;
        }

        if (arrived)
        {
            return false;
        }

        // Nothing is carrying the player forward - (re)start the run.
        if (!bits.Moving())
        {
            return true;
        }

        return input.Approach.SinceLastClickMs >
            (TargetMoved()
                ? MOVING_TARGET_REPEAT_MS
                : STATIONARY_TARGET_REPEAT_MS);
    }

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
