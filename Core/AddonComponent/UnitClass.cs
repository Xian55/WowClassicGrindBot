namespace Core;

public enum UnitClass
{
    None,
    Warrior,
    Paladin,
    Hunter,
    Rogue,
    Priest,
    DeathKnight,
    Shaman,
    Mage,
    Warlock,
    Monk,
    Druid,
    DemonHunter
}

public static class UnitClassExtensions
{
    /// <summary>
    /// The text a class trainer carries in <c>Creature.SubName</c> - "Warrior Trainer",
    /// "Priest Trainer" and so on. Used to keep a ClassTrainer search from walking to
    /// another class's trainer, since NpcFlags.ClassTrainer marks all of them alike.
    /// <para>
    /// The enum name is the keyword. Matching is a case-insensitive substring test, so
    /// it also picks up the hand-titled ones - "High Priest", "Master Mage",
    /// "Grand Master Rogue" - while excluding "Portal Trainer" and "Pet Trainer", which
    /// teach no class spells.
    /// </para>
    /// <para>
    /// Covers every class trainer in som and tbc, and all but the non-spell ones in
    /// wrath. NOT wired up for DeathKnight or DemonHunter: the data spells those two
    /// inconsistently ("Death Knight" in wrath, "Deathknight" in MoP) and wrath does
    /// not flag its DK trainers as ClassTrainer at all.
    /// </para>
    /// </summary>
    public static string TrainerSubName(this UnitClass unitClass) =>
        unitClass == UnitClass.None ? string.Empty : unitClass.ToString();
}