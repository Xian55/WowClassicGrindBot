using Core.Addon;
using Core.GoalsComponent;
using Core.GOAP;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using SharedLib.Extensions;

using System;
using System.Numerics;

namespace Core.Goals;

public sealed class AutoGatherGoal : GoapGoal, IGoapEventListener, IRouteProvider, IDisposable
{
    public const string KeyActionName = "AutoGathering";

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

    public AutoGatherGoal(
        ILogger<AutoGatherGoal> logger,
        ConfigurableInput input,
        Navigation navigation,
        PlayerReader playerReader,
        Wait wait,
        AddonBits bits,
        FoundNodeListener foundNodeListener,
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

        if (bits.SoftInteract_Enabled())
        {
            int id = playerReader.SoftInteract_Id;

            if (id != 0 && (GameObject.IsMineral(id) || GameObject.IsHerb(id)))
            {
                input.PressInteract();
                wait.Update();
            }
        }

        //if (pathState != PathState.Finished)
        navigation.Update();

        wait.Update();
    }

    public void FoundNode(Vector3 node)
    {
        if (node == default)
        {
            key.Path = [];
            return;
        }

        if (key.Path.Length == 1 && key.Path[0] == node)
        {
            return;
        }

        if (key.Path.Length > 0 && Vector2.Distance(node.AsVector2(), key.Path[0].AsVector2()) < 0.05f)
        {
            return;
        }

        logger.LogWarning($"Found node at {node}");

        key.Path = [node];
        navigation.SetWayPoints(key.Path);
    }
}
