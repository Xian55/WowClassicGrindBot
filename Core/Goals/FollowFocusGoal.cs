using Core.GOAP;

namespace Core.Goals;

public sealed class FollowFocusGoal : GoapGoal
{
    public override float Cost => 19f;

    private readonly ConfigurableInput input;
    private readonly PlayerReader playerReader;
    private readonly AddonBits bits;
    private readonly Wait wait;

    public FollowFocusGoal(ConfigurableInput input,
        PlayerReader playerReader,
        AddonBits bits,
        Wait wait)
        : base(nameof(FollowFocusGoal))
    {
        this.input = input;
        this.playerReader = playerReader;
        this.bits = bits;
        this.wait = wait;

        AddPrecondition(GoapKey.hasfocus, true);
        AddPrecondition(GoapKey.dangercombat, false);
        AddPrecondition(GoapKey.damagedone, false);
        AddPrecondition(GoapKey.damagetaken, false);
        AddPrecondition(GoapKey.producedcorpse, false);
        AddPrecondition(GoapKey.consumecorpse, false);
    }

    public override void OnEnter()
    {
        if (input.IsKeyDown(input.ForwardKey))
        {
            input.StopForward(true);
        }
    }

    public override void OnExit()
    {
        if (playerReader.TargetGuid == playerReader.FocusGuid)
        {
            input.PressClearTarget();
            wait.Update();
        }
    }

    public override void Update()
    {
        if (playerReader.TargetGuid != playerReader.FocusGuid)
        {
            input.PressTargetFocus();
            wait.Update();
        }

        if (playerReader.TargetGuid == playerReader.FocusGuid &&
            playerReader.SpellInRange.Focus_Inspect &&
            !bits.AutoFollow() &&
            !input.FollowTarget.OnCooldown())
        {
            input.PressFollowTarget();
        }

        wait.Update();
    }
}
