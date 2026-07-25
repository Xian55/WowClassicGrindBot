namespace SharedLib;

/// <summary>
/// Tunables for the WASD spline follower, bound from configuration (section
/// <see cref="Position"/>). The defaults match the values tuned against the live
/// game. With <see cref="Enabled"/> off, Navigation uses the legacy waypoint
/// follower and behaves exactly as before the spline follower existed.
/// </summary>
public sealed class SplineFollowerOptions
{
    public const string Position = "SplineFollower";

    /// <summary>Master switch enabling the spline follower.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Validation fallback: steer with the legacy map-space heading formula
    /// instead of the world-space one, in case a client disagrees with the
    /// derived world convention.
    /// </summary>
    public bool UseMapHeading { get; set; }

    // --- Steering -------------------------------------------------------

    /// <summary>Start holding a turn key when |heading error| exceeds this (rad). ~8 degrees.</summary>
    public float DeadbandOn { get; set; } = 0.14f;

    /// <summary>Release the turn key when |heading error| falls under this (rad). ~3 degrees.</summary>
    public float DeadbandOff { get; set; } = 0.052f;

    /// <summary>Seconds of travel the steering target sits ahead of the player, outdoors.</summary>
    public float TauSteerOutdoor { get; set; } = 0.6f;

    /// <summary>Seconds of travel the steering target sits ahead of the player, indoors.</summary>
    public float TauSteerIndoor { get; set; } = 0.35f;

    public float LookAheadMinOutdoor { get; set; } = 3f;
    public float LookAheadMaxOutdoor { get; set; } = 15f;
    public float LookAheadMinIndoor { get; set; } = 1.2f;
    public float LookAheadMaxIndoor { get; set; } = 4f;

    // --- Curvature brake (stop-and-turn) --------------------------------

    /// <summary>Seconds of travel the brake looks ahead for upcoming heading change.</summary>
    public float BrakeWindowSeconds { get; set; } = 0.8f;

    /// <summary>Brake engages when required turn rate exceeds this fraction of the measured rate.</summary>
    public float BrakeSafety { get; set; } = 0.85f;

    /// <summary>Brake releases when required turn rate falls under this fraction (hysteresis).</summary>
    public float BrakeRelease { get; set; } = 0.70f;

    /// <summary>Minimum ms between forward key state flips, so the brake cannot chatter.</summary>
    public float ForwardMinHoldMs { get; set; } = 150f;

    // --- Off-path / projection ------------------------------------------

    /// <summary>Outdoor off-path distance floor, yards (scaled up by measured spacing).</summary>
    public float OffPathOutdoor { get; set; } = 4f;

    /// <summary>Indoor off-path distance, yards.</summary>
    public float OffPathIndoor { get; set; } = 3f;

    /// <summary>Assumed input delivery latency, ms - part of the predictive turn release.</summary>
    public float InputLatencyMs { get; set; } = 30f;
}
