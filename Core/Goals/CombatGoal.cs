using Core.GOAP;

using Game;

using Microsoft.Extensions.Logging;

using System;
using System.Numerics;

using static System.MathF;

namespace Core.Goals;

public sealed class CombatGoal : GoapGoal, IGoapEventListener
{
    public override float Cost => 4f;

    private readonly ILogger<CombatGoal> logger;
    private readonly ConfigurableInput input;
    private readonly ClassConfiguration classConfig;
    private readonly Wait wait;
    private readonly PlayerReader playerReader;
    private readonly AddonBits bits;
    private readonly StopMoving stopMoving;
    private readonly CastingHandler castingHandler;
    private readonly IMountHandler mountHandler;
    private readonly CombatLog combatLog;

    private float lastDirection;
    private float lastMinDistance;
    private float lastMaxDistance;

    public CombatGoal(ILogger<CombatGoal> logger, ConfigurableInput input,
        Wait wait, PlayerReader playerReader, StopMoving stopMoving, AddonBits bits,
        ClassConfiguration classConfiguration, ClassConfiguration classConfig,
        CastingHandler castingHandler, CombatLog combatLog,
        IMountHandler mountHandler)
        : base(nameof(CombatGoal))
    {
        this.logger = logger;
        this.input = input;

        this.wait = wait;
        this.playerReader = playerReader;
        this.bits = bits;
        this.combatLog = combatLog;

        this.stopMoving = stopMoving;
        this.castingHandler = castingHandler;
        this.mountHandler = mountHandler;
        this.classConfig = classConfig;

        AddPrecondition(GoapKey.incombat, true);
        AddPrecondition(GoapKey.hastarget, true);
        AddPrecondition(GoapKey.targetisalive, true);
        AddPrecondition(GoapKey.targethostile, true);
        //AddPrecondition(GoapKey.targettargetsus, true);
        AddPrecondition(GoapKey.incombatrange, true);

        AddEffect(GoapKey.producedcorpse, true);
        AddEffect(GoapKey.targetisalive, false);
        AddEffect(GoapKey.hastarget, false);

        Keys = classConfiguration.Combat.Sequence;
    }

    public void OnGoapEvent(GoapEventArgs e)
    {
        if (e is GoapStateEvent s && s.Key == GoapKey.producedcorpse)
        {
            // have to check range
            // ex. target died far away have to consider the range and approximate
            float distance = (lastMaxDistance + lastMinDistance) / 2f;
            int packedGuid = combatLog.DeadGuid.Value;
            SendGoapEvent(new CorpseEvent(GetCorpseLocation(distance), distance, playerReader.Direction, playerReader.MapPos, packedGuid));
        }
    }

    private void ResetCooldowns()
    {
        ReadOnlySpan<KeyAction> span = Keys;
        for (int i = 0; i < span.Length; i++)
        {
            KeyAction keyAction = span[i];
            if (keyAction.ResetOnNewTarget)
            {
                keyAction.ResetCooldown();
                keyAction.ResetCharges();
            }
        }
    }

    public override void OnEnter()
    {
        if (mountHandler.IsMounted())
        {
            mountHandler.Dismount();
        }

        lastDirection = playerReader.Direction;
    }

    public override void OnExit()
    {
        if (combatLog.DamageTakenCount() > 0 && !bits.Target())
        {
            stopMoving.Stop();
        }
    }

    public override void Update()
    {
        wait.Update();

        if (Abs(lastDirection - playerReader.Direction) > PI / 2)
        {
            logger.LogInformation("Turning too fast!");
            stopMoving.Stop();
        }

        lastDirection = playerReader.Direction;
        lastMinDistance = playerReader.MinRange();
        lastMaxDistance = playerReader.MaxRange();

        if (bits.Drowning())
        {
            input.PressJump();
            return;
        }

        if (bits.SoftInteract_Enabled())
        {
            UnstuckDeadSoftTargetLock();
        }

        if (classConfig.AutoPetAttack &&
            bits.Pet() &&
            (!playerReader.PetTarget() || playerReader.PetTargetGuid != playerReader.TargetGuid) &&
            !input.PetAttack.OnCooldown())
        {
            input.PressPetAttack();
        }

        ReadOnlySpan<KeyAction> span = Keys;
        for (int i = 0; bits.Target_Alive() && i < span.Length; i++)
        {
            KeyAction keyAction = span[i];

            if (castingHandler.SpellInQueue() && !keyAction.BaseAction)
            {
                continue;
            }

            bool interrupt() => bits.Target_Alive() && keyAction.CanBeInterrupted();

            if (castingHandler.CastIfReady(keyAction, interrupt))
            {
                break;
            }
        }

        if (!bits.Target() || (bits.Target() && bits.Target_Dead()))
        {
            logger.LogInformation("Lost target!");

            if (combatLog.DamageTakenCount() > 0)
            {
                if (bits.Target() && bits.Target_Dead())
                {
                    logger.LogInformation("Clear current dead target!");
                    input.PressClearTarget();
                    wait.Update();
                }

                logger.LogWarning("Search Possible Threats!");
                stopMoving.Stop();

                FindPossibleThreats();
            }
            else
            {
                input.PressClearTarget();
                wait.Update();
            }
        }
    }

