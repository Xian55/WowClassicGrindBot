using Core.GOAP;
using Microsoft.Extensions.Logging;
using System;
using System.Drawing;
using System.Threading.Tasks;

namespace Core.Goals
{
    public class ApproachTargetGoal : GoapGoal
    {
        public override float CostOfPerformingAction { get => 8f; }

        private ILogger logger;
        private readonly ConfigurableInput input;

        private readonly Wait wait;
        private readonly PlayerReader playerReader;
        private readonly StopMoving stopMoving;
        private readonly StuckDetector stuckDetector;

        private bool debug = false;

        private Random random = new Random();
        private DateTime LastJump = DateTime.Now;

        private bool NeedsToReset = true;
        private bool playerWasInCombat = false;

        private DateTime lastFighting = DateTime.Now;

        private int SecondsSinceLastFighting => (int)(DateTime.Now - this.lastFighting).TotalSeconds;

        private bool HasPickedUpAnAdd
        {
            get
            {
                return this.playerReader.PlayerBitValues.PlayerInCombat && !this.playerReader.PlayerBitValues.TargetOfTargetIsPlayer;
            }
        }

        public ApproachTargetGoal(ILogger logger, ConfigurableInput input, Wait wait, PlayerReader playerReader, StopMoving stopMoving,  StuckDetector stuckDetector)
        {
            this.logger = logger;
            this.input = input;

            this.wait = wait;
            this.playerReader = playerReader;
            this.stopMoving = stopMoving;
            
            this.stuckDetector = stuckDetector;

            AddPrecondition(GoapKey.hastarget, true);
            AddPrecondition(GoapKey.targetisalive, true);
            AddPrecondition(GoapKey.incombatrange, false);

            AddEffect(GoapKey.incombatrange, true);
        }

        public override async Task PerformAction()
        {
            if (playerReader.PlayerBitValues.IsMounted)
            {
                await input.TapDismount();
            }

            if (NeedsToReset)
            {
                this.stuckDetector.ResetStuckParameters();
            }

            var location = playerReader.PlayerLocation;

            if (!playerReader.PlayerBitValues.PlayerInCombat)
            {
                playerWasInCombat = false;
            }
            else
            {
                // we are in combat
                if (!playerWasInCombat && HasPickedUpAnAdd)
                {
                    logger.LogInformation("WARN Bodypull -- Looks like we have an add on approach");
                    logger.LogInformation($"Combat={this.playerReader.PlayerBitValues.PlayerInCombat}, Is Target targetting me={this.playerReader.PlayerBitValues.TargetOfTargetIsPlayer}");
                    
                    await this.stopMoving.Stop();
                    await input.TapClearTarget();
                    await input.TapStopKey();

                    if(playerReader.PetHasTarget)
                    {
                        await this.input.TapTargetPet();
                        await this.input.TapTargetOfTarget();
                    }
                }

                playerWasInCombat = true;
            }

            await this.TapInteractKey("");
            await wait.Update(1);

            var newLocation = playerReader.PlayerLocation;
            if ((location.X == newLocation.X && location.Y == newLocation.Y && SecondsSinceLastFighting > 5) ||
                this.playerReader.LastUIErrorMessage == UI_ERROR.ERR_AUTOFOLLOW_TOO_FAR)
            {
                input.SetKeyState(ConsoleKey.UpArrow, true, false, $"{GetType().Name}: ");
                await wait.InterruptTask(100, () => false);
                await input.TapJump();
                this.playerReader.LastUIErrorMessage = UI_ERROR.NONE;
            }

            await RandomJump();

            //
            int approachSeconds = (int)(this.stuckDetector.actionDurationSeconds);
            if (approachSeconds > 20)
            {
                await this.stuckDetector.Unstick();
                await this.TapInteractKey($"{GetType().Name}:  unstick");
                await Task.Delay(250);
            }
        }

        public override void OnActionEvent(object sender, ActionEventArgs e)
        {
            if (sender != this)
            {
                NeedsToReset = true;
                playerWasInCombat = false;

                if (e.Key == GoapKey.fighting)
                {
                    lastFighting = DateTime.Now;
                }
            }
        }

        private async Task RandomJump()
        {
            if ((DateTime.Now - LastJump).TotalSeconds > 7)
            {
                if (random.Next(1) == 0)
                {
                    await input.TapJump();
                }
                LastJump = DateTime.Now;
            }
        }


        public async Task TapInteractKey(string source)
        {
            await input.TapInteractKey(source);
            this.playerReader.LastUIErrorMessage = UI_ERROR.NONE;
            await input.TapStopAttack();
        }

        private void Log(string text)
        {
            if (debug)
            {
                logger.LogInformation($"{this.GetType().Name}: {text}");
            }
        }

    }
}