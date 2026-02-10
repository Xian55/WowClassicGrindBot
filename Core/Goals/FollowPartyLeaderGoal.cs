using Core.GOAP;
using Core.GoalsComponent;

using Microsoft.Extensions.Logging;

using SharedLib;
using SharedLib.Extensions;
using SharedLib.Data;

using System;
using System.Numerics;

namespace Core.Goals;

public sealed class FollowPartyLeaderGoal : GoapGoal
{
    private const int LosFailureThreshold = 5;
    private const float DefaultCost = 20f;

    public override float Cost => DefaultCost;

    private readonly ILogger<FollowPartyLeaderGoal> logger;
    private readonly Navigation navigation;
    private readonly PlayerReader playerReader;
    private readonly ClassConfiguration classConfiguration;
    private PartyOptions Party => classConfiguration.Party;
    private readonly IPartyLeaderProvider leaderProvider;
    private readonly Wait wait;
    private readonly AddonBits bits;

    private DateTime lastPathRequest;
    private Vector3 lastKnownLeaderWorld;
    private int losFailureCount;

    public FollowPartyLeaderGoal(ILogger<FollowPartyLeaderGoal> logger,
        Navigation navigation,
        PlayerReader playerReader,
        ClassConfiguration classConfiguration,
        IPartyLeaderProvider leaderProvider,
        Wait wait,
        AddonBits bits)
        : base(nameof(FollowPartyLeaderGoal))
    {
        this.logger = logger;
        this.navigation = navigation;
        this.playerReader = playerReader;
        this.classConfiguration = classConfiguration;
        this.leaderProvider = leaderProvider;
        this.wait = wait;
        this.bits = bits;

        AddPrecondition(GoapKey.dangercombat, false);
        AddPrecondition(GoapKey.damagedone, false);
        AddPrecondition(GoapKey.damagetaken, false);
        AddPrecondition(GoapKey.producedcorpse, false);
        AddPrecondition(GoapKey.consumecorpse, false);
    }

    public override void OnEnter()
    {
        lastPathRequest = DateTime.MinValue;
        losFailureCount = 0;
        navigation.OnNoPathFound += HandleNoPath;
        navigation.OnPathCalculated += ResetFailures;
        navigation.OnAnyPointReached += ResetFailures;
    }

    public override void OnExit()
    {
        navigation.OnNoPathFound -= HandleNoPath;
        navigation.OnPathCalculated -= ResetFailures;
        navigation.OnAnyPointReached -= ResetFailures;
        navigation.Stop();
    }

    public override void Update()
    {
        PartyLeaderSnapshot snapshot = leaderProvider.GetSnapshot(classConfiguration);
        if (!snapshot.HasPosition)
        {
            navigation.StopMovement();
            wait.Update();
            return;
        }

        if (!snapshot.TryGetWaypoint(Party.CoordinateSource, playerReader.WorldMapArea, out Vector3 waypoint, out Vector3 leaderWorld))
        {
            navigation.StopMovement();
            wait.Update();
            return;
        }

        lastKnownLeaderWorld = leaderWorld;

        bool leaderInCombat = snapshot.LeaderInCombat;
        float distanceToLeader = playerReader.WorldPos.WorldDistanceXYTo(leaderWorld);

        if (leaderInCombat)
        {
            EnforceCombatLeash(distanceToLeader, waypoint);
            wait.Update();
            navigation.Update();
            return;
        }

        if (bits.Combat())
        {
            navigation.StopMovement();
            wait.Update();
            return;
        }

        if (distanceToLeader > Party.FollowRadius && ShouldRepath())
        {
            RequestPath(waypoint);
        }
        else if (distanceToLeader <= Party.FollowRadius)
        {
            navigation.StopMovement();
        }

        wait.Update();
        navigation.Update();
    }

    private void EnforceCombatLeash(float distanceToLeader, Vector3 waypoint)
    {
        if (distanceToLeader > Party.CombatLeash)
        {
            RequestPath(waypoint);
        }
        else
        {
            navigation.StopMovement();
        }
    }

    private bool ShouldRepath()
    {
        if (!navigation.HasWaypoint())
            return true;

        double elapsedSeconds = (DateTime.UtcNow - lastPathRequest).TotalSeconds;
        return elapsedSeconds >= Party.RepathIntervalSeconds;
    }

    private void RequestPath(Vector3 waypoint)
    {
        lastPathRequest = DateTime.UtcNow;

        Span<Vector3> points = stackalloc Vector3[1] { waypoint };
        navigation.SetWayPoints(points);
        navigation.ResetStuckParameters();
    }

    private void HandleNoPath()
    {
        if (Party.Enforcements.HasFlag(FollowRadiusEnforcements.LineOfSight))
        {
            losFailureCount++;
            if (losFailureCount >= LosFailureThreshold)
            {
                Regroup();
            }
        }

        if (Party.Enforcements.HasFlag(FollowRadiusEnforcements.FallbackToRegroup))
        {
            Regroup();
        }
    }

    private void ResetFailures()
    {
        losFailureCount = 0;
    }

    private void Regroup()
    {
        if (lastKnownLeaderWorld == Vector3.Zero)
        {
            return;
        }

        Vector3 waypoint = Party.CoordinateSource == CoordinateSource.World
            ? lastKnownLeaderWorld
            : WorldMapAreaDB.ToMap_FlipXY(lastKnownLeaderWorld, playerReader.WorldMapArea);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Regrouping toward leader at {Location}", waypoint.ToStringF());

        RequestPath(waypoint);
        ResetFailures();
    }
}
