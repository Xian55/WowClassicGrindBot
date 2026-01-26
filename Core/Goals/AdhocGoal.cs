using Core.GOAP;

using Microsoft.Extensions.Logging;

namespace Core.Goals;

public sealed class AdhocGoal : GoapGoal
{
    public override float Cost => key.Cost;

    private readonly ILogger logger;
    private readonly ConfigurableInput input;

    private readonly Wait wait;
    private readonly StopMoving stopMoving;
    private readonly PlayerReader playerReader;

    private readonly KeyAction key;
    private readonly CastingHandler castingHandler;
    private readonly IMountHandler mountHandler;
    private readonly AddonBits bits;
    private readonly CombatLog combatLog;

    private readonly bool? combatMatters;

    public AdhocGoal(KeyAction key, ILogger logger,
        ConfigurableInput input, Wait wait,
        PlayerReader playerReader, StopMoving stopMoving,
        CastingHandler castingHandler, IMountHandler mountHandler,
        AddonBits bits, CombatLog combatLog)
        : base(nameof(AdhocGoal))
    {
        this.logger = logger;
        this.input = input;
        this.wait = wait;
        this.stopMoving = stopMoving;
        this.playerReader = playerReader;
        this.key = key;
        this.castingHandler = castingHandler;
        this.mountHandler = mountHandler;
        this.bits = bits;
        this.combatLog = combatLog;

        if (bool.TryParse(key.InCombat, out bool result))
        {
            AddPrecondition(GoapKey.incombat, result);
            combatMatters = result;
        }

        Keys = [key];
    }

    public override bool CanRun() => key.CanRun();

    public override void OnEnter()
    {
        if (key.BeforeCastDismount && mountHandler.IsMounted())
        {
            mountHandler.Dismount();
        }
    }

    public override void Update()
    {
        wait.Update();

        if (!CanRun() || castingHandler.SpellInQueue())
            return;

        if (key.Charge >= 1 && key.CanRun())
            castingHandler.CastIfReady(key, Interrupt);
    }

    private bool Interrupt()
    {
        return combatMatters.HasValue
            ? combatMatters.Value == bits.Combat() && combatLog.DamageTakenCount() > 0
            : combatLog.DamageTakenCount() > 0;
    }
}
