using Core.GOAP;

using Microsoft.Extensions.Logging;

using System;
using System.Buffers;
using System.Numerics;

namespace Core.Goals;

public sealed class FleeGoal : GoapGoal, IRouteProvider
{
    public override float Cost => 3.1f;

    private readonly ILogger<CombatGoal> logger;
    private readonly ConfigurableInput input;
    private readonly ClassConfiguration classConfig;
    private readonly Wait wait;
    private readonly PlayerReader playerReader;
    private readonly Navigation navigation;
    private readonly AddonBits bits;
    private readonly CastingHandler castingHandler;

    private readonly SafeSpotCollector safeSpotCollector;

    private Vector3[] MapPoints = [];

    public FleeGoal(ILogger<CombatGoal> logger, ConfigurableInput input,
        Wait wait, PlayerReader playerReader, AddonBits bits,
        CastingHandler castingHandler,
        ClassConfiguration classConfiguration, Navigation playerNavigation,
        ClassConfiguration classConfig,
        SafeSpotCollector safeSpotCollector)
        : base(nameof(FleeGoal))
    {
        this.logger = logger;
        this.input = input;

        this.wait = wait;
        this.playerReader = playerReader;
        this.navigation = playerNavigation;
        this.bits = bits;
        this.castingHandler = castingHandler;

        this.classConfig = classConfig;

        AddPrecondition(GoapKey.incombat, true);

        Keys = classConfiguration.Flee.Sequence;

        this.safeSpotCollector = safeSpotCollector;
    }

    #region IRouteProvider

    public DateTime LastActive => navigation.LastActive;

    public Vector3[] MapRoute() => MapPoints;

    public Vector3[] PathingRoute()
    {
        return navigation.TotalRoute;
    }

    public bool HasNext()
    {
        return navigation.HasNext();
    }

    public Vector3 NextMapPoint()
    {
        return navigation.NextMapPoint();
    }

    #endregion

    public override bool CanRun()
    {
        return
            safeSpotCollector.MapLocations.Count > 0 &&
            Keys.Length > 0 && Keys[0].CanRun();
    }

    public override void OnEnter()
    {
        int count = safeSpotCollector.MapLocations.Count;

        ArrayPool<Vector3> pooler = ArrayPool<Vector3>.Shared;
        Vector3[] array = pooler.Rent(count);
        var span = array.AsSpan();

        safeSpotCollector.MapLocations.CopyTo(array, 0);

        Span<Vector3> simplified = PathSimplify.Simplify(array.AsSpan()[..count], PathSimplify.HALF, true);
        MapPoints = simplified.ToArray();

        navigation.SetWayPoints(simplified);
        navigation.ResetStuckParameters();

        pooler.Return(array);
    }

    public override void OnExit()
    {
        safeSpotCollector.Reduce(playerReader.MapPosNoZ);

        navigation.Stop();
        navigation.StopMovement();

        if (bits.Target())
        {
            input.PressClearTarget();
        }
    }

    public override void Update()
    {
        wait.Update();
        navigation.Update();

        // first element is skipped 
        // its part of the Goal Custom Condition
        ReadOnlySpan<KeyAction> span = Keys;
        for (int i = 1; i < span.Length; i++)
        {
            KeyAction keyAction = span[i];

            if (castingHandler.SpellInQueue() && !keyAction.BaseAction)
            {
                continue;
            }

            if (castingHandler.CastIfReady(keyAction,
                keyAction.Interrupts.Count > 0
                ? keyAction.CanBeInterrupted
                : bits.Combat))
            {
                break;
            }
        }
    }
}
