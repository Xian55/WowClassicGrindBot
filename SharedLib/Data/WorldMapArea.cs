namespace SharedLib;

public readonly record struct WorldMapArea
{
    public int MapID { get; init; }
    public int AreaID { get; init; }
    public string AreaName { get; init; }
    public float LocLeft { get; init; }
    public float LocRight { get; init; }
    public float LocTop { get; init; }
    public float LocBottom { get; init; }
    public int UIMapId { get; init; }
    public string Continent { get; init; }
    public int ParentAreaId { get; init; }
    public int ExplorationLevel { get; init; }

    public float ToWorldX(float value)
    {
        return ((LocBottom - LocTop) * value / 100f) + LocTop;
    }

    public float ToWorldY(float value)
    {
        return ((LocRight - LocLeft) * value / 100f) + LocLeft;
    }

    public float ToMapX(float value)
    {
        return 100f - (((value - LocBottom) * 100f) / (LocTop - LocBottom));
    }

    public float ToMapY(float value)
    {
        return 100f - (((value - LocRight) * 100f) / (LocLeft - LocRight));
    }

}
