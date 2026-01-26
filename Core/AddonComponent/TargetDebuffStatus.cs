using System.Collections.Specialized;

namespace Core;

public sealed class TargetDebuffStatus : IReader
{
    private const int cell = 42;

    private BitVector32 v;

    public TargetDebuffStatus() { }

    public void Update(IAddonDataProvider reader)
    {
        v = new(reader.GetInt(cell));
    }

    public override string ToString()
    {
        return string.Empty;
    }

    // Priest
    [Names(["Shadow Word: Pain"])]
    public bool Shadow_Word_Pain() => v[Mask._0];
    public bool Holy_Fire() => v[Mask._1];
    public bool Vampiric_Embrace() => v[Mask._2];
    public bool Silence() => v[Mask._3];
    public bool Shackle_Undead() => v[Mask._4];

    // Druid
    public bool Demoralizing_Roar() => v[Mask._0];
    public bool Faerie_Fire() => v[Mask._1];
    public bool Rip() => v[Mask._2];
    public bool Moonfire() => v[Mask._3];
    public bool Entangling_Roots() => v[Mask._4];
    public bool Rake() => v[Mask._5];

    // Paladin
    public bool Judgement_of_the_Crusader() => v[Mask._0];
    public bool Hammer_of_Justice() => v[Mask._1];
    public bool Judgement_of_Any() => Judgement_of_Wisdom() || Judgement_of_Light() || Judgement_of_Justice();
    public bool Judgement_of_Wisdom() => v[Mask._2];
    public bool Judgement_of_Light() => v[Mask._3];
    public bool Judgement_of_Justice() => v[Mask._4];

    // Mage
    public bool Frostbite() => v[Mask._0];
    public bool Slow() => v[Mask._1];

    // Rogue

    // Warrior
    public bool Rend() => v[Mask._0];
    public bool Thunder_Clap() => v[Mask._1];
    public bool Hamstring() => v[Mask._2];
    public bool Charge_Stun() => v[Mask._3];

    // Warlock
    [Names([
        "Curse of Weakness",
        "Curse of Elements",
        "Curse of Recklessness",
        "Curse of Shadow",
        "Curse of Agony",
        "Curse of"])]
    public bool Curse_of() => v[Mask._0];
    public bool Corruption() => v[Mask._1];
    public bool Immolate() => v[Mask._2];
    public bool Siphon_Life() => v[Mask._3];

    // Hunter
    public bool Serpent_Sting() => v[Mask._0];
    [Names(["Hunter's Mark"])]
    public bool Hunters_Mark() => v[Mask._1];
    public bool Viper_Sting() => v[Mask._2];
    public bool Explosive_Shot() => v[Mask._3];
    public bool Black_Arrow() => v[Mask._4];

    // Death Knight
    public bool Blood_Plague() => v[Mask._0];
    public bool Frost_Fever() => v[Mask._1];
    public bool Strangulate() => v[Mask._2];
    public bool Chains_of_Ice() => v[Mask._3];
}