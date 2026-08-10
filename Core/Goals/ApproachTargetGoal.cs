using Core.GOAP;

using Microsoft.Extensions.Logging;

using System;

using static System.Diagnostics.Stopwatch;

#pragma warning disable 162

namespace Core.Goals;

public sealed partial class ApproachTargetGoal : GoapGoal, IGoapEventListener
{
    private const bool debug = true;
    /// <summary>
    /// The stuck ladder reads "Interact was pressed and the player is still not
    /// moving", so it cannot be shorter than the gap between presses - and that gap
    /// is <see cref="ApproachThrottle"/>'s keepalive, not the Approach key cooldown.
    /// A shorter window hands down its verdict between two presses and clears a
    /// target the bot was about to walk to.
    /// </summary>
    private const double STUCK_INTERVAL_MS =
        ApproachThrottle.STATIONARY_TARGET_REPEAT_MS;
    private const double MAX_APPROACH_DURATION_MS = 15_000; // max time to chase to pull
    private const double MIN_TIME_TILL_IDLE = 2000;

    // Spacing between tab-probes for a closer target. The TargetNearestTarget
    // key cooldown alone let this re-tab every 400ms for the whole approach.
    private const double NEAREST_PROBE_INTERVAL_MS = 3000;

    // The addon needs a few frames after the tab lands before the range cells
    // describe the new unit rather than the old one.
    private const double NEAREST_PROBE_SETTLE_MS = 300;

    private const double SWAP_BACK_TIMEOUT_MS = 300;
    private const int SWAP_BACK_MAX_ATTEMPTS = 2;

    /// <summary>
    /// Yards the tab-target must beat the current one by before the swap is
    /// worth it. A bare "closer" comparison flip-flops between two mobs at
    /// similar distance, because MinRange is a coarse bucket that jitters and
    /// because the baseline it is compared against is captured one settle
    /// window before the reading. The player keeps closing on the initial
    /// target during that window - at run speed roughly
    /// NEAREST_PROBE_SETTLE_MS * 7yd/s - so the baseline reads too far and
    /// every probe leans towards switching. This margin has to cover both.
    /// </summary>
    private const int NEAREST_PROBE_MIN_GAIN = 5;

    /// <summary>
    /// How long a mob stays "the one we just walked away from". Long enough to
    /// outlast a whole approach so the two candidates cannot trade places every
    /// time the goal re-enters, short enough that a genuinely new situation is
    /// judged on its own merits.
    /// </summary>
    private const double ABANDONED_GUID_TTL_MS = 20_000;

    private enum CloserTargetProbe
    {
        Idle,
        Settling,
        SwappingBack
    }

    public override float Cost => 8f;

    private readonly ILogger<ApproachTargetGoal> logger;
    private readonly ConfigurableInput input;
    private readonly Wait wait;
    private readonly PlayerReader playerReader;
    private readonly AddonBits bits;
    private readonly StopMoving stopMoving;
    private readonly CombatTracker combatTracker;
    private readonly IMountHandler mountHandler;
    private readonly IBlacklist targetBlacklist;
    private readonly CombatLog combatLog;
    private readonly ApproachThrottle approachThrottle;

    private long approachStart;

    private double nextStuckCheckTime;

    private int initialTargetGuid;
    private float initialMinRange;

    private CloserTargetProbe probe;

    /// <summary>
    /// A probe holds a different unit than the one being approached, so anything
    /// keyed off the target's range reads the wrong mob until it resolves. The
    /// old blocking version never exposed that window.
    /// </summary>
    private bool ProbeInFlight => probe != CloserTargetProbe.Idle;

    private double probeDeadline;
    private double nextNearestProbeTime;
    private int probeInitialMinRange;
    private int swapBackAttempts;

    private int probeAbandonedGuid;
    private long probeAbandonedAt;

    private double ApproachDurationMs => GetElapsedTime(approachStart).TotalMilliseconds;

    public ApproachTargetGoal(ILogger<ApproachTargetGoal> logger,
        ConfigurableInput input, Wait wait,
        PlayerReader playerReader, AddonBits addonBits,
        StopMoving stopMoving, CombatTracker combatTracker,
        IBlacklist blacklist,
        IMountHandler mountHandler,
        CombatLog combatLog,
        ApproachThrottle approachThrottle)
        : base(nameof(ApproachTargetGoal))
    {
        this.logger = logger;
        this.input = input;

        this.wait = wait;
        this.playerReader = playerReader;
        this.bits = addonBits;

        this.stopMoving = stopMoving;
        this.combatTracker = combatTracker;
        this.mountHandler = mountHandler;
        this.targetBlacklist = blacklist;
        this.combatLog = combatLog;
        this.approachThrottle = approachThrottle;

        AddPrecondition(GoapKey.hastarget, true);
        AddPrecondition(GoapKey.targetisalive, true);
        AddPrecondition(GoapKey.targethostile, true);
        AddPrecondition(GoapKey.incombatrange, false);

        AddEffect(GoapKey.incombatrange, true);
    }

