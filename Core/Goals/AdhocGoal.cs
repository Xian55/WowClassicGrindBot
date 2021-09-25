using Core.GOAP;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Core.Goals
{
    public class AdhocGoal : GoapGoal
    {
        private readonly ILogger logger;
        private readonly ConfigurableInput input;

        private readonly Wait wait;
        private readonly StopMoving stopMoving;
        private readonly PlayerReader playerReader;
        
        private readonly KeyAction key;
        private readonly CastingHandler castingHandler;

        public AdhocGoal(ILogger logger, ConfigurableInput input, Wait wait, KeyAction key, PlayerReader playerReader, StopMoving stopMoving, CastingHandler castingHandler)
        {
            this.logger = logger;
            this.input = input;
            this.wait = wait;
            this.stopMoving = stopMoving;
            this.playerReader = playerReader;
            this.key = key;
            this.castingHandler = castingHandler;

            if (key.InCombat == "false")
            {
                AddPrecondition(GoapKey.incombat, false);
            }
            else if (key.InCombat == "true")
            {
                AddPrecondition(GoapKey.incombat, true);
            }

            this.Keys.Add(key);
        }

        public override bool CheckIfActionCanRun()
        {
            return this.key.CanRun();
        }

        public override float CostOfPerformingAction { get => key.Cost; }

        public override async Task PerformAction()
        {
            if (key.StopBeforeCast)
            {
                await stopMoving.Stop();
                await wait.Update(1);

                if (playerReader.PlayerBitValues.IsMounted)
                {
                    await input.TapDismount();
                    //if (!await Wait(1000, () => playerReader.PlayerBitValues.PlayerInCombat)) return; // vanilla after dismout GCD
                }
            }

            await castingHandler.CastIfReady(key, key.DelayBeforeCast);
            key.ResetCooldown();

            bool wasDrinkingOrEating = playerReader.Buffs.Drinking || playerReader.Buffs.Eating;

            logger.LogInformation($"Waiting for {key.Name}");

            DateTime startTime = DateTime.Now;
            while ((playerReader.Buffs.Drinking || playerReader.Buffs.Eating || playerReader.IsCasting) && !playerReader.PlayerBitValues.PlayerInCombat)
            {
                await wait.Update(1);

                if (playerReader.Buffs.Drinking)
                {
                    if (playerReader.ManaPercentage > 98) { break; }
                }
                else if (playerReader.Buffs.Eating && !key.Requirement.Contains("Well Fed"))
                {
                    if (playerReader.HealthPercent > 98) { break; }
                }
                else if (!key.CanRun())
                {
                    break;
                }

                if ((DateTime.Now - startTime).TotalSeconds > 25)
                {
                    logger.LogInformation($"Waited (25s) long enough for {key.Name}");
                    break;
                }
            }

            if (wasDrinkingOrEating)
            {
                await input.TapStopKey(); // stand up
            }

            key.SetClicked();
        }

        public override string Name => this.Keys.Count == 0 ? base.Name : this.Keys[0].Name;
    }
}
