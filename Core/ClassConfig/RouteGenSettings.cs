namespace Core;

/// <summary>
/// Whether a route should concentrate on where the mobs actually pack together, or try to
/// take in everything that matched the filter.
/// </summary>
public enum RouteFocus
{
    /// <summary>
    /// Stick to the packs. Isolated spawn clusters are dropped, and stops are drawn heavily
    /// toward locally dense spots. Best for levelling - kills per yard walked is what
    /// matters, and a stray mob across a lake is not worth the swim.
    /// </summary>
    Density,

    /// <summary>Middle ground: drop only the sparsest outliers, mild density bias.</summary>
    Balanced,

    /// <summary>
    /// Take in everything that matched, spread evenly. Use when the point is to sweep an
    /// area - gathering, or hunting a rare that can be anywhere.
    /// </summary>
    Coverage
}

public enum RouteGenMode
{
    /// <summary>
    /// A fresh set of anchors inside the spawn polygon every lap, so the bot never
    /// retraces a line. <see cref="PathSettings.PathThereAndBack"/> is ignored.
    /// </summary>
    Wander,

    /// <summary>
    /// One closed tour over spawn-cluster centroids, ordered by a TSP solver. Generated
    /// once at session build time and deterministic for a given seed - the drop-in
    /// replacement for a hand-recorded route file.
    /// </summary>
    Loop
}

/// <summary>
/// Describes a grind route as a query over the running client's NPC spawn data, instead
/// of pointing at a recorded waypoint file. Set on <see cref="PathSettings.Generate"/>.
/// See docs/generated-routes-design.md.
/// </summary>
public sealed class RouteGenSettings
{
    /// <summary>Zone name, matched against <c>WorldMapArea.AreaName</c>.</summary>
    public string? Zone { get; set; }

    /// <summary>Subzone name - narrower than <see cref="Zone"/>, e.g. "Northshire Valley".</summary>
    public string? Subzone { get; set; }

    /// <summary>Explicit override; wins over both names when &gt; 0.</summary>
    public int UIMapId { get; set; }

    /// <summary>Label for logs, the goal name and the UI badge.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// The whole mob selector, as a requirement expression in <b>creature scope</b> - the
    /// subject is a candidate creature row, not the player. <c>Level</c> is the creature's
    /// level; the player's is <c>PlayerLevel</c>. Empty means the default level window.
    /// See <see cref="CreatureRequirementFactory"/>.
    /// </summary>
    public string Mobs { get; set; } = string.Empty;

    public RouteGenMode Mode { get; set; } = RouteGenMode.Wander;

    /// <summary>Pack together, or cover the whole spread. See <see cref="RouteFocus"/>.</summary>
    public RouteFocus Focus { get; set; } = RouteFocus.Density;

    /// <summary>
    /// A spawn cluster is kept only if it holds at least this share of the spawns the
    /// biggest cluster holds. Derived from <see cref="Focus"/> unless set explicitly.
    /// </summary>
    public float? MinClusterShare { get; set; }

    /// <summary>
    /// How hard anchor selection leans toward locally dense spots. 0 is uniform, 1 is
    /// proportional to density, 2 strongly favours packs. Derived from
    /// <see cref="Focus"/> unless set explicitly.
    /// </summary>
    public float? DensityBias { get; set; }

    /// <summary>Effective cluster threshold, honouring an explicit override.</summary>
    public float ResolvedMinClusterShare => MinClusterShare ?? Focus switch
    {
        RouteFocus.Density => 0.25f,
        RouteFocus.Balanced => 0.10f,
        _ => 0f
    };

    /// <summary>Effective density bias, honouring an explicit override.</summary>
    public float ResolvedDensityBias => DensityBias ?? Focus switch
    {
        RouteFocus.Density => 2f,
        RouteFocus.Balanced => 1f,
        _ => 0f
    };

    /// <summary>
    /// Minimum share of the busiest cell's spawn count that a cell must hold to become a
    /// <see cref="RouteGenMode.Loop"/> stop.
    ///
    /// <para><see cref="DensityBias"/> only steers the random draw a wander route makes, so
    /// it does nothing in Loop, which picks its stops deterministically. This is the knob
    /// that makes <see cref="Focus"/> mean something there: it is what excludes the thin
    /// fringe cells holding one stray mob.</para>
    /// </summary>
    public float? MinCellShare { get; set; }

    /// <summary>Effective cell-density floor, honouring an explicit override.</summary>
    public float ResolvedMinCellShare => MinCellShare ?? Focus switch
    {
        RouteFocus.Density => 0.35f,
        RouteFocus.Balanced => 0.15f,
        _ => 0f
    };

    /// <summary>
    /// Anchors per generated route. These are hunting spots the bot roams between, not a
    /// traced path, so this is far smaller than a recorded route's point count.
    /// </summary>
    public int Stops { get; set; } = 8;

    /// <summary>How far the spawn-cell mask is dilated before sampling.</summary>
    public float PaddingYards { get; set; } = 25f;

    /// <summary>
    /// Rejects a sampled point whose z differs from its anchoring spawn by more than this.
    /// The cheap half of the roof/canopy/ledge gate - see the design doc, §6 gate 2.
    /// </summary>
    public float MaxVerticalDelta { get; set; } = 5f;

    /// <summary>Minimum spacing between two anchors, so a route spreads over the area.</summary>
    public float MinSpacingYards { get; set; } = 30f;

    /// <summary>
    /// Spacing of the emitted waypoints, in yards.
    ///
    /// <para>The legs between stops are pathed through the navmesh, which returns points
    /// every few yards. Handing those straight to the follower makes it brake at every one,
    /// because each waypoint ends a spline segment - the bot visibly stutters along a
    /// straight stretch. Recorded routes sit around 17 yd apart and that is what the
    /// follower is tuned for, so the leg is resampled to roughly that.</para>
    /// </summary>
    public float WaypointSpacingYards { get; set; } = 15f;

    /// <summary>Per-path override of <see cref="ClassConfiguration.Seed"/>.</summary>
    public int? Seed { get; set; }
}
