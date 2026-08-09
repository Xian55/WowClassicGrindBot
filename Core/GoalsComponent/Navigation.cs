using Core.Database;
using Core.GOAP;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SharedLib;
using SharedLib.Data;
using SharedLib.Extensions;

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

using static System.MathF;

#pragma warning disable 162

namespace Core.Goals;

public sealed partial class Navigation : IDisposable
{
    private const bool debug = false;

    private const float DIFF_THRESHOLD = 1.5f;   // within 50% difference
    private const float UNIFORM_DIST_DIV = 2;    // within 50% difference

    private readonly string patherName;

    private readonly ILogger<Navigation> logger;
    private readonly PlayerDirection playerDirection;
    private readonly ConfigurableInput input;
    private readonly PlayerReader playerReader;
    private readonly AddonBits bits;
    private readonly StopMoving stopMoving;
    private readonly StuckDetector stuckDetector;
    private readonly IPPather pather;
    private readonly IMountHandler mountHandler;
    private readonly AreaDB areaDB;

    private const float MinDistanceMount = 10;
    private readonly float MaxDistance = 200;
    private readonly float IndoorMinDistance = 1f;
    private readonly float OutDoorMinDistance = 3f;

    private const float OUTDOOR_LOOK_AHEAD = 0.75f;    // seconds
    private const float INDOOR_LOOK_AHEAD = 0.25f;     // seconds
    private const float SIMPLIFY_LOOK_AHEAD = 0.25f;   // seconds

    private float AvgDistance;

    /// <summary>
    /// The destination has resisted every unstuck attempt for long enough that the caller
    /// should stop trying to reach it. See <see cref="StuckDetector.IsUnreachable"/>.
    /// </summary>
    public bool IsUnreachable => stuckDetector.IsUnreachable;

    /// <summary>
    /// The waypoints are hunting anchors spread over an area, not a traced path, so every
    /// leg between them must be pathfound. Set by the caller that supplies such a route -
    /// see the note in <see cref="SetWayPoints"/>.
    /// </summary>
    public bool SparseWaypoints { get; set; }
    private float lastWorldDistance = float.MaxValue;

    private const float minAngleToTurn = PI / 35f;              // 5.14 degree
    private const float minAngleToStopBeforeTurn = PI / 3f;     // 60 degree

    private readonly Stack<Vector3> wayPoints = new();
    private readonly Stack<Vector3> routeToNextWaypoint = new();

    public Vector3[] TotalRoute { private set; get; } = Array.Empty<Vector3>();

    public DateTime LastActive { get; private set; }

    public event Action? OnPathCalculated;
    public event Action? OnWayPointReached;
    public event Action? OnDestinationReached;
    public event Action? OnAnyPointReached;
    public event Action? OnNoPathFound;

    public bool SimplifyRouteToWaypoint { get; set; } = true;

    // Simplification thins the route (drops near/collinear points); it suits the
    // sparse SpotAStar output but guts the navmesh's dense funnel path, so the
    // follower cuts corners. Never simplify when the pather already returns
    // smoothed (navmesh) paths - keep them dense regardless of the flag.
    private bool ShouldSimplify => SimplifyRouteToWaypoint && !pather.PathsAreSmoothed;

    private bool active;
    private Vector3 playerWorldPos;

    private readonly Queue<PathRequest> pathRequests = new(1);
    private readonly Queue<PathResult> pathResults = new(1);

    private readonly CancellationToken token;
    private readonly Thread pathfinderThread;
    private readonly ManualResetEventSlim manualReset;

    private int failedAttempt;
    private Vector3 lastFailedDestination;

    // Closed-loop follower for dense navmesh splines. Engaged only when the
    // pather emits smoothed paths AND the master env switch is on; the legacy
    // waypoint-pop follower below is bypassed wholesale, never modified, so
    // turning the switch off restores todays behaviour exactly.
    private readonly SplineFollowerCore spline;
    private readonly SplineFollowerOptions splineSettings;
    private readonly ConsoleKey turnLeftKey;
    private readonly ConsoleKey turnRightKey;
    private TurnState appliedTurn;
    private long brakeSinceMs;

    // True while the character is toggled into walk (slow) mode for a hairpin
    // approach. ReleaseTurnKeys (every exit/stop) toggles it back to run, so the
    // bot can never be left stuck walking.
    private bool walking;

    // Logs which follower is in use once, on the first navigation tick.
    private bool splineLogged;
    private bool warnedNoWalkKey;

