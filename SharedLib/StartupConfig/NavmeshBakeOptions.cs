using System.Collections.Generic;

namespace SharedLib;

/// <summary>
/// Bake-time navmesh parameters, bound from configuration (section
/// <see cref="Position"/>). <see cref="AgentRadius"/>, <see cref="AgentMaxClimb"/>
/// and <see cref="WalkableSlope"/> feed the tile-cache settings hash, so a change
/// to any of them yields a distinct cache directory and a fresh on-demand bake;
/// the defaults are the values the engine was tuned against in-game.
/// </summary>
public sealed class NavmeshBakeOptions
{
    public const string Position = "Navmesh:Bake";

    /// <summary>
    /// Clearance the bake keeps from obstacles, yards. Feeds the settings hash.
    /// 0.533 is the exact WoW collision radius - right for a server NPC on the
    /// path centreline, but WASD movement is not that precise and at that
    /// clearance the follower clipped trees and hugged doorframes. 0.5-0.58
    /// tested as the sweet spot in-game: margin for keyboard movement without
    /// sealing tight interiors (whose ceiling is any gap narrower than 2*radius).
    /// </summary>
    public float AgentRadius { get; set; } = 0.5f;

    /// <summary>
    /// Largest step treated as walkable rather than a ledge, yards. Feeds the
    /// settings hash. 1.6 lets the agent walk fences and small steps like the
    /// client - but also connects climbable tree bases (1.0-1.6yd steps), so a
    /// path can route onto a tree; lower it to drop those surfaces from the mesh.
    /// </summary>
    public float AgentMaxClimb { get; set; } = 1.6f;

    /// <summary>
    /// Surfaces steeper than this are unwalkable, degrees. Feeds the settings
    /// hash. TrinityCore's 55 keeps climbable-tree slopes (48-55 deg) in the
    /// mesh, so paths could route onto trees the follower jammed against; 48
    /// drops trees while keeping the interiors that matter reachable.
    /// </summary>
    public float WalkableSlope { get; set; } = 48f;

    /// <summary>
    /// Tile bake workers. Null auto-derives from the CPU count. Not part of the
    /// settings hash - worker count does not change the produced geometry.
    /// </summary>
    public int? BakeWorkers { get; set; }

    /// <summary>
    /// Per-continent world-Z floor, yards: geometry whose highest vertex sits
    /// below it is dropped for that continent only. Keyed by continent name
    /// (e.g. "Expansion01"), so a floor set for one continent never disturbs
    /// another's cache. Empty (default) keeps everything everywhere.
    ///
    /// For a floating continent like Outland the MPQ still carries the low
    /// "base" terrain far under the playable landmass (~Z -1190); a player who
    /// reached it would only fall and die, so it must not become walkable
    /// navmesh. Configure e.g. { "Expansion01": -700 } - a cutoff between the
    /// base and the lowest real ground. The resolved value feeds the settings
    /// hash, so a filtered continent bakes into its own cache directory while
    /// unlisted continents keep their existing hash.
    /// </summary>
    public Dictionary<string, float> MinWorldZ { get; set; } = [];

    /// <summary>The world-Z floor configured for <paramref name="continent"/>, or null if none.</summary>
    public float? ResolveMinWorldZ(string continent) =>
        MinWorldZ.TryGetValue(continent, out float z) ? z : null;
}
