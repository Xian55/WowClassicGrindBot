using System;
using System.Collections.Generic;
using System.Numerics;

using Core.Goals;

using Microsoft.Extensions.Logging;

using static System.MathF;

namespace CoreTests.SplineSim;

/// <summary>
/// Game-free validation of <see cref="SplineFollowerCore"/> against a simulated
/// kinematic player. The controller is pure (snapshot in, key states out), so
/// everything that matters about it - tracking, oscillation, the curvature
/// brake, estimator adaptation - is testable here without WoW.
///
/// The simulation is deliberately hostile: tick jitter 40-90ms, input latency
/// 20-40ms, fixed20 position noise. If a case passes here it survives worse
/// than the live game produces.
/// </summary>
internal static class SplineSim
{
    private const float RunSpeed = 7f;
    private const float MountedSpeed = 14f;

    public static int Run(ILogger logger)
    {
        int failures = 0;

        failures += Case(logger, "straight 200yd on foot",
            Straight(200f, 2.5f), RunSpeed, indoors: false, simOmega: PI,
            maxCrossTrack: 1.0f, maxTurnTransitionsPerSec: 1.0f, minForwardDuty: 0.99f);

        failures += Case(logger, "straight 200yd mounted",
            Straight(200f, 2.5f), MountedSpeed, indoors: false, simOmega: PI,
            maxCrossTrack: 1.0f, maxTurnTransitionsPerSec: 1.0f, minForwardDuty: 0.99f);

        failures += Case(logger, "90-degree corner",
            Corner(40f, 2.5f), RunSpeed, indoors: false, simOmega: PI,
            maxCrossTrack: 1.6f, maxTurnTransitionsPerSec: 3.0f, minForwardDuty: 0.90f);

        failures += Case(logger, "s-slalom r=8yd",
            Slalom(8f, 4, 2.0f), RunSpeed, indoors: false, simOmega: PI,
            maxCrossTrack: 2.0f, maxTurnTransitionsPerSec: 6.5f, minForwardDuty: 0.55f);

        failures += Case(logger, "spiral r=2.2yd x3 revolutions (tower)",
            Spiral(2.2f, 3, 0.8f), RunSpeed, indoors: true, simOmega: PI,
            maxCrossTrack: 1.6f, maxTurnTransitionsPerSec: 8.0f, minForwardDuty: 0.05f,
            maxForwardDuty: 0.98f); // must brake: 100% duty means the brake never fired

        failures += Case(logger, "doorway 1.5yd gap",
            Doorway(30f, 1.5f), RunSpeed, indoors: true, simOmega: PI,
            maxCrossTrack: 0.75f, maxTurnTransitionsPerSec: 4.0f, minForwardDuty: 0.80f);

        failures += Case(logger, "mounted gentle bend r=10yd (no brake)",
            Slalom(10f, 2, 3.0f), MountedSpeed, indoors: false, simOmega: PI,
            maxCrossTrack: 3.0f, maxTurnTransitionsPerSec: 6.5f, minForwardDuty: 0.85f);

        failures += Case(logger, "slow client turn rate 0.8pi (estimator adapts)",
            Slalom(6f, 3, 2.0f), RunSpeed, indoors: false, simOmega: 0.8f * PI,
            maxCrossTrack: 2.5f, maxTurnTransitionsPerSec: 5.0f, minForwardDuty: 0.40f);

        failures += Case(logger, "start displaced 3yd laterally (callback drift)",
            Straight(60f, 2.5f), RunSpeed, indoors: false, simOmega: PI,
            maxCrossTrack: 3.5f, maxTurnTransitionsPerSec: 5.0f, minForwardDuty: 0.90f,
            startOffset: new Vector2(0f, 3f));

        if (failures == 0)
        {
            logger.LogInformation("SplineSim: all follower cases passed");
        }

        return failures;
    }

    // --- Harness --------------------------------------------------------

