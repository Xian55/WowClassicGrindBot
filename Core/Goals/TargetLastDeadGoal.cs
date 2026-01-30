using Core.GOAP;

using Microsoft.Extensions.Logging;

namespace Core.Goals;

public sealed class TargetLastDeadGoal : GoapGoal
{
    public override float Cost => 4.2f;

    private readonly ILogger logger;
    private readonly ConfigurableInput input;
    private readonly Wait wait;
    private readonly AddonBits bits;

    public TargetLastDeadGoal(ILogger logger, ConfigurableInput input,
        Wait wait, AddonBits bits)
        : base(nameof(TargetLastDeadGoal))
    {
        this.logger = logger;
        this.input = input;
        this.wait = wait;
        this.bits = bits;

        AddPrecondition(GoapKey.hastarget, false);
        AddPrecondition(GoapKey.producedcorpse, true);
    }

    public override void Update()
    {
        input.PressLastTargetAndWait(wait, bits.Target);
    }
}