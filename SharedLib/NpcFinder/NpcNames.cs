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