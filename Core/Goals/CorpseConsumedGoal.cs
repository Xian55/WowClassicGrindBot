using Core.GOAP;

using Microsoft.Extensions.Logging;

using System;

namespace Core.Goals;

public sealed partial class CorpseConsumedGoal : GoapGoal
{
    public override float Cost => 4.7f;

    private readonly ILogger<CorpseConsumedGoal> logger;
    private readonly GoapAgentState goapAgentState;
    private readonly Wait wait;

    private readonly bool lootEnabled;

    public CorpseConsumedGoal(ILogger<CorpseConsumedGoal> logger,
        ClassConfiguration classConfig, GoapAgentState goapAgentState, Wait wait)
        : base(nameof(CorpseConsumedGoal))
    {
        this.logger = logger;
        this.goapAgentState = goapAgentState;
        this.wait = wait;

        this.lootEnabled = classConfig.Loot;

        if (classConfig.KeyboardOnly)
        {
            AddPrecondition(GoapKey.consumablecorpsenearby, true);
        }
        AddPrecondition(GoapKey.pulled, false);
        AddPrecondition(GoapKey.dangercombat, false);
        AddPrecondition(GoapKey.incombat, false);

        AddPrecondition(GoapKey.consumecorpse, true);

        AddEffect(GoapKey.consumecorpse, false);
    }

    public override void OnEnter()
    {
        goapAgentState.ConsumableCorpseCount = Math.Max(goapAgentState.ConsumableCorpseCount - 1, 0);
        if (goapAgentState.ConsumableCorpseCount == 0)
        {
            goapAgentState.LastCombatKillCount = 0;
            goapAgentState.RecentlyLooted.Clear();
        }

        LogConsumed(logger, goapAgentState.LastCombatKillCount, goapAgentState.ConsumableCorpseCount);

        SendGoapEvent(new GoapStateEvent(GoapKey.consumecorpse, false));

        if (goapAgentState.LastCombatKillCount > 1)
        {
            wait.Fixed(Loot.LOOTFRAME_AUTOLOOT_DELAY_MS);
            wait.Update();
        }

        if (!lootEnabled)
        {
            SendGoapEvent(new RemoveClosestPoi(CorpseEvent.NAME));
            wait.Fixed(Loot.LOOTFRAME_AUTOLOOT_DELAY_MS / 2);
        }
    }

    [LoggerMessage(
        EventId = 0120,
        Level = LogLevel.Information,
        Message = "Total: {total} | Remaining: {remains}")]
    static partial void LogConsumed(ILogger logger, int total, int remains);
}
