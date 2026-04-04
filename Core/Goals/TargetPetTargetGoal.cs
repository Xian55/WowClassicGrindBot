using Core.GOAP;

namespace Core.Goals;

public sealed class TargetPetTargetGoal : GoapGoal
{
    public override float Cost => 4.01f;

    private readonly ConfigurableInput input;
    private readonly PlayerReader playerReader;
    private readonly AddonBits bits;
    private readonly CombatLog combatLog;
    private readonly Wait wait;

    public TargetPetTargetGoal(ConfigurableInput input,
        PlayerReader playerReader, AddonBits bits,
        CombatLog combatLog, Wait wait)
        : base(nameof(TargetPetTargetGoal))
    {
        this.input = input;
        this.playerReader = playerReader;
        this.bits = bits;
        this.combatLog = combatLog;
        this.wait = wait;

        AddPrecondition(GoapKey.targetisalive, false);

        if (input.KeyboardOnly)
        {
            AddPrecondition(GoapKey.consumablecorpsenearby, false);
        }

        AddPrecondition(GoapKey.pethastarget, true);

        AddEffect(GoapKey.hastarget, true);
    }

    public override bool CanRun()
    {
        return playerReader.PetAlive() &&
            bits.Pet_Defensive() &&
            bits.PetTarget_Alive() &&
            !combatLog.RecentlyDead.Contains(playerReader.PetTargetGuid);
    }

    public override void Update()
    {
        input.PressTargetPet();
        wait.Update();
        input.PressTargetOfTarget();
        wait.Update();

        if (bits.Target() &&
            (bits.Target_Dead() || playerReader.TargetGuid == playerReader.PetGuid))
        {
            input.PressClearTarget();
            wait.Update();
        }
    }
}
