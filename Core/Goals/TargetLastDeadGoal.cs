using Core.GOAP;

using Microsoft.Extensions.Logging;

namespace Core.Goals;

public sealed class TargetLastDeadGoal : GoapGoal
{
    public override float Cost => 4.2f;

    private readonly ILogger logger;
    private readonly ConfigurableInput input;

    public TargetLastDeadGoal(ILogger logger, ConfigurableInput input)
        : base(nameof(TargetLastDeadGoal))
    {
        this.logger = logger;
        this.input = input;

        AddPrecondition(GoapKey.hastarget, false);
        AddPrecondition(GoapKey.producedcorpse, true);
    }

    public override void Update()
    {
        input.PressLastTarget();
    }
}