    /// <summary>
    /// How long a deliberate stop-and-turn may hold the character stationary
    /// before the stuck ladder is allowed to intervene. A worst-case 180 degree
    /// pivot takes ~1000ms at the client turn rate; the margin covers brake
    /// decay and tick jitter.
    /// </summary>
    private const long PivotGraceMs = 2500;

    /// <summary>Stuck duration that clears the route and dismounts, ms.</summary>
    private const double StuckClearRouteMs = 10_000;

    private bool SplineActive =>
        splineSettings.Enabled && pather.PathsAreSmoothed && spline.HasPath;

    public Navigation(ILogger<Navigation> logger,
        CancellationTokenSource<GoapAgent> cts,
        PlayerDirection playerDirection,
        ConfigurableInput input,
        PlayerReader playerReader, AddonBits bits,
        StopMoving stopMoving,
        StuckDetector stuckDetector, IPPather pather, IMountHandler mountHandler,
        ClassConfiguration classConfiguration,
        AreaDB areaDB,
        IOptions<SplineFollowerOptions> splineOptions)
    {
        this.logger = logger;
        this.playerDirection = playerDirection;
        this.input = input;
        this.playerReader = playerReader;
        this.bits = bits;
        this.stopMoving = stopMoving;
        this.stuckDetector = stuckDetector;
        this.pather = pather;
        this.mountHandler = mountHandler;
        this.areaDB = areaDB;

        splineSettings = splineOptions.Value;
        spline = new SplineFollowerCore(splineSettings);

        patherName = pather.GetType().Name;

        turnLeftKey = classConfiguration.TurnLeftKey;
        turnRightKey = classConfiguration.TurnRightKey;

        AvgDistance = OutDoorMinDistance;
        token = cts.Token;
        manualReset = new(false);
        pathfinderThread = new(PathFinderThread);
        pathfinderThread.Start();

        switch (classConfiguration.Mode)
        {
            case Mode.AutoGather:
            case Mode.AttendedGather:
                MaxDistance = OutDoorMinDistance;
                SimplifyRouteToWaypoint = false;
                break;
        }
    }

    public void Dispose()
    {
        ReleaseTurnKeys();
        manualReset.Set();
    }

    public void Update()
    {
        Update(token);
    }

    public void Update(CancellationToken token)
    {
        active = true;

        if (wayPoints.Count == 0 && routeToNextWaypoint.Count == 0)
        {
            OnDestinationReached?.Invoke();
            return;
        }

        while (pathResults.TryDequeue(out PathResult result))
        {
            result.Callback(result);
        }

        if (token.IsCancellationRequested || pathRequests.Count > 0)
        {
            return;
        }

        if (routeToNextWaypoint.Count == 0)
        {
            RefillRouteToNextWaypoint(token);
            return;
        }

        LastActive = DateTime.UtcNow;

        if (!splineLogged)
        {
            splineLogged = true;
            LogFollowerSelected(logger,
                (splineSettings.Enabled && pather.PathsAreSmoothed) ? "SPLINE (WASD)" : "LEGACY waypoint",
                splineSettings.Enabled, pather.PathsAreSmoothed, input.CanWalk);
        }

        if (SplineActive)
        {
            SplineUpdate(token);
            return;
        }

        input.StartForward(true);

        // main loop
        Vector3 playerW = playerReader.WorldPos;
        playerWorldPos = playerW;
        Vector3 targetW = routeToNextWaypoint.Peek();
        float worldDistance = playerW.WorldDistanceXYTo(targetW);

        Vector3 playerM = WorldMapAreaDB.ToMap_FlipXY(playerW, playerReader.WorldMapArea);
        Vector3 targetM = WorldMapAreaDB.ToMap_FlipXY(targetW, playerReader.WorldMapArea);
        float heading = DirectionCalculator.CalculateMapHeading(playerM, targetM);

        if (worldDistance < ReachedDistance(OutDoorMinDistance))
        {
            if (targetW.Z != 0 && targetW.Z != playerW.Z)
            {
                playerReader.WorldPosZ = targetW.Z;
            }

            if (ShouldSimplify)
                ReduceByDistance(playerW, OutDoorMinDistance);
            else
                routeToNextWaypoint.Pop();

            OnAnyPointReached?.Invoke();

            lastWorldDistance = float.MaxValue;
            UpdateTotalRoute();

            if (routeToNextWaypoint.Count == 0)
            {
                if (wayPoints.Count > 0)
                {
                    wayPoints.Pop();
                    UpdateTotalRoute();

                    if (debug)
                        LogDebug($"Reached wayPoint! Distance: {worldDistance} -- Remains: {wayPoints.Count}");

                    OnWayPointReached?.Invoke();
                }
            }
            else
            {
                if (!routeToNextWaypoint.TryPeek(out targetW))
                    return;
                stuckDetector.SetTargetLocation(targetW);

                playerM = WorldMapAreaDB.ToMap_FlipXY(playerW, playerReader.WorldMapArea);
                targetM = WorldMapAreaDB.ToMap_FlipXY(targetW, playerReader.WorldMapArea);
                heading = DirectionCalculator.CalculateMapHeading(playerM, targetM);

                AdjustHeading(heading, token);

                return;
            }
        }

        if (routeToNextWaypoint.Count > 0)
        {
            if (stuckDetector.IsGettingCloser())
            {
                AdjustHeading(heading, token);
            }
            else
            {
                if (stuckDetector.ActionDurationMs > StuckClearRouteMs)
                {
                    if (mountHandler.IsMounted())
                        mountHandler.Dismount();

                    LogClearRouteToWaypointStuck(logger, stuckDetector.ActionDurationMs);
                    stuckDetector.Reset();
                    routeToNextWaypoint.Clear();
                    return;
                }

                if (HasBeenActiveRecently())
                {
                    stuckDetector.Update(token);
                    // Stop/Reset can clear the route on another thread between the
                    // Count check above and here - TryPeek instead of crashing.
                    if (routeToNextWaypoint.TryPeek(out Vector3 nextW))
                        worldDistance = playerW.WorldDistanceXYTo(nextW);
                }
            }
        }

        lastWorldDistance = worldDistance;
    }

