using Core.GOAP;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Core.Extensions;

namespace Core.Goals
{
    public class ParallelGoal : GoapGoal
    {
        public override float CostOfPerformingAction { get => 3f; }

        private readonly ILogger logger;
        private readonly ConfigurableInput input;

        private readonly StopMoving stopMoving;
        private readonly Wait wait;
        private readonly PlayerReader playerReader;
        
        private readonly CastingHandler castingHandler;

        public ParallelGoal(ILogger logger, ConfigurableInput input, Wait wait, PlayerReader playerReader, StopMoving stopMoving, List<KeyAction> keysConfig, CastingHandler castingHandler)
        {
            this.logger = logger;
            this.input = input;

            this.stopMoving = stopMoving;
            this.wait = wait;
            this.playerReader = playerReader;
            
            this.castingHandler = castingHandler;

            AddPrecondition(GoapKey.incombat, false);

            keysConfig.ForEach(key => Keys.Add(key));
        }

        public override bool CheckIfActionCanRun()
        {
            return Keys.Any(key => key.CanRun());
        }

        public override async Task PerformAction()
        {
            if (Keys.Any(k => k.StopBeforeCast))
            {
                await stopMoving.Stop();
                await wait.Update(1);

                if (playerReader.PlayerBitValues.IsMounted)
                {
                    await input.TapDismount();
                    await wait.Update(1);
                    //if (!await Wait(1000, () => playerReader.PlayerBitValues.PlayerInCombat)) return; // vanilla after dismout GCD
                }
            }

            await AsyncExt.Loop(Keys, async (KeyAction key) =>
            {
                var pressed = await castingHandler.CastIfReady(key, key.DelayBeforeCast);
                key.ResetCooldown();
                key.SetClicked();
            });

            bool wasDrinkingOrEating = playerReader.Buffs.Drinking || playerReader.Buffs.Eating;

            logger.LogInformation($"Waiting for {Name}");

            DateTime startTime = DateTime.Now;
            while ((playerReader.Buffs.Drinking || playerReader.Buffs.Eating || playerReader.IsCasting) && !playerReader.PlayerBitValues.PlayerInCombat)
            {
                await wait.Update(1);

                if (playerReader.Buffs.Drinking && playerReader.Buffs.Eating)
                {
                    if (playerReader.ManaPercentage > 98 && playerReader.HealthPercent > 98) { break; }
                }
                else if (playerReader.Buffs.Drinking)
                {
                    if (playerReader.ManaPercentage > 98) { break; }
                }
                else if (playerReader.Buffs.Eating)
                {
                    if (playerReader.HealthPercent > 98) { break; }
                }

                if ((DateTime.Now - startTime).TotalSeconds >= 25)
                {
                    logger.LogInformation($"Waited (25s) long enough for {Name}");
                    break;
                }
            }

            if (wasDrinkingOrEating)
            {
                await input.TapStandUpKey();
            }
        }
    }
}