    private static int Case(ILogger logger, string name, Vector3[] pathPoints,
        float maxSpeed, bool indoors, float simOmega,
        float maxCrossTrack, float maxTurnTransitionsPerSec, float minForwardDuty,
        float maxForwardDuty = 1.0f, Vector2 startOffset = default)
    {
        SplineFollowerCore follower = new(new SharedLib.SplineFollowerOptions());
        follower.SetPath(pathPoints, maxSpeed);

        // Deterministic per-case seed: reproducible failures.
        Random rng = new(name.GetHashCode(StringComparison.Ordinal));

        // Kinematic player state. Facing starts along the first segment.
        Vector2 pos = pathPoints[0].AsVector2() + startOffset;
        float facing = SplineFollowerCore.WorldHeading(
            pathPoints[0].AsVector2(), pathPoints[1].AsVector2());
        float v = 0f;

        // Applied (post-latency) input state, plus a latency queue.
        bool fwdApplied = true;
        int turnApplied = 0;
        Queue<(long applyAt, bool fwd, int turn)> pending = new();

        float pathLen = 0f;
        for (int i = 1; i < pathPoints.Length; i++)
        {
            pathLen += Vector2.Distance(pathPoints[i - 1].AsVector2(), pathPoints[i].AsVector2());
        }

        long budgetMs = (long)(pathLen / (0.15f * maxSpeed) * 1000f) + 20_000;
        float reached = Max(3f, maxSpeed * 0.75f);

        long now = 0;
        int ticks = 0;
        int turnTransitions = 0;
        int forwardTicks = 0;
        int prevTurn = 0;
        float worstCrossTrack = 0f;
        int lastStuckIndex = -1;
        bool stuckIndexRegressed = false;
        SplineStatus status = SplineStatus.Following;

        while (now < budgetMs)
        {
            float dtMs = 40f + (float)rng.NextDouble() * 50f;
            long tickStart = now;
            now += (long)dtMs;
            ticks++;

            // Physics substeps at 5ms so latency lands mid-tick.
            for (float t = 0f; t < dtMs; t += 5f)
            {
                long simTime = tickStart + (long)t;
                while (pending.Count > 0 && pending.Peek().applyAt <= simTime)
                {
                    (_, fwdApplied, turnApplied) = pending.Dequeue();
                }

                const float stepS = 5f / 1000f;
                float targetV = fwdApplied ? maxSpeed : 0f;
                v = targetV > v
                    ? Min(targetV, v + maxSpeed / 0.1f * stepS) // 0.1s ramp up
                    : targetV;                                  // instant stop

                facing += simOmega * turnApplied * stepS;
                facing = (facing % Tau + Tau) % Tau;

                pos += v * stepS * new Vector2(Cos(facing), Sin(facing));
            }

            // Sensor noise: fixed20-ish quantization.
            Vector2 sensedPos = pos + new Vector2(
                ((float)rng.NextDouble() - 0.5f) * 0.1f,
                ((float)rng.NextDouble() - 0.5f) * 0.1f);

            SplineSnapshot snap = new(sensedPos, 0f, facing, maxSpeed,
                indoors, Falling: false, Moving: v > 0.1f, now);

            SplineCommand cmd = follower.Tick(in snap, reached);
            status = cmd.Status;

            if (cmd.Status is SplineStatus.Completed or SplineStatus.OffPath)
            {
                break;
            }

            worstCrossTrack = Max(worstCrossTrack, cmd.OffPathDistance);
            if (cmd.Forward) forwardTicks++;

            int turn = cmd.Turn switch
            {
                TurnState.Left => 1,
                TurnState.Right => -1,
                _ => 0
            };

            if (turn != prevTurn) turnTransitions++;
            prevTurn = turn;

            if (cmd.StuckTargetIndex < lastStuckIndex) stuckIndexRegressed = true;
            lastStuckIndex = Math.Max(lastStuckIndex, cmd.StuckTargetIndex);

            // Input latency 20-40ms.
            pending.Enqueue((now + 20 + rng.Next(21), cmd.Forward, turn));
        }

        float seconds = now / 1000f;
        float transitionsPerSec = turnTransitions / Max(seconds, 1f);
        float forwardDuty = forwardTicks / (float)Max(ticks, 1);

        List<string> problems = [];
        if (status != SplineStatus.Completed) problems.Add($"status={status} (never completed, {seconds:F0}s)");
        if (worstCrossTrack > maxCrossTrack) problems.Add($"crossTrack {worstCrossTrack:F2} > {maxCrossTrack}");
        if (transitionsPerSec > maxTurnTransitionsPerSec) problems.Add($"turnTransitions/s {transitionsPerSec:F1} > {maxTurnTransitionsPerSec}");
        if (forwardDuty < minForwardDuty) problems.Add($"forwardDuty {forwardDuty:P0} < {minForwardDuty:P0}");
        if (forwardDuty > maxForwardDuty) problems.Add($"forwardDuty {forwardDuty:P0} > {maxForwardDuty:P0} (brake never engaged)");
        if (stuckIndexRegressed) problems.Add("StuckTargetIndex regressed");

        if (problems.Count == 0)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "SplineSim [{Name}]: OK in {Sec:F1}s | crossTrack {Cross:F2}yd | turns/s {Tps:F1} | fwd {Duty:P0}",
                    name, seconds, worstCrossTrack, transitionsPerSec, forwardDuty);
            }
            return 0;
        }

        logger.LogError("SplineSim [{Name}]: FAILED - {Problems}", name, string.Join("; ", problems));
        return 1;
    }

    // --- Path generators (dense polylines, world-space XY) --------------

    private static Vector3[] Straight(float length, float spacing)
    {
        int n = (int)(length / spacing) + 1;
        Vector3[] p = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            p[i] = new Vector3(i * spacing, 0f, 0f);
        }

        return p;
    }

    private static Vector3[] Corner(float legLength, float spacing)
    {
        List<Vector3> p = [];
        for (float d = 0f; d <= legLength; d += spacing)
        {
            p.Add(new Vector3(d, 0f, 0f));
        }

        for (float d = spacing; d <= legLength; d += spacing)
        {
            p.Add(new Vector3(legLength, d, 0f));
        }

        return [.. p];
    }

    private static Vector3[] Slalom(float radius, int arcs, float spacing)
    {
        List<Vector3> p = [];
        Vector2 pos = Vector2.Zero;
        float heading = 0f;

        for (float d = 0f; d <= 15f; d += spacing)
        {
            p.Add(new Vector3(d, 0f, 0f));
        }

        pos = new Vector2(15f, 0f);

        for (int a = 0; a < arcs; a++)
        {
            float sign = a % 2 == 0 ? 1f : -1f;
            float arcStep = spacing / radius;
            for (float swept = 0f; swept < PI / 2f; swept += arcStep)
            {
                heading += sign * arcStep;
                pos += spacing * new Vector2(Cos(heading), Sin(heading));
                p.Add(new Vector3(pos.X, pos.Y, 0f));
            }
        }

        for (float d = spacing; d <= 15f; d += spacing)
        {
            pos += spacing * new Vector2(Cos(heading), Sin(heading));
            p.Add(new Vector3(pos.X, pos.Y, 0f));
        }

        return [.. p];
    }

    private static Vector3[] Spiral(float radius, int revolutions, float spacing)
    {
        List<Vector3> p = [];

        // Lead-in so the follower is settled before the spiral starts.
        for (float d = 0f; d <= 10f; d += spacing)
        {
            p.Add(new Vector3(d - 10f - radius, -radius, 0f));
        }

        float arcStep = spacing / radius;
        float totalAngle = revolutions * Tau;
        float zPerRadian = 2f / Tau; // 2yd climb per revolution

        for (float a = -PI / 2f; a < totalAngle; a += arcStep)
        {
            p.Add(new Vector3(
                radius * Cos(a),
                radius * Sin(a),
                (a + PI / 2f) * zPerRadian));
        }

        return [.. p];
    }

    private static Vector3[] Doorway(float legLength, float gapWidth)
    {
        // Straight through a narrow doorway: geometrically a straight line;
        // the assertion is on cross-track staying inside the gap half-width.
        _ = gapWidth;
        return Straight(legLength, 1.0f);
    }

    private static Vector2 AsVector2(this Vector3 v) => new(v.X, v.Y);
}
