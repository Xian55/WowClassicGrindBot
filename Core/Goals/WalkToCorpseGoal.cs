using Core.Database;
using Core.GOAP;

using Microsoft.Extensions.Logging;

using SharedLib;
using SharedLib.Data;

using System;
using System.Numerics;

namespace Core.Goals;

public sealed partial class WalkToCorpseGoal : GoapGoal, IGoapEventListener, IRouteProvider, IDisposable
{
    public override float Cost => 1f;

    private readonly ILogger<WalkToCorpseGoal> logger;
    private readonly Wait wait;
    private readonly ConfigurableInput input;

    private readonly PlayerReader playerReader;
    private readonly AddonBits bits;
    private readonly Navigation navigation;
    private readonly StopMoving stopMoving;

    private readonly AreaDB areaDB;

    private DateTime onEnterTime;

    #region IRouteProvider

    public DateTime LastActive => navigation.LastActive;

    public Vector3[] MapRoute()
    {
        return Array.Empty<Vector3>();
    }


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

    public WalkToCorpseGoal(ILogger<WalkToCorpseGoal> logger,
        ConfigurableInput input, Wait wait,
        PlayerReader playerReader, AddonBits bits,
        Navigation navigation, StopMoving stopMoving, AreaDB areaDB)
        : base(nameof(WalkToCorpseGoal))
    {
        this.logger = logger;
        this.wait = wait;
        this.input = input;

        this.playerReader = playerReader;
        this.bits = bits;

        this.stopMoving = stopMoving;

        this.navigation = navigation;

        this.areaDB = areaDB;

        AddPrecondition(GoapKey.isdead, true);
    }

    public void Dispose()
    {
        navigation.Dispose();
    }

    public void OnGoapEvent(GoapEventArgs e)
    {
        if (e.GetType() == typeof(ResumeEvent))
        {
            navigation.ResetStuckParameters();
        }
    }

    public override void OnEnter()
    {
        playerReader.WorldPosZ = 0;

        wait.While(AliveOrLoadingScreen);

        (Creature npc, Vector3 worldPos)
            = areaDB.FindClosestCreatureByNpcFlag(NpcFlags.SpiritHealer, playerReader.WorldPos);

        Log($"Player teleported to the graveyard! {worldPos}");

        playerReader.WorldPosZ = worldPos.Z;

        Vector3 corpseLocation = playerReader.CorpseMapPos;
        Log($"Corpse location is {corpseLocation}");

        navigation.SetWayPoints([corpseLocation]);

        onEnterTime = DateTime.UtcNow;
    }

    public override void OnExit()
    {
        navigation.StopMovement();
        navigation.Stop();
    }

    public override void Update()
    {
        if (!bits.CorpseInRange())
        {
            navigation.Update();
        }
        else
        {
            stopMoving.Stop();
            navigation.ResetStuckParameters();
        }

        RandomJump();

        wait.Update();
    }

    private void RandomJump()
    {
        if ((DateTime.UtcNow - onEnterTime).TotalSeconds > 5 &&
            input.Jump.SinceLastClickMs > Random.Shared.Next(10_000, 25_000))
        {
            Log("Random jump");
            input.PressJump();
        }
    }

    private bool AliveOrLoadingScreen()
    {
        return bits.Dead() && playerReader.CorpseMapPos == Vector3.Zero;
    }

    private void Log(string text)
    {
        logger.LogInformation(text);
    }
}