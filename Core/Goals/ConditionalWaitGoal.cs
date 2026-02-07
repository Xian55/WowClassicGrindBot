using Microsoft.Extensions.Logging;

namespace Core.Goals;

public sealed class ConditionalWaitGoal : GoapGoal
{
    public override float Cost => Keys[0].Cost;

    private readonly ILogger<ConditionalWaitGoal> logger;
    private readonly Wait wait;

    public ConditionalWaitGoal(KeyAction keyAction,
        ILogger<ConditionalWaitGoal> logger, Wait wait)
        : base(nameof(ConditionalWaitGoal))
    {
        this.logger = logger;
        this.wait = wait;

        Keys = [keyAction];
    }

    public override bool CanRun() => Keys[0].CanRun();

    public override void OnEnter()
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Waiting for {Name}", Keys[0].Name);
    }

    public override void Update()
    {
        wait.Update();
    }
}