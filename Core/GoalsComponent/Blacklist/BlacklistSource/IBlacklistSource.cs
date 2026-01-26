namespace Core;

public interface IBlacklistSource
{
    int UnitGuid { get; }
    int UnitId { get; }
    string UnitName { get; }
    int UnitLevel { get; }

    UnitClassification UnitClassification { get; }

    bool Exists();
    bool UnitTarget_PlayerOrPet();
    bool Unit_Player();
    bool Unit_PlayerControlled();
    bool Unit_Dead();
    bool Unit_Tagged();
    bool Unit_Hostile();
    bool Unit_Trivial();
}