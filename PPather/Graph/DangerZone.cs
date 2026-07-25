namespace PPather.Graph;

/// <summary>
/// How a danger zone affects routing.
/// <see cref="Avoid"/> prices the area up so a path crosses it only when there
/// is no reasonable alternative; <see cref="Block"/> makes it impassable, which
/// can leave a destination unreachable - that is the point, but draw carefully.
/// </summary>
public enum DangerZoneMode
{
    Avoid = 0,
    Block
}

public readonly record struct CircleDangerZone(
    string Name, float CenterX, float CenterY, float Radius, float Penalty)
{
    public DangerZoneMode Mode { get; init; }
}

public readonly record struct RectangleDangerZone(
    string Name, float MinX, float MinY, float MaxX, float MaxY, float Penalty)
{
    public DangerZoneMode Mode { get; init; }
}
