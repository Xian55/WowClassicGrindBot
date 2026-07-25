using System;
using System.Numerics;

using SharedLib;
using SharedLib.Extensions;

using static System.MathF;

namespace Core.Goals;

/// <summary>One frame of sensor data handed to the follower.</summary>
public readonly record struct SplineSnapshot(
    Vector2 PosXY,
    float PosZ,
    float Facing,
    float RunSpeed,
    bool Indoors,
    bool Falling,
    bool Moving,
    long TimestampMs);

public enum SplineStatus
{
    Inactive,
    Following,
    Completed,
    OffPath
}

public enum TurnState
{
    None,
    Left,
    Right
}

/// <summary>One frame of desired input state coming back out.</summary>
public readonly record struct SplineCommand(
    SplineStatus Status,
    bool Forward,
    TurnState Turn,
    int ConsumedPoints,
    float OffPathDistance,
    int StuckTargetIndex,
    bool Braking,
    bool Slow);

/// <summary>
/// Closed-loop follower for the dense navmesh spline, built for keyboard
/// movement. The legacy follower is a waypoint-popper: aim at the next point,
/// pop it several yards early, dead-reckon turns with blocking timed key
/// presses. That works on sparse spot-A* breadcrumbs and falls apart on a
/// spline threading a tower staircase.
///
/// This one never pops by proximity and never blocks. Progress is the player's
/// monotone projection onto the path; steering is a lookahead point a fixed
/// travel-time ahead on the curve; turning is a key HELD as state and released
/// when the measured facing closes on the target - every tick re-decides all
/// key states from fresh sensor data, so the caller's input resolution is
/// actually used instead of being slept through.
///
/// It also knows the physics the legacy follower ignores: keyboard turn rate
/// is about pi rad/s, so at speed v the tightest trackable turn radius is
/// v/omega (~2.2yd on foot, ~4.5yd mounted). When the path ahead bends faster
/// than that, no steering exists that follows it - the only correct move is to
/// drop forward, pivot, and re-engage. That is the curvature brake, and it is
/// what climbs a spiral staircase.
///
/// Pure by design: no game or input dependencies, all state in this class,
/// sensor data in via <see cref="SplineSnapshot"/>, key states out via
/// <see cref="SplineCommand"/>. The whole controller is unit-testable against
/// a simulated kinematic player.
/// </summary>
public sealed class SplineFollowerCore
{
    // Turn rate the WoW client applies while a turn key is held. Used only to
    // seed the online estimate.
    public const float DefaultTurnRate = PI;

    private const float EmaSpeed = 0.3f;
    private const float EmaOmega = 0.2f;
    private const float EmaDt = 0.2f;

    // --- Estimator tuning ---
    /// <summary>Seed for the per-tick interval estimate, ms.</summary>
    private const float DefaultTickMs = 50f;
    /// <summary>Fallback ground run speed when the sensor reports none, yd/s.</summary>
    private const float DefaultRunSpeed = 7f;
    /// <summary>Minimum measurable tick interval to trust an estimate, ms.</summary>
    private const float MinTickMs = 1f;
    /// <summary>Speed above this multiple of run speed is a teleport / loading screen.</summary>
    private const float TeleportSpeedFactor = 1.5f;
    /// <summary>Minimum turn rate to trust an omega sample, rad/s.</summary>
    private const float MinObservableOmega = 0.3f;
    /// <summary>Turn-rate estimate is clamped to this band around the default.</summary>
    private const float OmegaClampMinFactor = 0.5f;
    private const float OmegaClampMaxFactor = 1.5f;

    // --- Projection window ---
    /// <summary>Minimum monotone-projection search window, yards.</summary>
    private const float ProjectionWindowMin = 6f;
    /// <summary>Spacing multiplier for the projection window.</summary>
    private const float ProjectionWindowSpacingFactor = 2.5f;
    /// <summary>Enlarged projection window on arming, for callback-latency drift, yards.</summary>
    private const float ProjectionWindowArmMin = 10f;

