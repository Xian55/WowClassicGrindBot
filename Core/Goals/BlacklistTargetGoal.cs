namespace Core.Goals;

public sealed class BlacklistTargetGoal : GoapGoal
{
    public override float Cost => 2;

    private readonly PlayerReader playerReader;
    private readonly AddonBits bits;
    private readonly ConfigurableInput input;
    private readonly Wait wait;
    private readonly IBlacklist targetBlacklist;

    public BlacklistTargetGoal(PlayerReader playerReader,
        AddonBits bits,
        ConfigurableInput input,
        IBlacklist blacklist,
        Wait wait)
        : base(nameof(BlacklistTargetGoal))
    {
        this.playerReader = playerReader;
        this.bits = bits;
        this.input = input;
        this.targetBlacklist = blacklist;
        this.bits = bits;
        this.wait = wait;
    }

    public override bool CanRun()
    {
        return bits.Target() && targetBlacklist.Is();
    }

    public override void OnEnter()
    {
        if (playerReader.PetTarget() ||
            playerReader.IsCasting() ||
            bits.Any_AutoAttack())
        {
            input.PressStopAttack();
        }

        input.PressClearTarget();
        wait.Update();
    }
}
