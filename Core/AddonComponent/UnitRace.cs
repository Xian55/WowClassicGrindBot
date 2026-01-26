namespace Core;

public enum UnitRace
{
    None,
    Human,
    Orc,
    Dwarf,
    NightElf,
    Undead,
    Tauren,
    Gnome,
    Troll,
    Goblin,
    BloodElf,
    Draenei,
    Worgen = 22
}

public static class UnitRace_Extension
{
    public static string ToStringF(this UnitRace value) => value switch
    {
        UnitRace.None => nameof(UnitRace.None),
        UnitRace.Human => nameof(UnitRace.Human),
        UnitRace.Orc => nameof(UnitRace.Orc),
        UnitRace.Dwarf => nameof(UnitRace.Dwarf),
        UnitRace.NightElf => nameof(UnitRace.NightElf),
        UnitRace.Undead => nameof(UnitRace.Undead),
        UnitRace.Tauren => nameof(UnitRace.Tauren),
        UnitRace.Gnome => nameof(UnitRace.Gnome),
        UnitRace.Troll => nameof(UnitRace.Troll),
        UnitRace.Goblin => nameof(UnitRace.Goblin),
        UnitRace.BloodElf => nameof(UnitRace.BloodElf),
        UnitRace.Draenei => nameof(UnitRace.Draenei),
        UnitRace.Worgen => nameof(UnitRace.Worgen),
        _ => nameof(UnitRace.None)
    };
}