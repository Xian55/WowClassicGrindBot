using Newtonsoft.Json;

using System;

namespace Core;

[Flags]
[JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
public enum UnitClassification
{
    None = 0,
    Normal = 1,
    Trivial = 2,
    Minus = 4,
    Rare = 8,
    Elite = 16,
    RareElite = 32,
    WorldBoss = 64
}

public static class UnitClassification_Extension
{
    public static string ToStringF(this UnitClassification value) => value switch
    {
        UnitClassification.None => nameof(UnitClassification.None),
        UnitClassification.Normal => nameof(UnitClassification.Normal),
        UnitClassification.Trivial => nameof(UnitClassification.Trivial),
        UnitClassification.Minus => nameof(UnitClassification.Minus),
        UnitClassification.Rare => nameof(UnitClassification.Rare),
        UnitClassification.Elite => nameof(UnitClassification.Elite),
        UnitClassification.RareElite => nameof(UnitClassification.RareElite),
        UnitClassification.WorldBoss => nameof(UnitClassification.WorldBoss),
        _ => throw new ArgumentOutOfRangeException()
    };

    public static bool HasFlagF(this UnitClassification value, UnitClassification flag)
    {
        return (value & flag) != 0;
    }
}
