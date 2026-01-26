namespace Core;

public sealed class BlacklistMouseOver : IBlacklistSource
{
    private readonly PlayerReader playerReader;
    private readonly AddonReader addonReader;
    private readonly AddonBits bits;

    public BlacklistMouseOver(
        PlayerReader playerReader,
        AddonReader addonReader,
        AddonBits bits)
    {
        this.playerReader = playerReader;
        this.addonReader = addonReader;
        this.bits = bits;
    }

    public int UnitGuid => playerReader.MouseOverGuid;
    public int UnitId => playerReader.MouseOverId;
    public string UnitName => addonReader.MouseOverName;
    public int UnitLevel => playerReader.MouseOverLevel;
    public UnitClassification UnitClassification => playerReader.MouseOverClassification;

    public bool Exists() => bits.MouseOver();
    public bool UnitTarget_PlayerOrPet() => bits.MouseOverTarget_PlayerOrPet();
    public bool Unit_Dead() => bits.MouseOver_Dead();
    public bool Unit_Hostile() => bits.MouseOver_Hostile();
    public bool Unit_Player() => bits.MouseOver_Player();
    public bool Unit_PlayerControlled() => bits.MouseOver_PlayerControlled();
    public bool Unit_Tagged() => bits.MouseOver_Tagged();
    public bool Unit_Trivial() => bits.MouseOver_Trivial();
}
