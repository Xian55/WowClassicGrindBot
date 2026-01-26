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