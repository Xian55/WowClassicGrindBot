using Core.Goals;

using Microsoft.Extensions.Logging;

using SharedLib;
using SharedLib.Extensions;

using System;
using System.Numerics;
using System.Threading;

using static System.Diagnostics.Stopwatch;

namespace Core;

public sealed partial class StuckDetector
{
    private const float MIN_RANGE_DIFF = 1f;
    private const float MAX_RANGE = 999999;
    private const double UNSTUCK_AFTER_MS = 2000;
    private const double ACTION_STUCK_TIME = 3000;

    /// <summary>
    /// How long the target may go without getting any closer than it has ever been before
    /// the destination is written off as unreachable.
    ///
    /// <para>Sized against the ladder, not picked round: attempts are
    /// <see cref="UNSTUCK_AFTER_MS"/> apart and the last rung is reached on attempt 6, so a
    /// full pass takes ~12s and this allows roughly one and a half. Long enough that every
    /// recovery has been tried and retried, short enough that the character is not visibly
    /// jumping and sidestepping on the spot for a minute - which is what a passer-by
    /// notices and reports.</para>
    /// </summary>
    private const double GIVE_UP_AFTER_MS = 20_000;

    private readonly ILogger<StuckDetector> logger;
    private readonly ConfigurableInput input;

    private readonly PlayerReader playerReader;
    private readonly AddonBits bits;
    private readonly PlayerDirection playerDirection;
    private readonly StopMoving stopMoving;

    private Vector3 worldTarget;

    private long startTime;
    private long attemptTime;
    private int attemptCount;

    // Closest the target has ever been on this attempt, and when that happened. A
    // last-reading comparison cannot tell a yard of shuffling apart from covering ground;
    // a best-so-far one can.
    private float bestDistance = MAX_RANGE;
    private long bestTime;

    public double ActionDurationMs => GetElapsedTime(startTime).TotalMilliseconds;
    private double UnstuckMs => GetElapsedTime(attemptTime).TotalMilliseconds;

    public bool IsMoving => bits.Moving();

    /// <summary>
    /// The target has not been approached in <see cref="GIVE_UP_AFTER_MS"/> despite the
    /// unstuck attempts. The caller owns what to do about it - pick another destination,
    /// abandon the goal - but it must do something, or it will retry forever.
    /// </summary>
    public bool IsUnreachable =>
        bestDistance < MAX_RANGE &&
        GetElapsedTime(bestTime).TotalMilliseconds > GIVE_UP_AFTER_MS;

    public StuckDetector(ILogger<StuckDetector> logger, ConfigurableInput input,
        AddonBits bits, PlayerReader playerReader, PlayerDirection playerDirection,
        StopMoving stopMoving)
    {
        this.logger = logger;
        this.input = input;

        this.bits = bits;
        this.playerReader = playerReader;
        this.playerDirection = playerDirection;
        this.stopMoving = stopMoving;

        Reset();
    }

    public void SetTargetLocation(Vector3 worldTarget)
    {
        if (this.worldTarget != worldTarget)
        {
            this.worldTarget = worldTarget;

            // A different destination is a genuinely fresh attempt, so the unreachable
            // verdict goes with it.
            Reset();

            bestDistance = MAX_RANGE;
            bestTime = GetTimestamp();
        }
    }

    /// <summary>
    /// Clears the escalation state.
    ///
    /// <para>Deliberately leaves the unreachable clock alone: every caller means "start this
    /// attempt over" - a nudge worked, or the route is being recalculated - and none of them
    /// means the destination changed. Clearing the clock here is what let a hopeless
    /// approach retry indefinitely, because <see cref="Navigation"/> recalculates the route
    /// every few seconds while stuck.</para>
    /// </summary>
    public void Reset()
    {
        attemptTime = GetTimestamp();
        startTime = GetTimestamp();

        attemptCount = 0;
    }

    public void Update(CancellationToken token = default)
    {
        if (bits.Falling())
            return;

        if (UnstuckMs < UNSTUCK_AFTER_MS)
        {
            // Rate limit, because this branch cannot be trusted to be entered
            // slowly. A spline advances its lookahead every tick and re-seeds
            // SetTargetLocation with it, which Resets attemptTime - so UnstuckMs
            // restarts before it ever reaches UNSTUCK_AFTER_MS. The ladder then
            // never escalates past attempt 0, never logs, and this jump fires on
            // every single frame: the character pogo-jumps in place and the
            // spacebar looks stuck down.
            if (!bits.Flying() && !input.Jump.OnCooldown())
                input.PressJump(token);

            return;
        }

        attemptCount++;
        attemptTime = GetTimestamp();

        if (attemptCount == 1)
        {
            LogUnstuckJump(logger, attemptCount);

            if (!bits.Flying())
                input.PressJump(token);

            return;
        }

        if (attemptCount == 2)
        {
            LogUnstuckNudge(logger, attemptCount);

            if (!bits.Flying())
                input.PressJump(token);

            int nudgeDuration = Random.Shared.Next(200) + 200;
            input.PressFixed(input.ForwardKey, nudgeDuration, token);

            return;
        }

        if (attemptCount == 3)
        {
            // Aggressive: stop, turn, move forward, jump, reorient
            stopMoving.Stop();

            int turnDuration = Random.Shared.Next(150) + 100;
            LogUnstuckTurn(logger, attemptCount, turnDuration);
            input.TurnRandomDir(turnDuration, token);

            int moveDuration = Random.Shared.Next(500) + 500;
            input.PressFixed(input.ForwardKey, moveDuration, token);

            if (!bits.Flying())
                input.PressJump(token);

            FaceTarget(token);
            return;
        }

        // Everything above pushes forward, which is useless once the character is jammed
        // against something - it just holds it there. From here on the goal is to put the
        // body somewhere genuinely different, so the next path request starts from a spot
        // that is not against the obstacle. Reported recovery from a real stuck on Azuremyst
        // was exactly this: back up, then sidestep.
        int stage = (attemptCount - 4) % 3;

        switch (stage)
        {
            case 0:
                StrafeOut(token);
                break;

            case 1:
                BackOutAndAround(token);
                break;

            default:
                BackOutAndAround(token, longer: true);
                break;
        }

        FaceTarget(token);
    }

