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
    public static bool HasFlagF(this UnitClassification value, UnitClassification flag)
    {
        return (value & flag) != 0;
    }
}
