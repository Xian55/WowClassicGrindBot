namespace Core;

public sealed class BlacklistTarget : IBlacklistSource
{
    private readonly PlayerReader playerReader;
    private readonly AddonReader addonReader;
    private readonly AddonBits bits;

    public BlacklistTarget(
        PlayerReader playerReader,
        AddonReader addonReader,
        AddonBits bits)
    {
        this.playerReader = playerReader;
        this.addonReader = addonReader;
        this.bits = bits;
    }

    public int UnitGuid => playerReader.TargetGuid;
    public int UnitId => playerReader.TargetId;
    public string UnitName => addonReader.TargetName;
    public int UnitLevel => playerReader.TargetLevel;
    public UnitClassification UnitClassification => playerReader.TargetClassification;

    public bool Exists() => bits.Target();
    public bool UnitTarget_PlayerOrPet() => bits.TargetTarget_PlayerOrPet();
    public bool Unit_Dead() => bits.Target_Dead();
    public bool Unit_Hostile() => bits.Target_Hostile();
    public bool Unit_Player() => bits.Target_Player();
    public bool Unit_PlayerControlled() => bits.Target_PlayerControlled();
    public bool Unit_Tagged() => bits.Target_Tagged();
    public bool Unit_Trivial() => bits.Target_Trivial();
}
