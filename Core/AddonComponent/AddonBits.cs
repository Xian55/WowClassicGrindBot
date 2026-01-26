using System.Collections.Specialized;

namespace Core;

public sealed class AddonBits : IReader, IGameMenuWindowShown
{
    private const int cell1 = 8;
    private const int cell2 = 9;
    private const int cell3 = 100;

    private BitVector32 v1;
    private BitVector32 v2;
    private BitVector32 v3;

    public AddonBits() { }

    public void Update(IAddonDataProvider reader)
    {
        v1 = new(reader.GetInt(cell1));
        v2 = new(reader.GetInt(cell2));
        v3 = new(reader.GetInt(cell3));
    }

    // -- value1 based flags
    public bool Target_Combat() => v1[Mask._0];
    public bool Target_Dead() => v1[Mask._1];
    public bool Target_NotDead() => !v1[Mask._1];
    public bool Target_Alive() => Target() && Target_NotDead();
    public bool Dead() => v1[Mask._2];
    public bool TalentPoint() => v1[Mask._3];
    public bool MouseOver() => v1[Mask._4];
    public bool Target_Hostile() => v1[Mask._5];
    public bool Pet() => v1[Mask._6];
    public bool MainHandTempEnchant() => v1[Mask._7];
    public bool OffHandTempEnchant() => v1[Mask._8];
    public bool Items_Broken() => v1[Mask._9];
    public bool OnTaxi() => v1[Mask._10];
    public bool Swimming() => v1[Mask._11];
    public bool Pet_Happy() => v1[Mask._12];
    public bool Ammo() => v1[Mask._13];
    public bool Combat() => v1[Mask._14];
    public bool TargetTarget_PlayerOrPet() => v1[Mask._15];
    public bool AutoShot() => v1[Mask._16];
    public bool NoTarget() => !v1[Mask._17];
    public bool Target() => v1[Mask._17];
    public bool Mounted() => v1[Mask._18];
    public bool Shoot() => v1[Mask._19];
    public bool Auto_Attack() => v1[Mask._20];
    public bool Target_Player() => v1[Mask._21];
    public bool Target_Tagged() => v1[Mask._22];
    public bool Falling() => v1[Mask._23];

    // -- value2 based flags
    public bool Drowning() => v2[Mask._0];
    public bool CorpseInRange() => v2[Mask._1];
    public bool Indoors() => v2[Mask._2];
    public bool Focus() => v2[Mask._3];
    public bool Focus_Combat() => v2[Mask._4];
    public bool FocusTarget() => v2[Mask._5];
    public bool FocusTarget_Combat() => v2[Mask._6];
    public bool FocusTarget_Hostile() => v2[Mask._7];
    public bool MouseOver_Dead() => v2[Mask._8];
    public bool PetTarget_Dead() => v2[Mask._9];
    public bool PetTarget_Alive() => !PetTarget_Dead();
    public bool Stealthed() => v2[Mask._10];
    public bool Target_Trivial() => v2[Mask._11];
    public bool Target_NotTrivial() => !v2[Mask._11];
    public bool MouseOver_Trivial() => v2[Mask._12];
    public bool MouseOver_NotTrivial() => !v2[Mask._12];
    public bool MouseOver_Tagged() => v2[Mask._13];
    public bool MouseOver_Hostile() => v2[Mask._14];
    public bool MouseOver_Player() => v2[Mask._15];
    public bool MouseOverTarget_PlayerOrPet() => v2[Mask._16];
    public bool MouseOver_PlayerControlled() => v2[Mask._17];
    public bool Target_PlayerControlled() => v2[Mask._18];
    public bool AutoFollow() => v2[Mask._19];
    public bool GameMenuWindowShown() => v2[Mask._20];
    public bool Flying() => v2[Mask._21];
    public bool Moving() => v2[Mask._22];
    public bool Pet_Defensive() => v2[Mask._23];

    public bool NotMoving() => !Moving();

    // Combined

    public bool Grounded() => !Flying() && !Falling();

    public bool Any_AutoAttack() => AutoShot() || Auto_Attack() || Shoot();

    // -- value3 based flags
    public bool SoftInteract() => v3[Mask._0];

    public bool SoftInteract_Dead() => v3[Mask._1];

    public bool SoftInteract_DeadOrGhost() => v3[Mask._2];

    public bool SoftInteract_Player() => v3[Mask._3];

    public bool SoftInteract_Tagged() => v3[Mask._4];

    public bool SoftInteract_Combat() => v3[Mask._5];

    public bool SoftInteract_Hostile() => v3[Mask._6];

    public bool Channeling() => v3[Mask._7];

    public bool LootFrameShown() => v3[Mask._8];

    public bool ChatInputIsVisible() => v3[Mask._9];

    public bool SoftInteract_Enabled() => v3[Mask._10];
}