    public void OnGoapEvent(GoapEventArgs e)
    {
        if (e.GetType() == typeof(ResumeEvent))
        {
            approachStart = GetTimestamp();
        }
    }

    public override void OnEnter()
    {
        initialTargetGuid = initialTargetGuid == playerReader.TargetGuid
            ? -1
            : playerReader.TargetGuid;

        initialMinRange = playerReader.MinRange();

        approachStart = GetTimestamp();
        approachThrottle.ResetForNewChase();
        SetNextStuckTimeCheck();

        probe = CloserTargetProbe.Idle;
        nextNearestProbeTime = 0;
    }

    public override void OnExit()
    {
        input.StopForward(false);
    }

    public override void Update()
    {
        wait.Update();

        if (bits.Combat() && !bits.Target_Combat() &&
            !combatLog.ToPull.Contains(playerReader.TargetGuid))
        {
            stopMoving.Stop();

            LogPreventExtraPull(logger);

            input.PressClearTarget();
            wait.Update();

            combatTracker.AcquiredTarget(5000);
            return;
        }

        // WithInCombatRange is the source of GoapKey.incombatrange and this
        // goal's own effect: once it holds the goal is done and GOAP simply
        // has not replanned yet. A press inside that lag window restarts the
        // interact run onto a mob the player already stands on, which is what
        // carries a melee class past it.
        if (!ProbeInFlight &&
            approachThrottle.ShouldPress(playerReader.WithInCombatRange()) &&
            (!bits.SoftInteract() || HasValidSoftInteract()))
        {
            input.PressApproach();
            wait.Update();

            approachThrottle.OnPressed();
        }

        if (!bits.Combat())
        {
            NonCombatApproach();
            RandomJump();
        }
    }

    private void NonCombatApproach()
    {
        if (ApproachDurationMs >= nextStuckCheckTime)
        {
            SetNextStuckTimeCheck();

            if (!bits.Moving())
            {
                if (playerReader.LastUIError is
                    UI_ERROR.ERR_AUTOFOLLOW_TOO_FAR or UI_ERROR.ERR_BADATTACKPOS)
                {
                    playerReader.LastUIError = UI_ERROR.NONE;

                    Log($"Target is too far({playerReader.MinRange()} yard) for interact, start moving forward!");
                    input.StartForward(false);

                    return;
                }
                // TODO: not sure why this is here!
                else if (playerReader.LastUIError == UI_ERROR.ERR_ATTACK_PACIFIED)
                {
                    playerReader.LastUIError = UI_ERROR.NONE;

                    if (mountHandler.IsMounted())
                    {
                        mountHandler.Dismount();

                        wait.While(bits.Falling);

                        input.PressInteract();
                        wait.Update();

                        SetNextStuckTimeCheck();

                        return;
                    }
                }

                Log($"Seems stuck! Clear Target.");

                input.PressClearTarget();
                input.TurnRandomDir(250 + Random.Shared.Next(250));
                wait.Update();

                return;
            }
        }

        if (ApproachDurationMs > MAX_APPROACH_DURATION_MS)
        {
            logger.LogWarning("Too long time. Clear Target. Turn away.");

            input.PressClearTarget();
            input.TurnRandomDir(250 + Random.Shared.Next(250));
            wait.Update();

            return;
        }

        if (UpdateCloserTargetProbe())
        {
            return;
        }

        if (!ProbeInFlight &&
            ApproachDurationMs > MIN_TIME_TILL_IDLE &&
            initialMinRange < playerReader.MinRange())
        {
            Log($"Going away from the target! {initialMinRange} < {playerReader.MinRange()}");

            input.PressClearTarget();
            wait.Update();
        }
    }

    private void SetNextStuckTimeCheck()
    {
        nextStuckCheckTime = ApproachDurationMs + STUCK_INTERVAL_MS;
    }

    /// <summary>
    /// Tab for a closer target, spread across ticks instead of blocking the loop.
    /// A keypress and the addon frame that reflects it are several frames apart,
    /// so each step arms a deadline and a later Update picks it up. Nothing here
    /// waits.
    /// </summary>
    /// <returns>True when the caller should give up the rest of this tick.</returns>
    private bool UpdateCloserTargetProbe()
    {
        if (probe == CloserTargetProbe.Settling)
        {
            return ApproachDurationMs >= probeDeadline && JudgeCloserTarget();
        }

        if (probe == CloserTargetProbe.SwappingBack)
        {
            if (ApproachDurationMs >= probeDeadline)
            {
                ResolveSwapBack();
            }

            return false;
        }

        if (!CanStartCloserTargetProbe())
        {
            return false;
        }

        probeInitialMinRange = playerReader.MinRange();

        input.PressNearestTarget();

        probe = CloserTargetProbe.Settling;
        probeDeadline = ApproachDurationMs + NEAREST_PROBE_SETTLE_MS;
        nextNearestProbeTime = ApproachDurationMs + NEAREST_PROBE_INTERVAL_MS;

        return false;
    }

