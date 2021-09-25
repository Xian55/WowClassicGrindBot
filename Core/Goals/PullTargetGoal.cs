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

        private ILogger logger;
        private readonly ConfigurableInput input;

        private readonly Wait wait;
        private readonly PlayerReader playerReader;
        private readonly NpcNameFinder npcNameFinder;
        private readonly StopMoving stopMoving;
        private readonly StuckDetector stuckDetector;
        private readonly ClassConfiguration classConfiguration;
        
        private readonly CastingHandler castingHandler;
        private DateTime PullStartTime = DateTime.Now;
        private DateTime LastActive = DateTime.Now;

        public PullTargetGoal(ILogger logger, ConfigurableInput input, Wait wait, PlayerReader playerReader, NpcNameFinder npcNameFinder, StopMoving stopMoving, CastingHandler castingHandler, StuckDetector stuckDetector, ClassConfiguration classConfiguration)
        {
            this.logger = logger;
            this.input = input;

            this.wait = wait;
            this.playerReader = playerReader;
            this.npcNameFinder = npcNameFinder;
            this.stopMoving = stopMoving;
            
            this.castingHandler = castingHandler;
            this.stuckDetector = stuckDetector;
            this.classConfiguration = classConfiguration;

            AddPrecondition(GoapKey.targetisalive, true);
            AddPrecondition(GoapKey.incombat, false);
            AddPrecondition(GoapKey.hastarget, true);
            AddPrecondition(GoapKey.pulled, false);
            AddPrecondition(GoapKey.withinpullrange, true);
            AddPrecondition(GoapKey.isswimming, false);
            AddEffect(GoapKey.pulled, true);

            this.classConfiguration.Pull.Sequence.Where(k => k != null).ToList().ForEach(key => this.Keys.Add(key));
        }

        public override async Task PerformAction()
        {
            await input.TapStopAttack();
            this.playerReader.LastUIErrorMessage = UI_ERROR.NONE;

            if ((DateTime.Now - LastActive).TotalSeconds > 5)
            {
                PullStartTime = DateTime.Now;
            }
            LastActive = DateTime.Now;

            /*
            if ((DateTime.Now - PullStartTime).TotalSeconds > 30)
            {
                //await wowProcess.KeyPress(ConsoleKey.F3, 50); // clear target
                await wowProcess.TapClearTarget();
                await this.wowProcess.KeyPress(ConsoleKey.RightArrow, 1000, "Turn after pull timeout");
                return; 
            }
            */
            SendActionEvent(new ActionEventArgs(GoapKey.fighting, true));

            if (playerReader.PlayerBitValues.IsMounted)
            {
                await input.TapDismount();
            }

            bool pulled = await Pull();
            if (!pulled)
            {
                if (HasPickedUpAnAdd)
                {
                    logger.LogInformation($"Combat={this.playerReader.PlayerBitValues.PlayerInCombat}, Is Target targetting me={this.playerReader.PlayerBitValues.TargetOfTargetIsPlayer}");
                    logger.LogInformation($"Add on approach");

                    await this.stopMoving.Stop();
                    await input.TapStopKey();

                    //await wowProcess.KeyPress(ConsoleKey.F3, 50); // clear target
                    //await wowProcess.TapClearTarget();

                    await input.TapNearestTarget();
                    if (this.playerReader.HasTarget && playerReader.PlayerBitValues.TargetInCombat)
                    {
                        if (this.playerReader.TargetTarget == TargetTargetEnum.TargetIsTargettingMe)
                        {
                            return;
                        }
                    }

                    await input.TapClearTarget();
                    return;
                }
                

                if (!this.stuckDetector.IsMoving())
                {
                    await this.stuckDetector.Unstick();
                }

                await Interact();
                await wait.Update(1);
            }
            else
            {
                this.SendActionEvent(new ActionEventArgs(GoapKey.pulled, true));
                this.playerReader.LastUIErrorMessage = UI_ERROR.NONE;
            }
        }

        private async Task Interact()
        {
            if (this.classConfiguration.Interact.GetCooldownRemaining() == 0)
            {
                await input.TapInteractKey("PullTargetAction");
            }

            await this.castingHandler.InteractOnUIError();
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
            this.logger.LogInformation("Waiting for Melee range - max 10s");

            var start = DateTime.Now;

            while (playerReader.HasTarget && !playerReader.IsInMeleeRange && (DateTime.Now - start).TotalSeconds < 10)
            {
                await wait.Update(1);

                if (!item.StopBeforeCast)
                {
                    await Interact();
                }
            }
        }

        public async Task<bool> Pull()
        {
            bool hasCast = false;

            await input.TapStopAttack();
            this.playerReader.LastUIErrorMessage = UI_ERROR.NONE;

            foreach (var item in this.Keys)
            {
                if (item.StopBeforeCast)
                {
                    await this.stopMoving.Stop();
                    await input.TapStopAttack();
                    await input.TapStopKey();
                }

                if (playerReader.PlayerBitValues.HasPet && !playerReader.PetHasTarget)
                {
                    await input.TapPetAttack();
                }

                var sleepBeforeFirstCast = (item.StopBeforeCast && !hasCast && 150 > item.DelayBeforeCast) ? 150 : item.DelayBeforeCast;

                var success = await this.castingHandler.CastIfReady(item, sleepBeforeFirstCast);
                hasCast = hasCast || success;

                if (!this.playerReader.HasTarget)
                {
                    return false;
                }

                if (hasCast && item.WaitForWithinMelleRange)
                {
                    await this.WaitForWithinMeleeRange(item);
                }
            }

            // Wait for combat
            if (hasCast)
            {
                for (int i = 0; i < 40; i++)
                {
                    // wait for combat, for mob to be targetting me or have suffered damage or 2 seconds to have elapsed.
                    // sometimes after casting a ranged attack, we can be in combat before the attack has landed.
                    if (this.playerReader.PlayerBitValues.PlayerInCombat &&
                        (this.playerReader.PlayerBitValues.TargetOfTargetIsPlayer || this.playerReader.TargetHealthPercentage < 99 || i > 20))
                    {
                        return true;
                    }

                    await Task.Delay(100);
                }
            }

            return this.playerReader.PlayerBitValues.PlayerInCombat;
        }
    }
}