    // --- Stuck target / completion ---
    /// <summary>Look-ahead distance for the stuck-detector target index, yards.</summary>
    private const float StuckTargetLookaheadYards = 5f;
    /// <summary>Spacing floor guarding divisions by measured segment length, yards.</summary>
    private const float MinSpacing = 0.5f;
    /// <summary>Minimum points ahead for the stuck target index.</summary>
    private const int StuckTargetMinAhead = 2;
    /// <summary>Spacing multiplier for the near-end arc threshold.</summary>
    private const float NearEndArcSpacingFactor = 2f;
    /// <summary>Arc-length completion epsilon, yards.</summary>
    private const float ArcCompletionEpsilon = 0.25f;
    /// <summary>Orbit-guard band as a multiple of the reached distance.</summary>
    private const float OrbitGuardDistanceFactor = 2f;
    /// <summary>Consecutive rising-distance ticks that declare an orbit stall.</summary>
    private const int OrbitGuardRisingTicks = 3;

    // --- Off-path / brake / steering ---
    /// <summary>Spacing multiplier for the outdoor off-path limit.</summary>
    private const float OffPathSpacingFactor = 1.6f;
    /// <summary>Curvature-brake look-ahead window bounds, yards.</summary>
    private const float BrakeWindowMin = 3f;
    private const float BrakeWindowMax = 12f;
    /// <summary>Speed floor guarding the required-omega division, yd/s.</summary>
    private const float MinSpeedFloor = 0.5f;
    /// <summary>Deadband multiple that keeps the brake engaged (hysteresis).</summary>
    private const float BrakeHoldDeadbandFactor = 1.5f;
    /// <summary>Heading error that forces the brake on (legacy 60-degree stop-before-turn).</summary>
    private const float BrakeEngageAngle = PI / 3f;

    /// <summary>
    /// A single vertex sharper than this is a hairpin/switchback. The steering
    /// target is clamped to it (arc-length targeting would otherwise land on the
    /// return leg and aim across the wall between the legs) and curvature past it
    /// is excluded from the brake, so the bot drives to the vertex and pivots
    /// there instead of dead-stopping short. Continuous curvature (spirals) has
    /// no such vertex, so their behaviour is unchanged.
    /// </summary>
    private const float BendClampAngle = 2f * PI / 3f;

    /// <summary>
    /// Distance before a sharp vertex to request walk speed, yards. Arriving at
    /// the pivot at run speed overshoots into the wall; walking the last stretch
    /// lets the brake stop in time.
    /// </summary>
    private const float SlowApproachYards = 5f;
    /// <summary>Predictive turn release looks ahead half a tick, so this divides the tick.</summary>
    private const float PredictiveReleaseHalfTick = 2f;

    // --- Median segment length ---
    /// <summary>Max segments sampled for the median (must match the stackalloc size).</summary>
    private const int MedianSampleCount = 32;
    /// <summary>Floor on the median segment length, yards.</summary>
    private const float MinSegmentLength = 0.25f;

    private readonly SplineFollowerOptions settings;

    public SplineFollowerCore(SplineFollowerOptions settings)
    {
        this.settings = settings;
    }

    private Vector3[] path = Array.Empty<Vector3>();

    /// <summary>cum[i] = XY arc length from path[0] to path[i].</summary>
    private float[] cum = Array.Empty<float>();

    private float spacing;
    private int segIdx;
    private float s;
    private int consumedIdx;

    private float vEst;
    private float omegaEst = DefaultTurnRate;
    private float dtEstMs = DefaultTickMs;

    private Vector2 lastPos;
    private float lastFacing;
    private long lastTs;
    private bool hasLast;
    private TurnState heldTurn;
    private TurnState heldTurnPrevInterval;

    private bool forward = true;
    private long forwardChangedTs;
    private bool braking;

    private float lastEndDistance = float.MaxValue;
    private int endDistanceRisingTicks;

    public bool HasPath => path.Length >= 2;