    /// <summary>
    /// Per-tick application of the spline follower. Non-blocking by
    /// construction: every input is a key STATE decided from this frame's
    /// sensors - no timed presses, no thread sleeps - so the tick rate of the
    /// addon feed is fully used. Event timing mirrors the legacy loop point
    /// for point so goals cannot tell the followers apart.
    /// </summary>
    private void SplineUpdate(CancellationToken token)
    {
        Vector3 playerW = playerReader.WorldPos;
        playerWorldPos = playerW;

        SplineSnapshot snap = new(
            playerW.AsVector2(), playerW.Z,
            playerReader.Direction, playerReader.RunSpeed,
            bits.Indoors(), bits.Falling(), bits.Moving(),
            Environment.TickCount64);

        SplineCommand cmd = spline.Tick(in snap, ReachedDistance(OutDoorMinDistance));

        if (cmd.ConsumedPoints > 0)
        {
            // Pop-sync: the route stack tracks follower progress so TotalRoute,
            // the frontend overlay and goal-side distance math see exactly what
            // the legacy popper would have shown.
            Vector3 lastConsumed = default;
            for (int i = 0; i < cmd.ConsumedPoints && routeToNextWaypoint.Count > 1; i++)
            {
                lastConsumed = routeToNextWaypoint.Pop();
            }

            if (lastConsumed.Z != 0 && lastConsumed.Z != playerW.Z)
            {
                playerReader.WorldPosZ = lastConsumed.Z;
            }

            OnAnyPointReached?.Invoke();
            UpdateTotalRoute();
            stuckDetector.SetTargetLocation(spline.PointAt(cmd.StuckTargetIndex));

            LogSplineConsumed(logger, cmd.ConsumedPoints, routeToNextWaypoint.Count);
        }

        switch (cmd.Status)
        {
            case SplineStatus.Completed:
            {
                Vector3 finalPoint = spline.PointAt(int.MaxValue);
                if (finalPoint.Z != 0 && finalPoint.Z != playerW.Z)
                {
                    playerReader.WorldPosZ = finalPoint.Z;
                }

                spline.Clear();
                ReleaseTurnKeys();
                routeToNextWaypoint.Clear();

                if (wayPoints.Count > 0)
                {
                    wayPoints.Pop();
                    UpdateTotalRoute();
                    OnWayPointReached?.Invoke();
                }

                LogSplineCompleted(logger, wayPoints.Count);
                return;
            }
            case SplineStatus.OffPath:
            {
                LogSplineOffPath(logger, cmd.OffPathDistance);
                spline.Clear();
                ReleaseTurnKeys();
                routeToNextWaypoint.Clear();
                return; // next tick: refill -> stopMoving -> fresh path request
            }
        }

        // A deliberate stop-and-turn looks exactly like being stuck to the
        // detector: stationary, no XY progress, and IsGettingCloser's grace
        // requires bits.Moving(). Without this window the ladder fires within
        // a tick of the brake engaging, releases the turn keys mid-pivot, and
        // the pivot can never complete - a jump-in-place deadlock at every
        // tight corner.
        long nowMs = Environment.TickCount64;
        if (cmd.Braking)
        {
            if (brakeSinceMs == 0)
            {
                brakeSinceMs = nowMs;
                LogSplineBrake(logger, true, cmd.OffPathDistance);
            }
        }
        else
        {
            if (brakeSinceMs != 0)
                LogSplineBrake(logger, false, cmd.OffPathDistance);
            brakeSinceMs = 0;
        }

        bool pivotGrace = cmd.Braking && nowMs - brakeSinceMs < PivotGraceMs;

        if (!pivotGrace && !stuckDetector.IsGettingCloser())
        {
            if (stuckDetector.ActionDurationMs > StuckClearRouteMs)
            {
                if (mountHandler.IsMounted())
                    mountHandler.Dismount();

                LogClearRouteToWaypointStuck(logger, stuckDetector.ActionDurationMs);
                stuckDetector.Reset();
                spline.Clear();
                ReleaseTurnKeys();
                routeToNextWaypoint.Clear();
                return;
            }

            if (HasBeenActiveRecently())
            {
                // The unstick ladder does blocking presses on the same keys -
                // hand them over cleanly, then KEEP FORWARD ENGAGED, exactly
                // like the legacy loop which holds forward at the top of every
                // tick. Returning without any movement input here deadlocks:
                // no motion means Moving stays false, so the ladder repeats
                // while the character pogo-jumps in place.
                ReleaseTurnKeys();
                stuckDetector.Update(token);
                input.StartForward(true);
                return;
            }
        }

        // Walk-speed hairpin approach: match the follower's Slow request. Every
        // exit path resets this via ReleaseTurnKeys, so it cannot stick on.
        bool wantWalk = cmd.Slow && input.CanWalk;
        if (wantWalk != walking)
        {
            input.ToggleWalk(token);
            walking = wantWalk;
            LogSplineWalkToggle(logger, walking);
        }
        else if (cmd.Slow && !input.CanWalk && !warnedNoWalkKey)
        {
            // Surface once: the follower asked to slow into a hairpin but there
            // is no walk key to honour it. Explains run-speed hairpin approaches.
            warnedNoWalkKey = true;
            LogSplineNoWalkKey(logger);
        }

        if (cmd.Forward)
            input.StartForward(true);
        else
            input.StopForward(true);

        ApplyTurn(cmd.Turn);
    }

