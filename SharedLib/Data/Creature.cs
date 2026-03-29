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
    public CreatureType Type { get; init; }
}

/// <summary>
/// Creature type values from DB2 CreatureType table.
/// </summary>
public enum CreatureType
{
    None = 0,
    Beast = 1,
    Dragonkin = 2,
    Demon = 3,
    Elemental = 4,
    Giant = 5,
    Undead = 6,
    Humanoid = 7,
    Critter = 8,
    Mechanical = 9,
    NotSpecified = 10,
    Totem = 11,
    NonCombatPet = 12,
    GasCloud = 13
}

public static class CreatureType_Extension
{
    /// <summary>
    /// Check if creature type is valid for Cannibalize (Humanoid or Undead).
    /// </summary>
    public static bool IsCannibalizable(this CreatureType type) =>
        type is CreatureType.Humanoid or CreatureType.Undead;
}