using Core.GOAP;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Core.Goals
{
    public class AdhocGoal : GoapGoal
    {
        private readonly ILogger logger;
        private readonly ConfigurableInput input;

        private readonly StopMoving stopMoving;
        private readonly PlayerReader playerReader;
        
        private readonly KeyAction key;
        private readonly CastingHandler castingHandler;

        public AdhocGoal(ILogger logger, ConfigurableInput input, KeyAction key, PlayerReader playerReader, StopMoving stopMoving, CastingHandler castingHandler)
        {
            this.logger = logger;
            this.input = input;
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
                await this.stopMoving.Stop();
                if (playerReader.PlayerBitValues.IsMounted)
                {
                    await input.Dismount();
                }
                await Task.Delay(1000);
            }
            if (key.DelayBeforeCast != 0)
            {
                await Task.Delay(key.DelayBeforeCast);
            }

            if (key.DelayAfterCast != 0)
            {
                await Task.Delay(key.DelayAfterCast);
            }
            await this.castingHandler.CastIfReady(key);

            this.key.ResetCooldown();

            bool wasDrinkingOrEating = this.playerReader.Buffs.Drinking || this.playerReader.Buffs.Eating;

            int seconds = 0;

            while ((this.playerReader.Buffs.Drinking || this.playerReader.Buffs.Eating || this.playerReader.IsCasting) && !this.playerReader.PlayerBitValues.PlayerInCombat)
            {
                await Task.Delay(1000);
                seconds++;
                this.logger.LogInformation($"Waiting for {key.Name}");

                if (this.playerReader.Buffs.Drinking)
                {
                    if (this.playerReader.ManaPercentage > 98) { break; }
                }
                else if (this.playerReader.Buffs.Eating && !this.key.Requirement.Contains("Well Fed"))
                {
                    if (this.playerReader.HealthPercent > 98) { break; }
                }
                else if (!this.key.CanRun())
                {
                    break;
                }

                if (seconds > 20)
                {
                    this.logger.LogInformation($"Waited long enough for {key.Name}");
                    break;
                }
            }

            if (wasDrinkingOrEating)
            {
                await input.TapStopKey(); // stand up
            }

            this.key.SetClicked();
        }

        public override string Name => this.Keys.Count == 0 ? base.Name : this.Keys[0].Name;
    }
}