    public Vector3 PointAt(int index) => path[Math.Clamp(index, 0, path.Length - 1)];

    /// <summary>
    /// Arms the follower with a fresh world-space path. The path is consumed
    /// as-is - in particular it must NOT have been simplified, the density is
    /// the entire point.
    /// </summary>
    public void SetPath(Vector3[] worldPoints, float runSpeed)
    {
        if (worldPoints.Length < 2)
        {
            Clear();
            return;
        }

        path = worldPoints;

        if (cum.Length < worldPoints.Length)
        {
            cum = new float[worldPoints.Length];
        }

        cum[0] = 0f;
        for (int i = 1; i < worldPoints.Length; i++)
        {
            cum[i] = cum[i - 1] + worldPoints[i - 1].WorldDistanceXYTo(worldPoints[i]);
        }

        // Median segment length: every spacing-derived threshold scales from
        // the real density rather than assuming the local CatmullRom default -
        // the V3 server produces its own.
        spacing = MedianSegmentLength(worldPoints);

        segIdx = 0;
        s = 0f;
        consumedIdx = 0;
        hasLast = false;
        heldTurn = TurnState.None;
        heldTurnPrevInterval = TurnState.None;
        forward = true;
        braking = false;
        lastEndDistance = float.MaxValue;
        endDistanceRisingTicks = 0;

        // Reseed: a stale estimate from minutes ago (different mount state)
        // would mis-size the lookahead for the first seconds.
        vEst = runSpeed > 0f ? runSpeed : DefaultRunSpeed;
    }

    public void Clear()
    {
        path = Array.Empty<Vector3>();
        heldTurn = TurnState.None;
        hasLast = false;
    }

    public SplineCommand Tick(in SplineSnapshot snap, float reachedDistance)
    {
        if (!HasPath)
        {
            return new SplineCommand(SplineStatus.Inactive, false, TurnState.None, 0, 0f, 0, false, false);
        }

        int pathEnd = path.Length - 1;

        // --- 1. Estimators -------------------------------------------------
        if (hasLast)
        {
            float dtMs = snap.TimestampMs - lastTs;
            if (dtMs > MinTickMs)
            {
                dtEstMs += EmaDt * (dtMs - dtEstMs);

                if (!snap.Falling)
                {
                    float dt = dtMs / 1000f;
                    float vMeas = Vector2.Distance(snap.PosXY, lastPos) / dt;

                    // Teleport / loading-screen rejection.
                    if (vMeas < Max(snap.RunSpeed, DefaultRunSpeed) * TeleportSpeedFactor)
                    {
                        vEst += EmaSpeed * (vMeas - vEst);
                    }

                    // Turn rate is only observable while a key was held for the
                    // whole interval - key flips happen at tick edges, so the
                    // previous tick's state covers this interval.
                    if (heldTurnPrevInterval != TurnState.None)
                    {
                        float w = Abs(WrapPi(snap.Facing - lastFacing)) / dt;
                        if (w is > MinObservableOmega and < Tau)
                        {
                            omegaEst += EmaOmega * (w - omegaEst);
                            omegaEst = Math.Clamp(omegaEst,
                                OmegaClampMinFactor * DefaultTurnRate, OmegaClampMaxFactor * DefaultTurnRate);
                        }
                    }
                }
            }
        }

        lastPos = snap.PosXY;
        lastFacing = snap.Facing;
        lastTs = snap.TimestampMs;
        hasLast = true;

        // --- 2. Monotone local projection ---------------------------------
        // Never a global search: a spiral staircase overlaps itself in XY
        // across floors, and a global closest-point would teleport progress
        // between them. The window looks one segment back (jitter) and a few
        // spacings ahead.
        float window = Max(ProjectionWindowMin, ProjectionWindowSpacingFactor * spacing);
        if (s == 0f)
        {
            window = Max(window, ProjectionWindowArmMin); // callback-latency drift on arming
        }

        // Do not project across a hairpin. On a switchback the return leg sits
        // within `window` arc ahead AND physically close (a thin wall between the
        // legs), so the closest-point search would snap s onto it and consume the
        // approach + vertex early - the follower then thinks it is past the turn
        // and walks into the wall. Cap the search at the vertex so s only advances
        // to the tip; once physically there the next tick's bend is behind and the
        // return leg becomes reachable.
        float projBend = ArcOfNextSharpBend(s, Max(window, BrakeWindowMax));
        float projLimit = s + Min(window, Max(0f, projBend - s));

        float bestDistSq = float.MaxValue;
        float bestS = s;
        int bestSeg = segIdx;

        for (int i = Math.Max(0, segIdx - 1); i < pathEnd && cum[i] <= projLimit; i++)
        {
            Vector2 a = path[i].AsVector2();
            Vector2 b = path[i + 1].AsVector2();
            Vector2 p = VectorExt.GetClosestPointOnLineSegment(a, b, snap.PosXY);

            float dSq = Vector2.DistanceSquared(snap.PosXY, p);
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                bestSeg = i;
                bestS = cum[i] + Vector2.Distance(a, p);
            }
        }

