using Core.GOAP;

using Microsoft.Extensions.Logging;

namespace Core.Goals;

public sealed partial class ConsumeCorpseGoal : GoapGoal
{
    public override float Cost => 4.1f;

    private readonly ILogger<ConsumeCorpseGoal> logger;
    private readonly ClassConfiguration classConfig;
    private readonly GoapAgentState state;

    public ConsumeCorpseGoal(ILogger<ConsumeCorpseGoal> logger,
        ClassConfiguration classConfig, GoapAgentState state)
        : base(nameof(ConsumeCorpseGoal))
    {
        this.logger = logger;
        this.classConfig = classConfig;
        this.state = state;

        if (classConfig.KeyboardOnly)
        {
            AddPrecondition(GoapKey.consumablecorpsenearby, true);
        }
        AddPrecondition(GoapKey.damagedone, false);
        AddPrecondition(GoapKey.damagetaken, false);

        AddPrecondition(GoapKey.producedcorpse, true);
        AddPrecondition(GoapKey.consumecorpse, false);

        AddEffect(GoapKey.producedcorpse, false);
        AddEffect(GoapKey.consumecorpse, true);

        if (classConfig.Loot)
        {
            AddEffect(GoapKey.shouldloot, true);

            if (classConfig.GatherCorpse)
            {
                AddEffect(GoapKey.shouldgather, true);
            }
        }
    }

    public override void OnEnter()
    {
        LogConsume(logger);
        SendGoapEvent(new GoapStateEvent(GoapKey.consumecorpse, true));

        if (classConfig.Loot)
        {
            state.LootableCorpseCount++;
        }
    }

    [LoggerMessage(
        EventId = 0100,
        Level = LogLevel.Information,
        Message = "Safe to consume a corpse.")]
    static partial void LogConsume(ILogger logger);
}
