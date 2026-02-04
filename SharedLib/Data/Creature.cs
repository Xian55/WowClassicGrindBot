using SharedLib.Data;

namespace SharedLib;

public readonly record struct Creature
{
    public int Entry { get; init; }
    public string Name { get; init; }
    public string SubName { get; init; }
    public int Faction { get; init; }
    public int MinLevel { get; init; }
    public int MaxLevel { get; init; }
    public int Rank { get; init; }
    public NpcFlags NpcFlag { get; init; }
    public int SkinLoot { get; init; }
    public int Family { get; init; }
    public int Type { get; init; }
}

/// <summary>
/// Creature type constants from WoW database.
/// </summary>
public static class CreatureTypes
{
    public const int Beast = 1;
    public const int Dragonkin = 2;
    public const int Demon = 3;
    public const int Elemental = 4;
    public const int Giant = 5;
    public const int Undead = 6;
    public const int Humanoid = 7;
    public const int Critter = 8;
    public const int Mechanical = 9;
    public const int NotSpecified = 10;
    public const int Totem = 11;
    public const int NonCombatPet = 12;
    public const int GasCloud = 13;

    /// <summary>
    /// Check if creature type is valid for Cannibalize (Humanoid or Undead).
    /// </summary>
    public static bool IsCannibalizable(int type) => type is Humanoid or Undead;
}