using Core.GoalsComponent;

using Microsoft.Extensions.Logging;

using SharedLib;
using SharedLib.Data;

using System.Numerics;

namespace Core.Goals;

public readonly record struct PartyLeaderSnapshot(
    Vector3 MapPosition,
    Vector3 WorldPosition,
    bool HasMap,
    bool HasWorld,
    bool LeaderInCombat,
    int MapId)
{
    public static PartyLeaderSnapshot None => new(Vector3.Zero, Vector3.Zero, false, false, false, 0);

    public bool HasPosition => HasMap || HasWorld;

    public static PartyLeaderSnapshot FromMap(Vector3 map, bool leaderInCombat, int mapId)
    {
        return new(map, Vector3.Zero, true, false, leaderInCombat, mapId);
    }

    public static PartyLeaderSnapshot FromWorld(Vector3 world, bool leaderInCombat, int mapId)
    {
        return new(Vector3.Zero, world, false, true, leaderInCombat, mapId);
    }

    public static PartyLeaderSnapshot FromBoth(Vector3 map, Vector3 world, bool leaderInCombat, int mapId)
    {
        return new(map, world, true, true, leaderInCombat, mapId);
    }

    public bool TryGetWaypoint(CoordinateSource source, WorldMapArea currentArea, out Vector3 waypoint, out Vector3 world)
    {
        waypoint = Vector3.Zero;
        world = Vector3.Zero;

        if (source == CoordinateSource.World)
        {
            if (HasWorld)
            {
                waypoint = WorldPosition;
                world = WorldPosition;
                return true;
            }

            if (HasMap)
            {
                world = WorldMapAreaDB.ToWorld_FlipXY(MapPosition, currentArea);
                waypoint = world;
                return true;
            }

            return false;
        }

        if (HasMap)
        {
            waypoint = MapPosition;
            world = WorldMapAreaDB.ToWorld_FlipXY(MapPosition, currentArea);
            return true;
        }

        if (HasWorld)
        {
            world = WorldPosition;
            waypoint = WorldMapAreaDB.ToMap_FlipXY(world, currentArea);
            return true;
        }

        return false;
    }
}

public interface IPartyLeaderProvider
{
    PartyLeaderSnapshot GetSnapshot(ClassConfiguration classConfiguration);
}

public sealed class PartyLeaderProvider : IPartyLeaderProvider
{
    private readonly ILogger<PartyLeaderProvider> logger;
    private readonly PlayerReader playerReader;
    private readonly ConfigurableInput input;
    private readonly AddonBits bits;
    private readonly Wait wait;

    public PartyLeaderProvider(ILogger<PartyLeaderProvider> logger,
        PlayerReader playerReader,
        ConfigurableInput input,
        AddonBits bits,
        Wait wait)
    {
        this.logger = logger;
        this.playerReader = playerReader;
        this.input = input;
        this.bits = bits;
        this.wait = wait;
    }

    public PartyLeaderSnapshot GetSnapshot(ClassConfiguration classConfiguration)
    {
        PartyOptions party = classConfiguration.Party;

        return party.Mode switch
        {
            PartyFollowMode.Focus => ResolveFocus(),
            PartyFollowMode.PartySlot => ResolvePartySlot(party.LeaderSlot),
            PartyFollowMode.Name => ResolvePartyName(party.LeaderName),
            _ => PartyLeaderSnapshot.None
        };
    }

    private PartyLeaderSnapshot ResolvePartySlot(int slot)
    {
        if (playerReader.TryGetPartySnapshotBySlot(slot, out PartyLeaderSnapshot snapshot))
        {
            return snapshot;
        }

        return PartyLeaderSnapshot.None;
    }

    private PartyLeaderSnapshot ResolvePartyName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return PartyLeaderSnapshot.None;
        }

        if (playerReader.TryGetPartySnapshotByName(name, out PartyLeaderSnapshot snapshot))
        {
            return snapshot;
        }

        return PartyLeaderSnapshot.None;
    }

    private PartyLeaderSnapshot ResolveFocus()
    {
        if (!bits.Focus() || playerReader.FocusGuid == 0)
        {
            return PartyLeaderSnapshot.None;
        }

        if (playerReader.TargetGuid != playerReader.FocusGuid)
        {
            input.PressTargetFocus();
            wait.Update();
        }

        if (playerReader.TargetGuid != playerReader.FocusGuid)
        {
            return PartyLeaderSnapshot.None;
        }

        Vector3 focusMap = playerReader.TargetMapPos;
        if (focusMap == Vector3.Zero)
        {
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("No focus map position available");
            return PartyLeaderSnapshot.None;
        }

        Vector3 focusWorld = WorldMapAreaDB.ToWorld_FlipXY(focusMap, playerReader.WorldMapArea);
        bool leaderInCombat = bits.Focus_Combat();

        return PartyLeaderSnapshot.FromBoth(focusMap, focusWorld, leaderInCombat, playerReader.UIMapId.Value);
    }
}
