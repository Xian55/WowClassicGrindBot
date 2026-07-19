namespace SharedLib;

/// <summary>
/// Which in-process pathfinding engine PPatherService uses.
/// </summary>
public enum PathingEngine
{
    /// <summary>Legacy on-demand spot-grid A* (PathGraph).</summary>
    SpotAStar,

    /// <summary>DotRecast navmesh: runtime-baked tiles + Detour query.</summary>
    Navmesh
}