    private bool CanStartCloserTargetProbe()
    {
        return playerReader.TargetGuid == initialTargetGuid &&
            !playerReader.IsInMeleeRange() &&
            ApproachDurationMs >= nextNearestProbeTime &&
            !input.TargetNearestTarget.OnCooldown() &&
            // Inside pull range the chase is over and a swap is all downside.
            // Tab-targeting with an auto attack running engages whatever it
            // lands on: Auto Shot fires the moment the new target is acquired,
            // so even switching straight back leaves a second mob pulled and
            // inbound. Beyond that, a warrior reaches charge range long before
            // melee, so the plan flips between this goal and PullTargetGoal and
            // every re-entry re-arms the probe - trading targets there is how
            // the bot closes on two mobs and pulls neither. Out of pull range
            // the swap costs nothing, and that is where a closer target is
            // worth finding.
            !playerReader.WithInPullRange();
    }

    private bool JudgeCloserTarget()
    {
        probe = CloserTargetProbe.Idle;

        // The tab landed back on the same unit, or on nothing at all.
        if (!bits.Target() || playerReader.TargetGuid == initialTargetGuid)
        {
            return false;
        }

        if (targetBlacklist.Is())
        {
            logger.LogWarning("Losing the target due blacklist!");
            return true;
        }

        // The mob we just walked away from. Without this the two candidates
        // trade places on every re-entry: switch to B, re-enter, tab back to A,
        // switch to A, re-enter, tab back to B - closing on both, pulling
        // neither.
        if (RecentlyAbandoned(playerReader.TargetGuid))
        {
            SwapBackToInitial();
            return false;
        }

        if (playerReader.MinRange() + NEAREST_PROBE_MIN_GAIN <= probeInitialMinRange)
        {
            logger.LogWarning("Found a closer target! {MinRange} + {MinGain} <= {InitialTargetMinRange}",
                playerReader.MinRange(), NEAREST_PROBE_MIN_GAIN, probeInitialMinRange);

            Abandon(initialTargetGuid);

            initialMinRange = playerReader.MinRange();

            return false;
        }

        logger.LogWarning("Stick to initial target!");

        Abandon(playerReader.TargetGuid);
        SwapBackToInitial();

        return false;
    }

    private void SwapBackToInitial()
    {
        swapBackAttempts = 0;
        PressLastTargetAndArm();
    }

    private void Abandon(int guid)
    {
        probeAbandonedGuid = guid;
        probeAbandonedAt = GetTimestamp();
    }

    private bool RecentlyAbandoned(int guid)
    {
        return guid == probeAbandonedGuid &&
            GetElapsedTime(probeAbandonedAt).TotalMilliseconds < ABANDONED_GUID_TTL_MS;
    }

    private void PressLastTargetAndArm()
    {
        input.PressLastTarget();

        swapBackAttempts++;

        probe = CloserTargetProbe.SwappingBack;
        probeDeadline = ApproachDurationMs + SWAP_BACK_TIMEOUT_MS;
    }

    private void ResolveSwapBack()
    {
        probe = CloserTargetProbe.Idle;

        if (playerReader.TargetGuid == initialTargetGuid)
        {
            // Back on the original. Retire the probe for the rest of this
            // approach - the start guard keys off initialTargetGuid, so -1
            // stops it re-firing.
            initialTargetGuid = -1;
            return;
        }

        if (swapBackAttempts < SWAP_BACK_MAX_ATTEMPTS)
        {
            PressLastTargetAndArm();
            return;
        }

        // Out of attempts and still holding the farther unit. Retire the probe
        // rather than tab back and forth for the rest of the approach.
        initialTargetGuid = -1;
    }

    private void RandomJump()
    {
        if (ApproachDurationMs > MIN_TIME_TILL_IDLE &&
            input.Jump.SinceLastClickMs > Random.Shared.Next(5000, 25_000))
        {
            input.PressJump();
            wait.Update();
        }
    }

    private bool HasValidSoftInteract()
    {
        return
            bits.SoftInteract() &&
            !bits.SoftInteract_Dead() &&
            !bits.SoftInteract_Tagged() &&
            playerReader.SoftInteract_Type == GuidType.Creature;
    }

    private void Log(string text)
    {
        logger.LogDebug(text);
    }


    #region Logging

    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Warning,
        Message = "Clear current target as not in combat!")]
    static partial void LogPreventExtraPull(ILogger logger);

    #endregion
}