    private void ApplyTurn(TurnState desired)
    {
        if (desired == appliedTurn)
        {
            return;
        }

        if (appliedTurn == TurnState.Left)
            input.SetKeyState(turnLeftKey, false, true);
        else if (appliedTurn == TurnState.Right)
            input.SetKeyState(turnRightKey, false, true);

        if (desired == TurnState.Left)
            input.SetKeyState(turnLeftKey, true, true);
        else if (desired == TurnState.Right)
            input.SetKeyState(turnRightKey, true, true);

        appliedTurn = desired;
    }

    private void ReleaseTurnKeys()
    {
        if (input.IsKeyDown(turnLeftKey))
            input.SetKeyState(turnLeftKey, false, true);

        if (input.IsKeyDown(turnRightKey))
            input.SetKeyState(turnRightKey, false, true);

        appliedTurn = TurnState.None;

        // Guaranteed walk-mode reset: every exit/stop path funnels through here,
        // so the bot is never left toggled into walk.
        if (walking)
        {
            input.ToggleWalk();
            walking = false;
        }
    }

    public void Resume()
    {
        ResetStuckParameters();

        if (!pather.PathsAreSmoothed && routeToNextWaypoint.Count > 0)
        {
            V1_AttemptToKeepRouteToWaypoint();
        }

        int removed = 0;
        while (AdjustNextWaypointPointToClosest() && removed < 5) { removed++; };
        if (removed > 0)
        {
            UpdateTotalRoute();

            if (debug)
                LogDebug($"Resume: removed {removed} waypoint!");
        }
    }

    public void Stop()
    {
        active = false;

        if (pather.PathsAreSmoothed)
            routeToNextWaypoint.Clear();

        // Every goal interrupt (combat pull, abort) lands here. The follower
        // holds turn keys as state, so without this a pull mid-turn would
        // leave the character spinning.
        spline.Clear();
        ReleaseTurnKeys();

        ResetStuckParameters();
    }

