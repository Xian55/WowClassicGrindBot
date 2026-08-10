using Core.GOAP;

using Microsoft.Extensions.Logging;

using SharedLib.NpcFinder;

using System;

using static System.Diagnostics.Stopwatch;

namespace Core.Goals;

public sealed class PullTargetGoal : GoapGoal, IGoapEventListener
{
    public override float Cost => 7f;

    private const int AcquireTargetTimeMs = 5000;
    private const int MAX_PULL_DURATION = 15_000;

    private readonly ILogger<PullTargetGoal> logger;
    private readonly ConfigurableInput input;
    private readonly ClassConfiguration classConfig;
    private readonly Wait wait;
    private readonly CombatLog combatLog;
    private readonly PlayerReader playerReader;
    private readonly AddonBits bits;
    private readonly StopMoving stopMoving;
    private readonly StuckDetector stuckDetector;
    private readonly NpcNameTargeting npcNameTargeting;
    private readonly CastingHandler castingHandler;
    private readonly IMountHandler mountHandler;
    private readonly CombatTracker combatTracker;
    private readonly IBlacklist targetBlacklist;
    private readonly ApproachThrottle approachThrottle;
    private readonly ActionBarCastTimeReader castTimeReader;

    private readonly KeyAction? approachKey;
    private readonly Action approachAction;

    private readonly bool requiresNpcNameFinder;

    private long pullStart;

    private double PullDurationMs => GetElapsedTime(pullStart).TotalMilliseconds;

    public PullTargetGoal(ILogger<PullTargetGoal> logger, ConfigurableInput input,
        Wait wait, CombatLog combatlog, PlayerReader playerReader,
        AddonBits bits,
        IBlacklist targetBlacklist,
        StopMoving stopMoving, CastingHandler castingHandler,
        IMountHandler mountHandler, NpcNameTargeting npcNameTargeting,
        StuckDetector stuckDetector, CombatTracker combatTracker,
        ClassConfiguration classConfig, ApproachThrottle approachThrottle,
        ActionBarCastTimeReader castTimeReader)
        : base(nameof(PullTargetGoal))
    {
        this.logger = logger;
        this.input = input;
        this.wait = wait;
        this.combatLog = combatlog;
        this.playerReader = playerReader;
        this.bits = bits;
        this.stopMoving = stopMoving;
        this.castingHandler = castingHandler;
        this.mountHandler = mountHandler;
        this.npcNameTargeting = npcNameTargeting;
        this.stuckDetector = stuckDetector;
        this.combatTracker = combatTracker;
        this.targetBlacklist = targetBlacklist;
        this.classConfig = classConfig;
        this.approachThrottle = approachThrottle;
        this.castTimeReader = castTimeReader;

        Keys = classConfig.Pull.Sequence;

        approachAction = DefaultApproach;

        for (int i = 0; i < Keys.Length; i++)
        {
            KeyAction keyAction = Keys[i];

            if (keyAction.Name.Equals(input.Approach.Name, StringComparison.OrdinalIgnoreCase))
            {
                approachAction = ConditionalApproach;
                approachKey = keyAction;
            }

            if (keyAction.Requirements.Contains(RequirementFactory.AddVisible))
            {
                requiresNpcNameFinder = true;
            }
        }

        AddPrecondition(GoapKey.hastarget, true);
        AddPrecondition(GoapKey.targetisalive, true);
        if (classConfig.Mode != Mode.AssistFocus)
        {
            AddPrecondition(GoapKey.targettargetsus, false);
        }
        AddPrecondition(GoapKey.targethostile, true);
        AddPrecondition(GoapKey.withinpullrange, true);

        AddEffect(GoapKey.pulled, true);
    }

    public override void OnEnter()
    {
        wait.Update();
        stuckDetector.Reset();
        approachThrottle.Reset();

        if (mountHandler.IsMounted())
        {
            mountHandler.Dismount();
        }

        if (!input.StopAttack.OnCooldown() && RequiresStandingStill())
        {
            input.PressStopAttack();
            stopMoving.Stop();
            float stopElapsedMs = wait.Until(CastingHandler.SPELL_QUEUE_HALF, bits.NotMoving);
            Log($"Stop auto interact {stopElapsedMs}ms!");
        }

        if (requiresNpcNameFinder)
        {
            NpcNames npcTypes = NpcNames.Enemy;
            if (classConfig.TargetNeutral)
                npcTypes |= NpcNames.Neutral;

            npcNameTargeting.ChangeNpcType(npcTypes);
        }

        pullStart = GetTimestamp();
    }

    public override void OnExit()
    {
        if (requiresNpcNameFinder)
        {
            npcNameTargeting.ChangeNpcType(NpcNames.None);
        }
    }

    public void OnGoapEvent(GoapEventArgs e)
    {
        if (e.GetType() == typeof(ResumeEvent))
        {
            pullStart = GetTimestamp();
        }
    }