        if (bestS > s)
        {
            s = bestS;
            segIdx = bestSeg;
        }

        float offPath = Sqrt(bestDistSq);

        int consumed = 0;
        while (consumedIdx < pathEnd && cum[consumedIdx + 1] <= s)
        {
            consumedIdx++;
            consumed++;
        }

        int stuckTargetIndex = Math.Min(
            consumedIdx + Math.Max(StuckTargetMinAhead,
                (int)Ceiling(StuckTargetLookaheadYards / Max(spacing, MinSpacing))),
            pathEnd);

        // --- 3. Completion -------------------------------------------------
        // XY proximity alone is NOT arrival: a tower's end point sits directly
        // above its entrance, well inside the arrival radius in XY, before a
        // single stair is climbed. Arrival needs arc progress near the end too.
        // On ordinary paths the two coincide, so goal-facing behaviour matches
        // the legacy proximity pop exactly.
        float endDistance = Vector2.Distance(snap.PosXY, path[pathEnd].AsVector2());
        bool nearEndByArc = s >= cum[pathEnd] - Max(reachedDistance, NearEndArcSpacingFactor * spacing);

        if ((endDistance < reachedDistance && nearEndByArc) || s >= cum[pathEnd] - ArcCompletionEpsilon)
        {
            return new SplineCommand(SplineStatus.Completed, true, TurnState.None, consumed, offPath, stuckTargetIndex, false, false);
        }

        // Orbit guard: near the end but the distance keeps growing means the
        // arrival disc is inside our turning circle - a proximity pop would
        // have counted this as reached, so do the same rather than circling.
        if (nearEndByArc && endDistance < OrbitGuardDistanceFactor * reachedDistance)
        {
            endDistanceRisingTicks = endDistance > lastEndDistance ? endDistanceRisingTicks + 1 : 0;
            if (endDistanceRisingTicks >= OrbitGuardRisingTicks)
            {
                return new SplineCommand(SplineStatus.Completed, true, TurnState.None, consumed, offPath, stuckTargetIndex, false, false);
            }
        }
        else
        {
            endDistanceRisingTicks = 0;
        }

        lastEndDistance = endDistance;

        // --- 4. Off-path ---------------------------------------------------
        if (!snap.Falling)
        {
            float offPathLimit = snap.Indoors
                ? settings.OffPathIndoor
                : Max(settings.OffPathOutdoor, OffPathSpacingFactor * spacing);

            if (offPath > offPathLimit)
            {
                heldTurn = TurnState.None;
                return new SplineCommand(SplineStatus.OffPath, false, TurnState.None, consumed, offPath, stuckTargetIndex, false, false);
            }
        }