    public void StopMovement()
    {
        input.StopForward(true);
    }

    public bool HasWaypoint()
    {
        return wayPoints.Count != 0;
    }

    public bool HasNext()
    {
        return routeToNextWaypoint.Count != 0;
    }

    public Vector3 NextMapPoint()
    {
        return WorldMapAreaDB.ToMap_FlipXY(routeToNextWaypoint.Peek(), playerReader.WorldMapArea);
    }

    public void SetWayPoints(Span<Vector3> points)
    {
        wayPoints.Clear();
        routeToNextWaypoint.Clear();
        spline.Clear();
        ReleaseTurnKeys();

        float mapDistanceXY = 0;
        WorldMapArea wma = playerReader.WorldMapArea;
        for (int i = points.Length - 1; i >= 0; i--)
        {
            Vector3 point = points[i];
            if (IsMapPoint(point))
            {
                point = WorldMapAreaDB.ToWorld_FlipXY(point, wma);
            }

            if (i != points.Length - 1)
            {
                Vector3 prev = wayPoints.Peek();
                mapDistanceXY += point.WorldDistanceXYTo(prev);
            }

            wayPoints.Push(point);
        }

        // The average-spacing heuristic in RefillRouteToNextWaypoint means "a hop about as
        // long as this route's own point spacing is not worth pathfinding for". That holds
        // for a recorded route of hundreds of points a few yards apart. It is actively wrong
        // for sparse waypoints, where the spacing IS the long hop: an 8-anchor route across
        // a subzone yields AvgDistance ~60yd, so a 100yd leg counted as trivial and the bot
        // walked blind through whatever stood in the way.
        AvgDistance = SparseWaypoints
            ? OutDoorMinDistance
            : wayPoints.Count > 1
                ? Max(mapDistanceXY / wayPoints.Count, OutDoorMinDistance)
                : OutDoorMinDistance;

        UpdateTotalRoute();

        static bool IsMapPoint(Vector3 p)
        {
            return
                p.X is >= 0 and <= 100 &&
                p.Y is >= 0 and <= 100;
        }
    }

    public void ResetStuckParameters()
    {
        stuckDetector.Reset();
    }

    private void RefillRouteToNextWaypoint(CancellationToken token)
    {
        routeToNextWaypoint.Clear();
        spline.Clear();
        ReleaseTurnKeys();

        Vector3 playerW = playerReader.WorldPos;
        Vector3 targetW = wayPoints.Peek();
        float distance = playerW.WorldDistanceXYTo(targetW);

        if (distance > MaxDistance || distance > AvgDistance * 2)
        {
            if (debug)
                LogDebug($"Distance: {distance} vs Avg:({AvgDistance * 2},{AvgDistance}) - TAVG: {DIFF_THRESHOLD * AvgDistance} ");

            stopMoving.Stop();
            PathRequest(new PathRequest(playerReader.UIMapId.Value, bits.Indoors(), playerW, targetW, distance, PathCalculatedCallback));
        }
        else
        {
            if (debug)
                LogDebug($"non pathfinder - {distance} - {playerW} -> {targetW}");

            routeToNextWaypoint.Push(targetW);

            Vector3 playerM = WorldMapAreaDB.ToMap_FlipXY(playerW, playerReader.WorldMapArea);
            Vector3 targetM = WorldMapAreaDB.ToMap_FlipXY(targetW, playerReader.WorldMapArea);
            float heading = DirectionCalculator.CalculateMapHeading(playerM, targetM);
            AdjustHeading(heading, token);

            stuckDetector.SetTargetLocation(targetW);
            UpdateTotalRoute();
        }
    }

    private void PathRequest(PathRequest pathRequest)
    {
        pathRequests.Enqueue(pathRequest);
        manualReset.Set();
    }

