using Core.GOAP;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Core.Goals
{
    public class LootActions : GoapGoal
    {
        public override float CostOfPerformingAction { get => 4f; }

        private readonly ILogger logger;
        private readonly ConfigurableInput input;

        private readonly PlayerReader playerReader;
        private readonly StopMoving stopMoving;
        private readonly CastingHandler castingHandler;
        
        private DateTime lastActive = DateTime.Now;
        private readonly ClassConfiguration classConfiguration;
        private DateTime lastPulled = DateTime.Now;

        private int lastKilledGuid;

        public LootActions(ILogger logger, ConfigurableInput input, PlayerReader playerReader, StopMoving stopMoving,  ClassConfiguration classConfiguration, CastingHandler castingHandler)
        {
            this.logger = logger;
            this.input = input;

            this.playerReader = playerReader;
            this.stopMoving = stopMoving;
            
            this.classConfiguration = classConfiguration;
            this.castingHandler = castingHandler;

            lastKilledGuid = playerReader.LastKilledGuid;

            AddPrecondition(GoapKey.incombat, false);
            AddPrecondition(GoapKey.hastarget, true);
            AddPrecondition(GoapKey.targetisalive, false);
            //AddPrecondition(GoapKey.incombatrange, true);

            AddEffect(GoapKey.producedcorpse, true);
            AddEffect(GoapKey.targetisalive, false);
            AddEffect(GoapKey.hastarget, false);

            this.classConfiguration.LootActions.Sequence.Where(k => k != null).ToList().ForEach(key => this.Keys.Add(key));
        }

        protected async Task Loot()
        {
            //logger.LogInformation("-");
            if ((DateTime.Now - lastActive).TotalSeconds > 5)
            {
                classConfiguration.Interact.ResetCooldown();
            }


            bool pressed = false;
            foreach (var item in this.Keys)
            {
                bool isFightold=(DateTime.Now - lastActive).TotalSeconds > 5 && (DateTime.Now - lastPulled).TotalSeconds > 5;
                if (item.Name == "Interact" && !isFightold) // don't interact at the start of the fight
                {
                    item.SetClicked();
                    continue;
                }

                pressed = await this.castingHandler.CastIfReady(item);
                if (pressed)
                {
                    break;
                }
            }
            if (!pressed)
            {
                await Task.Delay(20);
            }

            this.lastActive = DateTime.Now;
        }

        public override void OnActionEvent(object sender, ActionEventArgs e)
        {
            if (e.Key == GoapKey.newtarget)
            {
                logger.LogInformation("?Reset cooldowns");

                ResetCooldowns();
            }

            if (e.Key == GoapKey.pulled)
            {
                this.lastPulled = DateTime.Now;
            }
        }

        private void ResetCooldowns()
        {
            this.classConfiguration.LootActions.Sequence
            .Where(i => i.ResetOnNewTarget)
            .ToList()
            .ForEach(item =>
            {
                logger.LogInformation($"Reset cooldown on {item.Name}");
                item.ResetCooldown();
                item.ResetChanges();
            });
        }

        protected bool HasPickedUpAnAdd
        {
            get
            {
                return this.playerReader.PlayerBitValues.PlayerInCombat &&
                    !this.playerReader.PlayerBitValues.TargetOfTargetIsPlayer
                    && this.playerReader.TargetHealthPercentage == 100;
            }
        }

        public override async Task PerformAction()
        {
            if (playerReader.PlayerBitValues.IsMounted)
            {
                await input.Dismount();
            }

            /*
            if (HasPickedUpAnAdd)
            {
                logger.LogInformation($"Combat={this.playerReader.PlayerBitValues.PlayerInCombat}, Is Target targetting me={this.playerReader.PlayerBitValues.TargetOfTargetIsPlayerOrPet}");
                logger.LogInformation($"Add on combat");
                await this.stopMoving.Stop();
                await wowProcess.TapStopKey();
                await wowProcess.TapClearTarget();
                return;
            }
            */

            if ((DateTime.Now - lastActive).TotalSeconds > 5 && (DateTime.Now - lastPulled).TotalSeconds > 5)
            {
                logger.LogInformation("Interact and stop");
                await input.TapInteractKey("CombatActionBase PerformAction");
                //await this.castingHandler.PressKey(ConsoleKey.UpArrow, "", 57);
            }

            await stopMoving.Stop();

            SendActionEvent(new ActionEventArgs(GoapKey.fighting, true));

            await this.castingHandler.InteractOnUIError();

            await Loot();
            lastActive = DateTime.Now;
        }
    }
}