        // --- 5. Steering target & error ------------------------------------
        float tau = snap.Indoors ? settings.TauSteerIndoor : settings.TauSteerOutdoor;
        float minLook = snap.Indoors ? settings.LookAheadMinIndoor : settings.LookAheadMinOutdoor;
        float maxLook = snap.Indoors ? settings.LookAheadMaxIndoor : settings.LookAheadMaxOutdoor;
        // Max floored to Min so a misconfig (Max < Min) can't throw from Clamp.
        float lookahead = Math.Clamp(vEst * tau, minLook, Max(minLook, maxLook));

        // Don't let the target or the brake look past a hairpin vertex (see
        // BendClampAngle): aiming across it steers into the wall between the legs,
        // and braking for it drops forward yards short. Clamp both to the vertex
        // so the bot reaches it, then pivots (error jumps ~180 the moment the
        // vertex is passed, which the brake below catches).
        float bendArc = ArcOfNextSharpBend(s, Max(lookahead, BrakeWindowMax));

        Vector2 target = PointAtArc(Min(s + lookahead, bendArc));
        float heading = WorldHeading(snap.PosXY, target);
        float error = WrapPi(heading - snap.Facing);

        // --- 6. Curvature brake (the generalized stop-and-turn) ------------
        // Total heading change the path demands over the next brake window,
        // versus what the measured turn rate can deliver at the current speed.
        // Over the limit means no steering can follow the path - stop forward,
        // pivot, re-engage. Self-stabilizing on spirals: braking drops vEst,
        // which shrinks the demand until it passes again.
        float brakeWindow = Math.Clamp(vEst * settings.BrakeWindowSeconds, BrakeWindowMin, BrakeWindowMax);
        float brakeWindowToBend = Min(brakeWindow, Max(0f, bendArc - s));
        float headingDemand = Abs(error) + HeadingChangeOver(s, brakeWindowToBend);
        float requiredOmega = headingDemand / (brakeWindow / Max(vEst, MinSpeedFloor));

        braking = braking
            ? requiredOmega > omegaEst * settings.BrakeRelease
              || Abs(error) > BrakeHoldDeadbandFactor * settings.DeadbandOn
            : requiredOmega > omegaEst * settings.BrakeSafety
              || Abs(error) > BrakeEngageAngle; // parity with the legacy 60-degree stop-before-turn

        // Walk-speed approach into a hairpin: a sharp vertex (BendClampAngle) is
        // close ahead but we are not braking yet. Walking the last stretch lets
        // the brake at the vertex stop in time instead of overshooting into the
        // wall. This only requests the state; Navigation drives the walk toggle.
        bool bendAhead = bendArc < s + Max(lookahead, BrakeWindowMax);
        bool slow = bendAhead && !braking && bendArc - s <= SlowApproachYards;

        bool wantForward = !braking;
        if (wantForward != forward
            && snap.TimestampMs - forwardChangedTs > settings.ForwardMinHoldMs)
        {
            forward = wantForward;
            forwardChangedTs = snap.TimestampMs;
        }

        // --- 7. Bang-bang steering with hysteresis + predictive release ----
        // The turn key is a held STATE. At pi rad/s a 70ms tick sweeps ~12.6
        // degrees - more than the deadband - so releasing on the deadband alone
        // limit-cycles. Predictive release lets go when the remaining angle
        // will be crossed within about half a tick plus input latency, which
        // bounds the residual error to roughly the deadband.
        float absError = Abs(error);
        TurnState desired = error > 0f ? TurnState.Left : TurnState.Right;

        if (heldTurn != TurnState.None)
        {
            if (desired != heldTurn)
            {
                heldTurn = absError > settings.DeadbandOn ? desired : TurnState.None;
            }
            else if (absError < settings.DeadbandOff
                || absError / omegaEst * 1000f < (dtEstMs / PredictiveReleaseHalfTick) + settings.InputLatencyMs)
            {
                heldTurn = TurnState.None;
            }
        }
        else if (absError > settings.DeadbandOn)
        {
            heldTurn = desired;
        }

        heldTurnPrevInterval = heldTurn;