    private void PathCalculatedCallback(PathResult result)
    {
        if (!active)
        {
            return;
        }

        // TODO: fix this later
        // Consider trivial proximity as a successful "no path needed"
        const float TrivialDistanceThreshold = MinDistanceMount; // adjust as per your units

        float distance = (result.EndW - result.StartW).Length(); // assuming it's a Vector3 or similar
        bool isTriviallyClose = result.Path.Length == 0 && distance < TrivialDistanceThreshold;

        LogPathfinderTrivial(logger, isTriviallyClose, result.ElapsedMs, result.StartW, result.EndW);

        if (result.Path.Length == 0 && !isTriviallyClose)
        {
            if (lastFailedDestination != result.EndW)
            {
                lastFailedDestination = result.EndW;
                LogPathfinderFailed(logger, result.StartW, result.EndW, result.ElapsedMs);
            }

            failedAttempt++;

            if (failedAttempt == 1 && bits.Indoors())
            {
                // try to find closest spawn point
                (Creature creature, Vector3 worldPos) = areaDB.FindClosestCreatureByNpcFlag(NpcFlags.None, playerReader.WorldPos);
                playerReader.WorldPosZ = worldPos.Z;

                logger.LogWarning("Found closest spawn {Name}", creature.Name);
            }
 
            if (failedAttempt > 2)
            {
                failedAttempt = 0;
                stuckDetector.SetTargetLocation(result.EndW);
                stuckDetector.Update();

                OnNoPathFound?.Invoke();
            }
            return;
        }

        failedAttempt = 0;
        // TODO: fix this later
        //if (!isTriviallyClose)
        {
            LogPathfinderSuccess(logger, result.Distance, result.StartW, result.EndW, result.ElapsedMs);

            for (int i = result.Path.Length - 1; i >= 0; i--)
            {
                routeToNextWaypoint.Push(result.Path[i]);
            }

            if (splineSettings.Enabled && pather.PathsAreSmoothed)
            {
                // The spline follower consumes the path raw - the density IS
                // the mechanism. Douglas-Peucker would gut it on straights and
                // leave clusters only at turns, which is exactly the shape the
                // legacy popper mishandles.
                spline.SetPath(result.Path, playerReader.RunSpeed);
                LogSplineLoaded(logger, result.Path.Length, playerReader.RunSpeed);
            }
            else if (ShouldSimplify)
            {
                SimplyfyRouteToWaypoint();
            }
        }

        if (routeToNextWaypoint.Count == 0)
        {
            routeToNextWaypoint.Push(wayPoints.Peek());

            if (debug)
                LogDebug($"RefillRouteToNextWaypoint -- WayPoint reached! {wayPoints.Count}");
        }

        // The spline branch seeds a short-lookahead progress target: the
        // legacy peek is the first route point, which is roughly the player's
        // own position and only works because the legacy popper immediately
        // replaces it.
        stuckDetector.SetTargetLocation(SplineActive
            ? spline.PointAt(2)
            : routeToNextWaypoint.Peek());
        UpdateTotalRoute();

        OnPathCalculated?.Invoke();
    }

    private void PathFinderThread()
    {
        while (!token.IsCancellationRequested)
        {
            manualReset.Reset();
            if (pathRequests.TryPeek(out PathRequest pathRequest))
            {
                try
                {
                    Vector3[] path = pather.FindWorldRoute(pathRequest.MapId, pathRequest.StartIndoors, pathRequest.StartW, pathRequest.EndW);
                    if (active)
                    {
                        pathResults.Enqueue(new PathResult(pathRequest, path, pathRequest.Callback));
                    }
                    pathRequests.Dequeue();
                }
                catch (Exception ex) when (ex is BadImageFormatException or DllNotFoundException or PlatformNotSupportedException)
                {
                    // Native StormLib (MPQ reader) failed to load - almost always a
                    // missing or architecture-mismatched MPQ\StormLib_<arch>.dll.
                    // Stop the thread instead of taking the whole process down.
                    LogNativePathingLoadFailed(logger, ex);
                    pathRequests.Dequeue();
                    break;
                }
            }
            manualReset.Wait();
        }

        LogThreadStopped(logger);
    }

    private float ReachedDistance(float minDistance)
    {
        float speed = playerReader.RunSpeed;
        if (speed <= 0f)
        {
            // Fallback: addon data unavailable
            return mountHandler.IsMounted()
                ? MinDistanceMount
                : bits.Indoors()
                    ? IndoorMinDistance
                    : minDistance;
        }

        float baseDistance = bits.Indoors() ? IndoorMinDistance : minDistance;
        float lookAhead = bits.Indoors() ? INDOOR_LOOK_AHEAD : OUTDOOR_LOOK_AHEAD;
        return Max(baseDistance, speed * lookAhead);
    }

    private void ReduceByDistance(Vector3 playerW, float minDistance)
    {
        while (routeToNextWaypoint.Count > 0 &&
            playerW.WorldDistanceXYTo(routeToNextWaypoint.Peek()) < ReachedDistance(minDistance))
        {
            routeToNextWaypoint.Pop();
        }
    }