    /// <summary>
    /// Slides sideways without turning. A doorway, fence post or tree the character is
    /// pressed against is often cleared by a yard of lateral movement, which forward-only
    /// nudging can never produce - pushing forward just holds it against the obstacle.
    /// </summary>
    private void StrafeOut(CancellationToken token)
    {
        bool left = Random.Shared.Next(2) == 0;
        ConsoleKey strafeKey = left
            ? input.StrafeLeft.ConsoleKey
            : input.StrafeRight.ConsoleKey;

        if (strafeKey == default)
        {
            LogNoStrafeBinding(logger);
            return;
        }

        stopMoving.Stop();

        int duration = Random.Shared.Next(400) + 600;
        LogUnstuckStrafe(logger, attemptCount, left ? "left" : "right", duration);

        input.PressFixed(strafeKey, duration, token);

        if (!bits.Flying())
            input.PressJump(token);
    }

    /// <summary>
    /// Reverses out and leaves at an angle. Backing straight up tends to return to the same
    /// spot, so the retreat is combined with a strafe - the character ends up off the line
    /// it was jammed on, which is what lets the next path take a different approach.
    /// </summary>
    private void BackOutAndAround(CancellationToken token, bool longer = false)
    {
        stopMoving.Stop();

        int backDuration = longer
            ? Random.Shared.Next(700) + 900
            : Random.Shared.Next(400) + 500;

        LogUnstuckBackOut(logger, attemptCount, backDuration);

        input.PressFixed(input.BackwardKey, backDuration, token);

        bool left = Random.Shared.Next(2) == 0;
        ConsoleKey strafeKey = left
            ? input.StrafeLeft.ConsoleKey
            : input.StrafeRight.ConsoleKey;

        if (strafeKey != default)
        {
            input.PressFixed(strafeKey, Random.Shared.Next(300) + 400, token);
        }
        else
        {
            // No strafe binding - a wide turn is the next best way off the old line.
            input.TurnRandomDir(Random.Shared.Next(300) + 400, token);
        }

        if (!bits.Flying())
            input.PressJump(token);
    }

    private void FaceTarget(CancellationToken token)
    {
        Vector3 targetM = WorldMapAreaDB.ToMap_FlipXY(worldTarget, playerReader.WorldMapArea);
        float heading = DirectionCalculator.CalculateMapHeading(playerReader.MapPos, targetM);
        playerDirection.SetDirection(heading, targetM, PlayerDirection.DefaultIgnoreDistance, token);
    }

    public bool IsGettingCloser()
    {
        float distance = playerReader.WorldPos.WorldDistanceXYTo(worldTarget);

        // Only a new closest approach counts as progress, and only it clears the ladder.
        //
        // This used to compare against the previous reading, which meant the yard or two an
        // unstuck nudge produces looked like progress and reset the attempt counter. The
        // ladder then restarted at attempt 1 every time and never reached anything stronger
        // than a jump - 42 minutes of it in one observed session. Measuring against the best
        // approach so far makes shuffling in place read as what it is.
        if (distance < bestDistance - MIN_RANGE_DIFF)
        {
            bestDistance = distance;
            bestTime = GetTimestamp();

            Reset();
            return true;
        }

        // Grace period only if client confirms movement
        if (ActionDurationMs < ACTION_STUCK_TIME && bits.Moving())
            return true;

        return false;
    }

    #region Logging

    [LoggerMessage(
        EventId = 0050,
        Level = LogLevel.Information,
        Message = "Unstuck attempt {Attempt}: jump")]
    static partial void LogUnstuckJump(ILogger logger, int attempt);

    [LoggerMessage(
        EventId = 0051,
        Level = LogLevel.Information,
        Message = "Unstuck attempt {Attempt}: jump + nudge forward")]
    static partial void LogUnstuckNudge(ILogger logger, int attempt);

    [LoggerMessage(
        EventId = 0052,
        Level = LogLevel.Information,
        Message = "Unstuck attempt {Attempt}: turning {TurnDuration}ms")]
    static partial void LogUnstuckTurn(ILogger logger, int attempt, int turnDuration);

    [LoggerMessage(
        EventId = 0053,
        Level = LogLevel.Information,
        Message = "Unstuck attempt {Attempt}: strafe {Direction} {Duration}ms")]
    static partial void LogUnstuckStrafe(ILogger logger, int attempt, string direction, int duration);

    [LoggerMessage(
        EventId = 0054,
        Level = LogLevel.Information,
        Message = "Unstuck attempt {Attempt}: back out {Duration}ms + sidestep")]
    static partial void LogUnstuckBackOut(ILogger logger, int attempt, int duration);

    [LoggerMessage(
        EventId = 0055,
        Level = LogLevel.Warning,
        Message = "No strafe binding available - unstuck cannot sidestep. Bind StrafeLeft/StrafeRight.")]
    static partial void LogNoStrafeBinding(ILogger logger);

    #endregion
}
