using Core.GOAP;

namespace Core.Goals;

public sealed class TargetFocusTargetGoal : GoapGoal
{
    public override float Cost => 10f;

    private readonly ConfigurableInput input;
    private readonly PlayerReader playerReader;
    private readonly AddonBits bits;
    private readonly Wait wait;

    public TargetFocusTargetGoal(ConfigurableInput input, PlayerReader playerReader,
        AddonBits bits, ClassConfiguration classConfig, Wait wait)
        : base(nameof(TargetFocusTargetGoal))
    {
        this.input = input;
        this.playerReader = playerReader;
        this.bits = bits;
        this.wait = wait;

        if (classConfig.Loot)
        {
            AddPrecondition(GoapKey.incombat, false);
        }

        AddPrecondition(GoapKey.hasfocus, true);
        AddPrecondition(GoapKey.focushastarget, true);
    }

    public override bool CanRun()
    {
        if (bits.TargetTarget_PlayerOrPet())
            return false;

        return
            !bits.TargetTarget_PlayerOrPet() &&
            ((bits.FocusTarget_Hostile() && bits.FocusTarget_Combat()) ||
            !bits.FocusTarget_Hostile() ||
            CanPull());
    }

    public override void OnEnter()
    {
        input.PressTargetFocus();
        wait.Update();
    }

    public override void Update()
    {
        if (bits.FocusTarget_Hostile() || playerReader.IsTargetCasting())
        {
            if (bits.FocusTarget_Combat() || CanPull())
            {
                input.PressTargetFocus();
                input.PressTargetOfTarget();
            }
        }
        else if (playerReader.SpellInRange.FocusTarget_Trade)
        {
            input.PressTargetFocus();
            input.PressTargetOfTarget();
            input.PressInteract();
        }

        wait.Update();
    }

    public override void OnExit()
    {
        if (!bits.FocusTarget())
        {
            input.PressClearTarget();
            wait.Update();
        }
    }

    private bool CanPull()
    {
        return bits.FocusTarget() && playerReader.IsTargetCasting();
    }
}