        return new SplineCommand(SplineStatus.Following, forward, heldTurn, consumed, offPath, stuckTargetIndex, braking, slow);
    }

    /// <summary>XY point at the given arc length, clamped to the path end.</summary>
    private Vector2 PointAtArc(float arc)
    {
        int end = path.Length - 1;
        if (arc >= cum[end])
        {
            return path[end].AsVector2();
        }

        int i = segIdx;
        while (i < end - 1 && cum[i + 1] < arc)
        {
            i++;
        }

        float segLen = cum[i + 1] - cum[i];
        float t = segLen > 0f ? (arc - cum[i]) / segLen : 0f;
        return Vector2.Lerp(path[i].AsVector2(), path[i + 1].AsVector2(), t);
    }

    /// <summary>Sum of |heading changes| between segments within the window past s.</summary>
    private float HeadingChangeOver(float from, float window)
    {
        int end = path.Length - 1;
        float total = 0f;
        float prevHeading = float.NaN;

        for (int i = segIdx; i < end && cum[i] <= from + window; i++)
        {
            Vector2 a = path[i].AsVector2();
            Vector2 b = path[i + 1].AsVector2();
            if (a == b)
            {
                continue;
            }

            float h = Atan2(b.Y - a.Y, b.X - a.X);
            if (!float.IsNaN(prevHeading))
            {
                total += Abs(WrapPi(h - prevHeading));
            }

            prevHeading = h;
        }

        return total;
    }

    /// <summary>
    /// Arc length of the first vertex past <paramref name="from"/> whose
    /// single-segment heading change exceeds <see cref="BendClampAngle"/>, or
    /// <paramref name="from"/> + <paramref name="maxArc"/> if none. Keeps the
    /// steering target and brake window from crossing a hairpin.
    /// </summary>
    private float ArcOfNextSharpBend(float from, float maxArc)
    {
        int end = path.Length - 1;
        float limit = from + maxArc;
        float prevHeading = float.NaN;

        for (int i = segIdx; i < end && cum[i] <= limit; i++)
        {
            Vector2 a = path[i].AsVector2();
            Vector2 b = path[i + 1].AsVector2();
            if (a == b)
            {
                continue;
            }

            float h = Atan2(b.Y - a.Y, b.X - a.X);
            if (!float.IsNaN(prevHeading)
                && cum[i] > from
                && Abs(WrapPi(h - prevHeading)) > BendClampAngle)
            {
                return cum[i];
            }

            prevHeading = h;
        }

        return limit;
    }

    /// <summary>
    /// World-space heading, WoW convention: 0 = north = +X, counter-clockwise,
    /// west = +Y. Map-space headings are distorted by the zone's aspect ratio
    /// (up to ~11 degrees on non-square zones), which a deadband controller
    /// cannot absorb the way the legacy constant-correction follower does.
    /// </summary>
    public static float WorldHeading(Vector2 fromXY, Vector2 toXY)
    {
        float heading = Atan2(toXY.Y - fromXY.Y, toXY.X - fromXY.X);
        return heading < 0f ? heading + Tau : heading;
    }

    /// <summary>Wraps an angle to (-pi, pi].</summary>
    public static float WrapPi(float angle)
    {
        angle %= Tau;
        return angle switch
        {
            > PI => angle - Tau,
            <= -PI => angle + Tau,
            _ => angle
        };
    }

    private static float MedianSegmentLength(Vector3[] points)
    {
        // Sampling stride keeps this allocation-free and O(1) extra space for
        // long paths; the median only steers thresholds, exactness is not
        // needed.
        int count = points.Length - 1;
        int stride = Math.Max(1, count / MedianSampleCount);

        Span<float> sample = stackalloc float[MedianSampleCount];
        int n = 0;
        for (int i = 0; i < count && n < sample.Length; i += stride)
        {
            sample[n++] = points[i].WorldDistanceXYTo(points[i + 1]);
        }

        Span<float> used = sample[..n];
        used.Sort();
        return Max(used[n / 2], MinSegmentLength);
    }
}
