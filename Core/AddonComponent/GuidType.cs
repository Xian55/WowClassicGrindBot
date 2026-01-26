namespace Core;

public enum GuidType
{
    Unknown,
    Creature,
    Pet,
    GameObject,
    Vehicle
}


public static class GuidType_Extensions
{
    public static string ToStringF(this GuidType value) => value switch
    {
        GuidType.Unknown => nameof(GuidType.Unknown),
        GuidType.Creature => nameof(GuidType.Creature),
        GuidType.Pet => nameof(GuidType.Pet),
        GuidType.GameObject => nameof(GuidType.GameObject),
        GuidType.Vehicle => nameof(GuidType.Vehicle),
        _ => throw new System.ArgumentOutOfRangeException()
    };
}