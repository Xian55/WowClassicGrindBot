using SharedLib;

namespace Core;

/// <summary>
/// The four player facts route generation actually reads. Narrower than
/// <see cref="PlayerReader"/> on purpose: the generator has no business with combat state,
/// and depending on the whole reader would make it impossible to exercise without a running
/// game client and a live addon feed.
/// </summary>
public interface IRouteGenPlayer
{
    int Level { get; }
    UnitRace Race { get; }
    PlayerFaction Faction { get; }
    int UIMapId { get; }
}

/// <summary>Live implementation, reading through to the addon data.</summary>
public sealed class RouteGenPlayer(PlayerReader playerReader) : IRouteGenPlayer
{
    public int Level => playerReader.Level.Value;
    public UnitRace Race => playerReader.Race;
    public PlayerFaction Faction => playerReader.Faction;
    public int UIMapId => playerReader.UIMapId.Value;
}