    public override void Update()
    {
        wait.Update();

        if (PullDurationMs > MAX_PULL_DURATION)
        {
            input.PressStopAttack();
            input.PressClearTarget();
            Log("Pull taking too long. Clear target and face away!");
            input.TurnRandomDir(1000);
            return;
        }

        if (classConfig.AutoPetAttack &&
            bits.Pet() &&
            (!playerReader.PetTarget() ||
            playerReader.TargetGuid != playerReader.PetTargetGuid) &&
            !input.PetAttack.OnCooldown())
        {
            input.PressStopAttack();
            input.PressPetAttack();
        }

        bool castAny = false;
        bool spellInQueue = false;

        ReadOnlySpan<KeyAction> keys = Keys;
        for (int i = 0; i < keys.Length; i++)
        {
            KeyAction keyAction = keys[i];

            if (keyAction.Name.Equals(input.Approach.Name,
                StringComparison.OrdinalIgnoreCase))
                continue;

            if (!keyAction.CanRun())
                continue;

            spellInQueue = castingHandler.SpellInQueue();
            if (spellInQueue)
            {
                break;
            }

            bool interrupt() => keyAction.CanBeInterrupted() || PullPrevention();

            if (castAny = castingHandler.Cast(keyAction, interrupt))
            {
                castAny = !keyAction.BaseAction;
            }
            else if (PullPrevention() &&
                !bits.Combat() &&
                (playerReader.IsCasting() || bits.Any_AutoAttack()))
            {
                Log("Preventing pulling possible tagged target!");
                input.PressStopAttack();
                input.PressClearTarget();
                wait.Update();
                return;
            }
        }

        if (bits.Target() && combatLog.EvadeMobs.Contains(playerReader.TargetGuid))
        {
            Log("Evading mob");

            input.PressStopAttack();
            input.PressClearTarget();
            wait.Update();
            return;
        }
        else if (bits.Target())
        {
            combatLog.ToPull.Add(playerReader.TargetGuid);
        }

        if (castAny || spellInQueue || playerReader.IsCasting() || (bits.AutoShot() && !playerReader.IsInMeleeRange()))
            return;

        approachAction();
    }

    private void DefaultApproach()
    {
        // Rate limiter for the whole method, not just the press: the stuck
        // detector below jumps on every call until its ladder opens, and
        // PressJump does not honour a cooldown of its own.
        if (input.Approach.OnCooldown())
        {
            return;
        }

        // Pull chases the target - only the profile's own Approach requirements
        // stop it, so this passes no arrival condition. WithInCombatRange() was the
        // wrong one: for a class whose combat range is a spell range - a Paladin's
        // Judgement at 10 yards from level 4 - it reads as arrived while the
        // character is still 10 yards out, so a melee pull never closes and the whole
        // pull ends up gated on that spell's cooldown.
        if (approachThrottle.ShouldPress() &&
            (!bits.SoftInteract() || EligibleEnemySoftTargetExists()))
        {
            input.PressApproach();
            wait.Update();

            approachThrottle.OnPressed();
        }

        if (!stuckDetector.IsMoving)
            stuckDetector.Update();
    }

    private void ConditionalApproach()
    {
        if (approachKey == null ||
            (!approachKey.CanRun() && !approachKey.OnCooldown()))
        {
            stopMoving.Stop();
            return;
        }

        DefaultApproach();
    }

    /// <summary>
    /// Whether a pull key that can fire right now needs the player standing still -
    /// a cast bar, or an explicit BeforeCastStop. Both are what a ranged opener
    /// declares; a melee opener declares neither, because it opens with instants and
    /// wants to keep the interact run it arrived on.
    ///
    /// <para>Stopping regardless cost the melee pull twice: the run has to be restarted
    /// from a standstill, and the stop plus its NotMoving wait burns the window a
    /// short-lived opener is waiting on - a Warrior sitting on Charge coming off
    /// cooldown loses most of it here.</para>
    /// </summary>
    private bool RequiresStandingStill()
    {
        ReadOnlySpan<KeyAction> keys = Keys;
        for (int i = 0; i < keys.Length; i++)
        {
            KeyAction keyAction = keys[i];

            // Approach, AutoAttack, StopAttack - none of them cast anything.
            if (keyAction.BaseAction)
                continue;

            // SlotIndex is 0 for a key with no action bar slot, so the reader would
            // answer with slot 1's cast time - only ask it about a key that has one.
            bool standStill =
                keyAction.BeforeCastStop ||
                keyAction.HasCastBar ||
                (keyAction.Slot > 0 && castTimeReader.HasCastBar(keyAction));

            if (standStill && keyAction.CanRun())
                return true;
        }

        return false;
    }

    private bool PullPrevention()
    {
        return !targetBlacklist.Is() ||
            playerReader.TargetTarget is
            UnitsTarget.None or
            UnitsTarget.Me or
            UnitsTarget.Pet or
            UnitsTarget.PartyOrPet;
    }

    private bool EligibleEnemySoftTargetExists() =>
        bits.SoftInteract() &&
        bits.SoftInteract_Hostile() &&
        !bits.SoftInteract_Dead() &&
        !bits.SoftInteract_Tagged() &&
        playerReader.SoftInteract_Type == GuidType.Creature;

    private void Log(string text)
    {
        logger.LogInformation(text);
    }
}