    private void AdjustHeading(float heading, CancellationToken token)
    {
        float diff1 = Abs(Tau + heading - playerReader.Direction) % Tau;
        float diff2 = Abs(heading - playerReader.Direction - Tau) % Tau;

        float diff = Min(diff1, diff2);
        if (diff > minAngleToTurn)
        {
            if (diff > minAngleToStopBeforeTurn)
            {
                stopMoving.Stop();
            }

            playerDirection.SetDirection(heading, routeToNextWaypoint.Peek(), ReachedDistance(OutDoorMinDistance), token);
        }
    }

    private bool AdjustNextWaypointPointToClosest()
    {
        if (wayPoints.Count < 2) { return false; }

        Vector3 A = wayPoints.Pop();
        Vector3 B = wayPoints.Peek();
        Vector2 result = VectorExt.GetClosestPointOnLineSegment(A.AsVector2(), B.AsVector2(), playerReader.WorldPos.AsVector2());
        Vector3 newPoint = new(result.X, result.Y, playerReader.WorldPosZ);

        if (newPoint.WorldDistanceXYTo(wayPoints.Peek()) > OutDoorMinDistance)
        {
            wayPoints.Push(newPoint);
            if (debug)
                LogDebug("Adjusted resume point");

            return false;
        }

        if (debug)
            LogDebug("Skipped next point in path");

        return true;
    }

    private void V1_AttemptToKeepRouteToWaypoint()
    {
        float totalDistance = VectorExt.TotalDistance<Vector3>(TotalRoute, VectorExt.WorldDistanceXY);
        if (totalDistance > MaxDistance / 2)
        {
            Vector3 playerW = playerReader.WorldPos;
            float distanceToRoute = playerW.WorldDistanceXYTo(routeToNextWaypoint.Peek());
            float distanceToPrevLoc = playerW.WorldDistanceXYTo(playerWorldPos);
            float dynamicThreshold = 2 * ReachedDistance(OutDoorMinDistance);
            if (distanceToRoute > dynamicThreshold &&
                distanceToPrevLoc > dynamicThreshold)
            {
                LogV1ClearRouteToWaypoint(logger, patherName, distanceToRoute);
                routeToNextWaypoint.Clear();
            }
            else
            {
                LogV1KeepRouteToWaypoint(logger, patherName, distanceToRoute);
                ResetStuckParameters();
            }
        }
        else
        {
            LogV1ClearRouteToWaypointTooFar(logger, patherName, totalDistance, MaxDistance / 2);
            routeToNextWaypoint.Clear();
        }
    }

    private float SimplifyTolerance()
    {
        float speed = playerReader.RunSpeed;
        float baseTolerance = OutDoorMinDistance / 2;
        return speed <= 0f ? baseTolerance : Max(baseTolerance, speed * SIMPLIFY_LOOK_AHEAD);
    }

    private void SimplyfyRouteToWaypoint()
    {
        const bool HighQuality = false;
        Span<Vector3> reduced = PathSimplify.Simplify(routeToNextWaypoint.ToArray(), SimplifyTolerance(), HighQuality);
        if (debug)
            LogDebug($"{nameof(SimplyfyRouteToWaypoint)} {routeToNextWaypoint.Count} -> {reduced.Length} | HQ: {HighQuality}");

        routeToNextWaypoint.Clear();
        for (int i = reduced.Length - 1; i >= 0; i--)
        {
            routeToNextWaypoint.Push(reduced[i]);
        }
    }

    private void UpdateTotalRoute()
    {
        TotalRoute = new Vector3[routeToNextWaypoint.Count + wayPoints.Count];
        routeToNextWaypoint.CopyTo(TotalRoute, 0);
        wayPoints.CopyTo(TotalRoute, routeToNextWaypoint.Count);
    }

    private bool HasBeenActiveRecently()
    {
        return (DateTime.UtcNow - LastActive).TotalSeconds < 2;
    }


    private void LogDebug(string text)
    {
        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("D: {Text}", text);
    }

    #region Logging

    [LoggerMessage(
        EventId = 0046,
        Level = LogLevel.Warning,
        Message = "Pathfinder - Trivial: {isTriviallyClose} | {elapsedMs}ms - {startW} -> {endW}")]
    static partial void LogPathfinderTrivial(ILogger logger, bool isTriviallyClose, double elapsedMs, Vector3 startW, Vector3 endW);

