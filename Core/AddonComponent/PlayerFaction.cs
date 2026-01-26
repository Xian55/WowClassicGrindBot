using System;

namespace Core;

public enum PlayerFaction
{
    Alliance,
    Horde
}

public static class PlayerFaction_Extension
{
    public static string ToStringF(this PlayerFaction value) => value switch
    {
        PlayerFaction.Alliance => nameof(PlayerFaction.Alliance),
        PlayerFaction.Horde => nameof(PlayerFaction.Horde),
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };
}