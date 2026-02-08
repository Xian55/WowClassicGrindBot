using Core.Addon;
using Core.GoalsComponent;
using Core.GOAP;

using Game;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using SharedLib;
using SharedLib.Extensions;

using System;
using System.Numerics;
using System.Threading;

namespace Core.Goals;

public sealed class AutoGatherGoal : GoapGoal, IGoapEventListener, IRouteProvider, IDisposable
{
    public const string KeyActionName = "AutoGathering";

    private const float INTERACTION_RANGE_YARDS = 2f;

    public override float Cost => key.Cost;
    public DateTime LastActive => navigation.LastActive;

    private readonly ILogger<AutoGatherGoal> logger;
    private readonly ConfigurableInput input;
    private readonly Navigation navigation;
    private readonly KeyAction key;
    private readonly PlayerReader playerReader;
    private readonly Wait wait;
    private readonly AddonBits bits;
    private readonly FoundNodeListener foundNodeListener;
    private readonly StopMoving stopMoving;
    private readonly CursorScan cursorScan;
    private readonly IMouseInput mouseInput;
    private readonly PlayerDirection playerDirection;
    private readonly CancellationToken token;

    public AutoGatherGoal(
        ILogger<AutoGatherGoal> logger,
        ConfigurableInput input,
        Navigation navigation,
        PlayerReader playerReader,
        Wait wait,
        AddonBits bits,
        FoundNodeListener foundNodeListener,
        StopMoving stopMoving,
        CursorScan cursorScan,
        IMouseInput mouseInput,
        PlayerDirection playerDirection,
        CancellationTokenSource cts,
        [FromKeyedServices(KeyActionName)] KeyAction keyAction
        ) : base(nameof(AutoGatherGoal))
    {
        this.logger = logger;
        this.input = input;
        this.wait = wait;
        this.playerReader = playerReader;
        this.navigation = navigation;
        this.foundNodeListener = foundNodeListener;
        this.bits = bits;
        this.stopMoving = stopMoving;
        this.cursorScan = cursorScan;
        this.mouseInput = mouseInput;
        this.playerDirection = playerDirection;
        this.token = cts.Token;

        key = keyAction;

        foundNodeListener.NodeFound += FoundNode;
    }

    public void Dispose()
    {
        foundNodeListener.NodeFound -= FoundNode;

        navigation.Dispose();
    }

    public override bool CanRun() =>
        key.Path != Array.Empty<Vector3>() &&
        (key.Path.Length == 1 && key.Path[0] != default) &&
        key.CanRun();

    public bool HasNext()
    {
        return navigation.HasNext();
    }

    public Vector3[] MapRoute()
    {
        return key.Path;
    }

    public Vector3 NextMapPoint()
    {
        return navigation.NextMapPoint();
    }
    public Vector3[] PathingRoute()
    {
        return navigation.TotalRoute;
    }

    public void OnGoapEvent(GoapEventArgs e)
    {
        if (e.GetType() == typeof(ResumeEvent))
        {
            Resume();

        }
        else if (e.GetType() == typeof(AbortEvent))
        {
            Abort();
        }
    }

    private void Resume()
    {
        navigation.Resume();
    }

    private void Abort()
    {
        navigation.StopMovement();
        navigation.Stop();
    }

    public override void OnEnter() => Resume();

    public override void OnExit() => Abort();

    public override void Update()
    {
        if (bits.Drowning())
            input.PressJump();

        // Check if we're close to the target node
        if (key.Path.Length == 1 && key.Path[0] != default)
        {
            Vector3 nodeMapPos = key.Path[0];
            Vector3 nodeWorldPos = WorldMapAreaDB.ToWorld_FlipXY(nodeMapPos, playerReader.WorldMapArea);
            float distanceYards = playerReader.WorldPos.WorldDistanceXYTo(nodeWorldPos);

            if (distanceYards < INTERACTION_RANGE_YARDS)
            {
                // Stop moving
                navigation.StopMovement();

                // If already casting (gathering), wait for it
                if (playerReader.IsCasting())
                {
                    wait.Update();
                    return;
                }

                // Turn to face the node - positions it at screen center
                float targetDirection = DirectionCalculator.CalculateMapHeading(playerReader.MapPosNoZ, nodeMapPos);
                playerDirection.SetDirection(targetDirection, token);
                wait.Update();

                // Try soft interact first (most reliable)
                if (bits.SoftInteract_Enabled())
                {
                    int id = playerReader.SoftInteract_Id;
                    if (id != 0 && (GameObject.IsMineral(id) || GameObject.IsHerb(id)))
                    {
                        input.PressInteract();
                        wait.Update();
                        return;
                    }
                }

                // Fallback: spiral cursor scan for herb/mining icons
                if (!input.KeyboardOnly)
                {
                    ReadOnlySpan<CursorType> gatherCursors = [CursorType.Mine, CursorType.Herb];

                    // Check if cursor is already over a node
                    if (cursorScan.TryMatchCurrent(gatherCursors, out _) ||
                        cursorScan.FindAny(gatherCursors, out _, out _))
                    {
                        mouseInput.InteractMouseOver(token);
                        wait.Update();
                        return;
                    }
                }
            }
        }

        // Continue navigation if not close enough or gathering failed
        navigation.Update();
        wait.Update();
    }

    public void FoundNode(Vector3 node)
    {
        if (node == default)
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("FoundNode: default node, clearing path");
            }
            key.Path = [];
            return;
        }

        if (key.Path.Length == 1 && key.Path[0] == node)
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("FoundNode: same node, skipping");
            }
            return;
        }

        if (key.Path.Length > 0 && Vector2.Distance(node.AsVector2(), key.Path[0].AsVector2()) < 0.05f)
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("FoundNode: node too close ({Dist:F4}), skipping",
                    Vector2.Distance(node.AsVector2(), key.Path[0].AsVector2()));
            }
            return;
        }

        logger.LogWarning($"Found node at {node}");

        key.Path = [node];
        navigation.SetWayPoints(key.Path);
    }
}