    [LoggerMessage(
        EventId = 0047,
        Level = LogLevel.Debug,
        Message = "Thread stopped!")]
    static partial void LogThreadStopped(ILogger logger);

    [LoggerMessage(
        EventId = 0048,
        Level = LogLevel.Error,
        Message = "Local pathing disabled: failed to load native StormLib. " +
            "Ensure MPQ/StormLib_<arch>.dll exists and matches the process architecture " +
            "(x64/x86/arm64).")]
    static partial void LogNativePathingLoadFailed(ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = 0040,
        Level = LogLevel.Warning,
        Message = "Unable to find path {start} -> {end}. Character may stuck! {elapsedMs}ms")]
    static partial void LogPathfinderFailed(ILogger logger, Vector3 start, Vector3 end, double elapsedMs);

    [LoggerMessage(
        EventId = 0041,
        Level = LogLevel.Information,
        Message = "Pathfinder - {distance} - {start} -> {end} {elapsedMs}ms")]
    static partial void LogPathfinderSuccess(ILogger logger, float distance, Vector3 start, Vector3 end, double elapsedMs);

    [LoggerMessage(
        EventId = 0042,
        Level = LogLevel.Information,
        Message = "Clear route to waypoint! Stucked for {elapsedMs}ms")]
    static partial void LogClearRouteToWaypointStuck(ILogger logger, double elapsedMs);

    [LoggerMessage(
        EventId = 0043,
        Level = LogLevel.Information,
        Message = "[{name}] distance from nearlest point is {distance}. Have to clear RouteToWaypoint.")]
    static partial void LogV1ClearRouteToWaypoint(ILogger logger, string name, float distance);

    [LoggerMessage(
        EventId = 0044,
        Level = LogLevel.Information,
        Message = "[{name}] distance is close {distance}. Keep RouteToWaypoint.")]
    static partial void LogV1KeepRouteToWaypoint(ILogger logger, string name, float distance);

    [LoggerMessage(
        EventId = 0045,
        Level = LogLevel.Information,
        Message = "[{name}] total distance {totalDistance} > {maxDistancehalf}. Have to clear RouteToWaypoint.")]
    static partial void LogV1ClearRouteToWaypointTooFar(ILogger logger, string name, float totalDistance, float maxDistancehalf);

    [LoggerMessage(
        EventId = 0046,
        Level = LogLevel.Warning,
        Message = "Spline follower off path by {distance:0.0}yd - requesting a fresh path")]
    static partial void LogSplineOffPath(ILogger logger, float distance);

    [LoggerMessage(
        EventId = 0049,
        Level = LogLevel.Information,
        Message = "Follower: {which} (SplineFollower.Enabled={enabled}, PathsAreSmoothed={smoothed}, WalkKey={canWalk})")]
    static partial void LogFollowerSelected(ILogger logger, string which, bool enabled, bool smoothed, bool canWalk);

    [LoggerMessage(
        EventId = 0050,
        Level = LogLevel.Debug,
        Message = "Spline loaded: {points} points, runSpeed {runSpeed:0.0}")]
    static partial void LogSplineLoaded(ILogger logger, int points, float runSpeed);

    [LoggerMessage(
        EventId = 0051,
        Level = LogLevel.Trace,
        Message = "Spline consumed {consumed} point(s), {remaining} remain")]
    static partial void LogSplineConsumed(ILogger logger, int consumed, int remaining);

    [LoggerMessage(
        EventId = 0052,
        Level = LogLevel.Debug,
        Message = "Spline segment completed, {waypointsRemaining} waypoint(s) remain")]
    static partial void LogSplineCompleted(ILogger logger, int waypointsRemaining);

    [LoggerMessage(
        EventId = 0053,
        Level = LogLevel.Debug,
        Message = "Spline brake={braking} (offPath {offPath:0.0}yd)")]
    static partial void LogSplineBrake(ILogger logger, bool braking, float offPath);

    [LoggerMessage(
        EventId = 0054,
        Level = LogLevel.Debug,
        Message = "Spline walk-speed={walk}")]
    static partial void LogSplineWalkToggle(ILogger logger, bool walk);

    [LoggerMessage(
        EventId = 0055,
        Level = LogLevel.Warning,
        Message = "Spline wants walk-speed hairpin approach but no walk key bound (TOGGLERUN/WalkKey); staying at run speed")]
    static partial void LogSplineNoWalkKey(ILogger logger);

    #endregion
}