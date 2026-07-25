namespace PPather.Graph;

public sealed class DangerZoneData
{
    public CircleDangerZone[] Circles { get; init; } = [];
    public RectangleDangerZone[] Rectangles { get; init; } = [];
}
