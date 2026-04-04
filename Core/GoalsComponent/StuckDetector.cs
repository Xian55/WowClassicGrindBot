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

    private readonly ILogger<StuckDetector> logger;
    private readonly ConfigurableInput input;

    private readonly PlayerReader playerReader;
    private readonly AddonBits bits;
    private readonly PlayerDirection playerDirection;
    private readonly StopMoving stopMoving;

    private Vector3 worldTarget;

    private float prevDistance = MAX_RANGE;
    private long startTime;
    private long attemptTime;
    private int attemptCount;

    public double ActionDurationMs => GetElapsedTime(startTime).TotalMilliseconds;
    private double UnstuckMs => GetElapsedTime(attemptTime).TotalMilliseconds;

    public bool IsMoving => bits.Moving();

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
            Reset();
        }
    }

    public void Reset()
    {
        attemptTime = GetTimestamp();
        startTime = GetTimestamp();

        prevDistance = MAX_RANGE;
        attemptCount = 0;
    }

    public void Update(CancellationToken token = default)
    {
        if (bits.Falling())
            return;

        if (UnstuckMs < UNSTUCK_AFTER_MS)
        {
            if (!bits.Flying())
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

        // Aggressive: stop, turn, move forward, jump, reorient
        stopMoving.Stop();

        int turnDuration = Random.Shared.Next(150) + 100;
        LogUnstuckTurn(logger, attemptCount, turnDuration);
        input.TurnRandomDir(turnDuration, token);

        int moveDuration = Random.Shared.Next(500) + 500;
        input.PressFixed(input.ForwardKey, moveDuration, token);

        if (!bits.Flying())
            input.PressJump(token);

        Vector3 targetM = WorldMapAreaDB.ToMap_FlipXY(worldTarget, playerReader.WorldMapArea);
        float heading = DirectionCalculator.CalculateMapHeading(playerReader.MapPos, targetM);
        playerDirection.SetDirection(heading, targetM, PlayerDirection.DefaultIgnoreDistance, token);
    }

    public bool IsGettingCloser()
    {
        float distance = playerReader.WorldPos.WorldDistanceXYTo(worldTarget);
        if (distance < prevDistance - MIN_RANGE_DIFF)
        {
            Reset();
            prevDistance = distance;
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

    #endregion
}
