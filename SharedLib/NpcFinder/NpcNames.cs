using System;

namespace SharedLib.NpcFinder;

[Flags]
public enum NpcNames
{
    None = 0,
    Enemy = 1,
    Friendly = 2,
    Neutral = 4,
    Corpse = 8,
    NamePlate = 16
}

public static class NpcNames_Extension
{
    public static string ToStringF(this NpcNames value) => value switch
    {
        NpcNames.None => nameof(NpcNames.None),
        NpcNames.Enemy => nameof(NpcNames.Enemy),
        NpcNames.Friendly => nameof(NpcNames.Friendly),
        NpcNames.Neutral => nameof(NpcNames.Neutral),
        NpcNames.Corpse => nameof(NpcNames.Corpse),
        NpcNames.NamePlate => nameof(NpcNames.NamePlate),
        _ => value.ToString(),
    };

    public static bool HasFlagF(this NpcNames value, NpcNames flag)
    {
        return (value & flag) != 0;
    }
}