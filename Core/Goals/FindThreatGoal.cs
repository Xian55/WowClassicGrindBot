using Core.GOAP;

namespace Core.Goals;

/// <summary>
/// Owns the "in combat, being hit, nothing alive targeted" gap.
///
/// <para>
/// Before this goal existed nothing could be planned in that state:
/// <see cref="CombatGoal"/>, <see cref="ApproachTargetGoal"/> and
/// <see cref="PullTargetGoal"/> all need <c>targetisalive</c>;
/// <see cref="ConsumeCorpseGoal"/>, <see cref="LootGoal"/>,
/// <see cref="CorpseConsumedGoal"/>, FollowRoute, Parallel and WrongZone are all
/// gated on being out of combat or out of danger. The result was the long
/// standing <c>NO PLAN</c> idle while a second mob beat the character to death -
/// issues #795, #823.
/// </para>
///
/// <para>
/// The gate is <c>dangercombat</c> (in combat AND <see cref="CombatLog.DamageTaken"/>
/// non-empty), not <c>incombat</c>. Something is actually attacking us, which is
/// exactly the window where every other goal is already blocked - so this cannot
/// preempt the normal post-kill loot sequence.
/// </para>
/// </summary>
public sealed class FindThreatGoal : GoapGoal
{
    // Loses to TargetPetTargetGoal (4.01f) so the pet path keeps priority while
    // it can run; this is the fallback for no pet, a passive pet, or a pet
    // target that just died. Mutually exclusive with CombatGoal (4f) via
    // targetisalive, so the ordering between those two never matters.
    public override float Cost => 4.02f;

    private readonly ThreatFinder threatFinder;

    // The same array CombatGoal drives, so a target acquired here resets the
    // per-target cooldowns exactly as it would on CombatGoal's own recovery path.
    private readonly KeyAction[] combatSequence;

    public FindThreatGoal(ThreatFinder threatFinder,
        ClassConfiguration classConfig)
        : base(nameof(FindThreatGoal))
    {
        this.threatFinder = threatFinder;
        this.combatSequence = classConfig.Combat.Sequence;

        AddPrecondition(GoapKey.dangercombat, true);
        AddPrecondition(GoapKey.targetisalive, false);

        AddEffect(GoapKey.hastarget, true);
        AddEffect(GoapKey.targetisalive, true);
    }

    public override void Update()
    {
        threatFinder.FindPossibleThreats(combatSequence);
    }
}
