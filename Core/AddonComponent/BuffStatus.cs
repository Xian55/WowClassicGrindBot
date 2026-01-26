using System.Collections.Specialized;

namespace Core;

public interface IFocus { }

public interface IPlayer { }

public sealed class BuffStatus<T> : IReader
{
    private readonly int cell;
    private BitVector32 v;

    public BuffStatus(int cell)
    {
        this.cell = cell;
    }

    public void Update(IAddonDataProvider reader)
    {
        v = new(reader.GetInt(cell));
    }

    // All
    public bool Food() => v[Mask._0];

    public bool Drink() => v[Mask._1];

    public bool Well_Fed() => v[Mask._2];

    public bool Mana_Regeneration() => v[Mask._3];

    public bool Clearcasting() => v[Mask._4];

    // Priest
    public bool Fortitude() => v[Mask._10];
    public bool Inner_Fire() => v[Mask._11];
    public bool Renew() => v[Mask._12];
    public bool Shield() => v[Mask._13];
    public bool Divine_Spirit() => v[Mask._14];
    public bool Inner_Focus() => v[Mask._15];
    public bool Abolish_Disease() => v[Mask._16];
    public bool Power_Infusion() => v[Mask._17];
    public bool Prayer_of_Shadow_Protection() => v[Mask._18];
    public bool Shadow_Protection() => v[Mask._19];

    // Druid
    public bool Mark_of_the_Wild() => v[Mask._10];
    public bool Thorns() => v[Mask._11];

    [Names(["Tiger's Fury"])]
    public bool Tigers_Fury() => v[Mask._12];
    public bool Prowl() => v[Mask._13];
    public bool Rejuvenation() => v[Mask._14];
    public bool Regrowth() => v[Mask._15];
    public bool Omen_of_Clarity() => v[Mask._16];

    // Paladin
    public bool Seal_of_Righteousness() => v[Mask._5];
    public bool Seal_of_the_Crusader() => v[Mask._6];
    public bool Seal_of_Command() => v[Mask._7];
    public bool Seal_of_Wisdom() => v[Mask._8];
    public bool Seal_of_Light() => v[Mask._9];
    public bool Seal_of_Blood() => v[Mask._10];
    public bool Seal_of_Vengeance() => v[Mask._11];

    public bool Blessing_of_Might() => v[Mask._12];
    public bool Blessing_of_Protection() => v[Mask._13];
    public bool Blessing_of_Wisdom() => v[Mask._14];
    public bool Blessing_of_Kings() => v[Mask._15];
    public bool Blessing_of_Salvation() => v[Mask._16];
    public bool Blessing_of_Sanctuary() => v[Mask._17];
    public bool Blessing_of_Light() => v[Mask._18];

    public bool Righteous_Fury() => v[Mask._19];
    public bool Divine_Protection() => v[Mask._20];
    public bool Avenging_Wrath() => v[Mask._21];
    public bool Holy_Shield() => v[Mask._22];
    public bool Divine_Shield() => v[Mask._23];

    // Mage
    [Names([
        "Frost Armor",
        "Ice Armor",
        "Molten Armor",
        "Mage Armor"])]
    public bool Frost_Armor() => v[Mask._10];
    public bool Arcane_Intellect() => v[Mask._11];
    public bool Ice_Barrier() => v[Mask._12];
    public bool Ward() => v[Mask._13];
    public bool Fire_Power() => v[Mask._14];
    public bool Mana_Shield() => v[Mask._15];
    public bool Presence_of_Mind() => v[Mask._16];
    public bool Arcane_Power() => v[Mask._17];

    // Rogue
    public bool Slice_and_Dice() => v[Mask._10];
    public bool Stealth() => v[Mask._11];

    // Warrior
    public bool Battle_Shout() => v[Mask._10];
    public bool Bloodrage() => v[Mask._11];

    // Warlock
    [Names([
        "Demon Skin",
        "Demon Armor"])]
    public bool Demon_Skin() => v[Mask._10]; //Skin and Armor
    public bool Soul_Link() => v[Mask._11];
    public bool Soulstone_Resurrection() => v[Mask._12];
    public bool Shadow_Trance() => v[Mask._13];
    public bool Fel_Armor() => v[Mask._14];
    public bool Fel_Domination() => v[Mask._15];
    public bool Demonic_Sacrifice() => v[Mask._16];
    public bool Sacrifice() => v[Mask._17];

    // Shaman
    public bool Lightning_Shield() => v[Mask._10];
    public bool Water_Shield() => v[Mask._11];

    [Names(["Shamanistic Focus", "Focused"])]
    public bool Shamanistic_Focus() => v[Mask._12];
    public bool Stoneskin() => v[Mask._13];
    public bool Elemental_Mastery() => v[Mask._14];
    public bool Stormstrike() => v[Mask._15];

    [Names(["Nature's Swiftness"])]
    public bool Natures_Swiftness() => v[Mask._16];

    // Hunter
    public bool Aspect_of_the_Cheetah() => v[Mask._10];
    public bool Aspect_of_the_Pack() => v[Mask._11];
    public bool Aspect_of_the_Hawk() => v[Mask._12];
    public bool Aspect_of_the_Monkey() => v[Mask._13];
    public bool Aspect_of_the_Viper() => v[Mask._14];
    public bool Rapid_Fire() => v[Mask._15];
    public bool Quick_Shots() => v[Mask._16];
    public bool Trueshot_Aura() => v[Mask._17];
    public bool Aspect_of_the_Dragonhawk() => v[Mask._18];
    public bool Lock_and_Load() => v[Mask._19];

    // Death Knight
    public bool Blood_Tap() => v[Mask._10];
    public bool Horn_of_Winter() => v[Mask._11];
    public bool Icebound_Fortitude() => v[Mask._12];
    public bool Path_of_Frost() => v[Mask._13];
    [Names(["Anti-Magic Shell"])]
    public bool Anti_Magic_Shell() => v[Mask._14];
    public bool Army_of_the_Dead() => v[Mask._15];
    public bool Vampiric_Blood() => v[Mask._16];
    public bool Dancing_Rune_Weapon() => v[Mask._17];
    public bool Unbreakable_Armor() => v[Mask._18];
    public bool Bone_Shield() => v[Mask._19];
    public bool Summon_Gargoyle() => v[Mask._20];
    public bool Freezing_Fog() => v[Mask._21];
}