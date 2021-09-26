using Core.GOAP;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Core.Goals
{
    public class PullTargetGoal : GoapGoal
    {
        public override float CostOfPerformingAction { get => 7f; }

        private readonly ILogger logger;
        private readonly ConfigurableInput input;

        private readonly Wait wait;
        private readonly PlayerReader playerReader;
        private readonly StopMoving stopMoving;
        private readonly StuckDetector stuckDetector;
        private readonly ClassConfiguration classConfiguration;
        
        private readonly CastingHandler castingHandler;

        private readonly Random random = new Random(DateTime.Now.Millisecond);

        private DateTime pullStart = DateTime.Now;

        private int SecondsSincePullStarted => (int)(DateTime.Now - pullStart).TotalSeconds;

        public PullTargetGoal(ILogger logger, ConfigurableInput input, Wait wait, PlayerReader playerReader, StopMoving stopMoving, CastingHandler castingHandler, StuckDetector stuckDetector, ClassConfiguration classConfiguration)
        {
            this.logger = logger;
            this.input = input;

            this.wait = wait;
            this.playerReader = playerReader;
            this.stopMoving = stopMoving;
            
            this.castingHandler = castingHandler;
            this.stuckDetector = stuckDetector;
            this.classConfiguration = classConfiguration;

            classConfiguration.Pull.Sequence.Where(k => k != null).ToList().ForEach(key => Keys.Add(key));

            AddPrecondition(GoapKey.targetisalive, true);
            AddPrecondition(GoapKey.incombat, false);
            AddPrecondition(GoapKey.hastarget, true);
            AddPrecondition(GoapKey.pulled, false);
            AddPrecondition(GoapKey.withinpullrange, true);
            AddPrecondition(GoapKey.isswimming, false);

            AddEffect(GoapKey.pulled, true);
        }

        public override async Task OnEnter()
        {
            await base.OnEnter();

            if (playerReader.PlayerBitValues.IsMounted)
            {
                await input.TapDismount();
            }

            pullStart = DateTime.Now;
        }

        public override async Task PerformAction()
        {
            if (SecondsSincePullStarted > 7)
            {
                await input.TapClearTarget();
                await input.KeyPress(random.Next(2) == 0 ? ConsoleKey.LeftArrow : ConsoleKey.RightArrow, 1000, "Too much time to pull!");
                return;
            }

            SendActionEvent(new ActionEventArgs(GoapKey.fighting, true));

            if (!await Pull())
            {
                if (HasPickedUpAnAdd)
                {
                    Log($"Combat={this.playerReader.PlayerBitValues.PlayerInCombat}, Is Target targetting me={this.playerReader.PlayerBitValues.TargetOfTargetIsPlayer}");
                    Log($"Add on approach");

                    await stopMoving.Stop();

                    await input.TapNearestTarget();
                    await wait.Update(1);

                    if (this.playerReader.HasTarget && playerReader.PlayerBitValues.TargetInCombat)
                    {
                        if (this.playerReader.TargetTarget == TargetTargetEnum.TargetIsTargettingMe)
                        {
                            return;
                        }
                    }

                    await input.TapClearTarget();
                    await wait.Update(1);

                    return;
                }

                if (!stuckDetector.IsMoving())
                {
                    await stuckDetector.Unstick();
                }

                await Interact("No pulled!");
                await wait.Update(1);
            }
            else
            {
                SendActionEvent(new ActionEventArgs(GoapKey.pulled, true));
                playerReader.LastUIErrorMessage = UI_ERROR.NONE;
            }
        }

        private async Task Interact(string source)
        {
            if (classConfiguration.Interact.GetCooldownRemaining() == 0)
            {
                playerReader.LastUIErrorMessage = UI_ERROR.NONE;
                await input.TapInteractKey($"{GetType().Name} {source}");
                await wait.Update(1);

                await castingHandler.ReactToLastUIErrorMessage($"{GetType().Name}-Interact: ");
            }
        }

        protected bool HasPickedUpAnAdd
        {
            get
            {
                return this.playerReader.PlayerBitValues.PlayerInCombat && !this.playerReader.PlayerBitValues.TargetOfTargetIsPlayer && this.playerReader.HealthPercent > 98;
            }
        }

        protected async Task WaitForWithinMeleeRange(KeyAction item)
        {
            Log("Waiting for Melee range - max 10s");

            var start = DateTime.Now;

            while (playerReader.HasTarget && !playerReader.IsInMeleeRange && (DateTime.Now - start).TotalSeconds < 10)
            {
                await wait.Update(1);

                if (!item.StopBeforeCast)
                {
                    await Interact("");
                }
            }
        }

        public async Task<bool> Pull()
        {
            if (Keys.Count != 0)
            {
                await input.TapStopAttack();
                await wait.Update(1);

                playerReader.LastUIErrorMessage = UI_ERROR.NONE;
            }

            if (playerReader.PlayerBitValues.HasPet && !playerReader.PetHasTarget)
            {
                await input.TapPetAttack();
            }

            foreach (var item in Keys)
            {
                if (item.StopBeforeCast)
                {
                    await stopMoving.Stop();
                    await wait.Update(1);
                }

                var success = await castingHandler.CastIfReady(item, item.DelayBeforeCast);

                if (!playerReader.HasTarget)
                {
                    return false;
                }

                if (item.WaitForWithinMelleRange)
                {
                    await WaitForWithinMeleeRange(item);
                }
            }

            // Wait for combat
            (bool interrupted, double elapsedMs) = await wait.InterruptTask(1000, () => playerReader.PlayerBitValues.PlayerInCombat);
            if (!interrupted)
            {
                Log($"Entered combat after {elapsedMs}ms");
            }

            return playerReader.PlayerBitValues.PlayerInCombat;
        }

        private void Log(string s)
        {
            logger.LogInformation($"{GetType().Name}: {s}");
        }
    }
}