    private void FindPossibleThreats()
    {
        if (bits.Pet_Defensive())
        {
            float elapsedPetFoundTarget = wait.Until(CastingHandler.GCD,
                () => playerReader.PetTarget() && bits.PetTarget_Alive());

            if (elapsedPetFoundTarget < 0)
            {
                logger.LogWarning("Pet not found target!");
                input.PressClearTarget();
                return;
            }

            ResetCooldowns();

            input.PressTargetPet();
            input.PressTargetOfTarget();
            wait.Update();

            logger.LogWarning("Found new target by pet. {ElapsedMs}ms", elapsedPetFoundTarget);

            return;
        }

        logger.LogInformation("Checking target in front...");
        input.PressNearestTarget();
        wait.Update();

        if (bits.Target() && !bits.Target_Dead() && bits.Target_Hostile())
        {
            if (!bits.Target_Combat())
            {
                logger.LogWarning("Dont pull non-hostile target!");
                input.PressClearTarget();
                wait.Update();
                return;
            }

            if (bits.TargetTarget_PlayerOrPet() || combatLog.DamageTaken.Contains(playerReader.TargetGuid))
            {
                ResetCooldowns();

                logger.LogWarning("Found new target!");
                wait.Update();
                return;
            }
        }

        logger.LogWarning("Possible threats {DamageTakenCount}!", combatLog.DamageTakenCount());

        if (bits.SoftInteract_Enabled())
        {
            UnstuckDeadSoftTargetLock();
        }
    }

    private Vector3 GetCorpseLocation(float distance)
    {
        return PointEstimator.GetMapPos(playerReader.WorldMapArea, playerReader.WorldPos, playerReader.Direction, distance);
    }

    private void UnstuckDeadSoftTargetLock()
    {
        if (!bits.SoftInteract() ||
            !bits.SoftInteract_Dead() ||
            !bits.Auto_Attack() ||
            combatLog.LastDamageDoneTime.ElapsedMs() < playerReader.MainHandSpeedMs() * 2 ||
            combatLog.DamageTakenCount() == 0)
        {
            return;
        }

        logger.LogWarning("Turn away from dead softTarget due locking current target interaction!");

        float startDirection = playerReader.Direction;
        float totalRotation = 0f;

        ConsoleKey turnKey = Random.Shared.Next(2) == 0
            ? input.TurnLeftKey
            : input.TurnRightKey;

        input.SetKeyState(turnKey, true, false);

        while (bits.SoftInteract() && bits.SoftInteract_Dead())
        {
            wait.Update();

            float currentDirection = playerReader.Direction;
            float delta = Abs(currentDirection - startDirection);
            if (delta > PI)
                delta = Tau - delta;

            totalRotation = delta;

            // Safety: if we've turned nearly 360°, soft target is everywhere - strafe instead
            if (totalRotation >= Tau - 0.2f)
            {
                input.SetKeyState(turnKey, false, false);
                logger.LogWarning("Full rotation without clearing soft target - strafe!");

                KeyAction strafeAction = Random.Shared.Next(2) == 0
                    ? input.StrafeLeft
                    : input.StrafeRight;

                input.PressFixed(strafeAction.ConsoleKey, 500, default);
                wait.Update();

                return;
            }
        }

        input.SetKeyState(turnKey, false, false);
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Cleared dead soft target after {TurnDegrees:F0} degree turn", totalRotation * 180f / PI);
    }
}
