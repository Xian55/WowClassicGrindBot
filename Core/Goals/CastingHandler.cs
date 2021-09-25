using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Core.Goals
{
    public class CastingHandler
    {
        private readonly ILogger logger;
        private readonly ConfigurableInput input;

        private readonly Wait wait;
        private readonly PlayerReader playerReader;
        
        private ConsoleKey lastKeyPressed = ConsoleKey.Escape;
        private readonly ClassConfiguration classConfig;
        private readonly IPlayerDirection direction;
        private readonly NpcNameFinder npcNameFinder;
        private readonly StopMoving stopMoving;

        private readonly KeyAction defaultKeyAction = new KeyAction();
        private const int MaxWaitTimeMs = 300;
        private const int MaxCastTimeMs = 15000;
        private const int GCD = 1500;

        public CastingHandler(ILogger logger, ConfigurableInput input, Wait wait, PlayerReader playerReader, ClassConfiguration classConfig, IPlayerDirection direction, NpcNameFinder npcNameFinder, StopMoving stopMoving)
        {
            this.logger = logger;
            this.input = input;

            this.wait = wait;
            this.playerReader = playerReader;
            
            this.classConfig = classConfig;
            this.direction = direction;
            this.npcNameFinder = npcNameFinder;
            this.stopMoving = stopMoving;
        }

        protected bool CanRun(KeyAction item)
        {
            if (!string.IsNullOrEmpty(item.CastIfAddsVisible))
            {
                var needAdds = bool.Parse(item.CastIfAddsVisible);
                if (needAdds != npcNameFinder.PotentialAddsExist)
                {
                    item.LogInformation($"Only cast if adds exist = {item.CastIfAddsVisible} and it is {npcNameFinder.PotentialAddsExist}");
                    return false;
                }
            }

            if(item.School != SchoolMask.None)
            {
                if(classConfig.ImmunityBlacklist.TryGetValue(playerReader.TargetId, out var list))
                {
                    if(list.Contains(item.School))
                    {
                        return false;
                    }
                }
            }

            return item.CanRun();
        }

        protected async Task PressCastKeyAndWaitForCastToEnd(ConsoleKey key, int maxWaitMs)
        {
            await PressKey(key);
            if (!this.playerReader.IsCasting)
            {
                // try again
                await PressKey(key);
            }

            for (int i = 0; i < maxWaitMs; i += 100)
            {
                if (!this.playerReader.IsCasting)
                {
                    return;
                }
                await Task.Delay(100);
            }
        }

        public async Task<bool> CastIfReady(KeyAction item, int sleepBeforeCast = 0)
        {
            if (!CanRun(item))
            {
                return false;
            }

            if (item.ConsoleKey == 0)
            {
                return false;
            }

            if (!await SwitchToCorrectShapeShiftForm(item))
            {
                return false;
            }

            if (playerReader.IsShooting)
            {
                await input.TapStopAttack("Stop casting Shoot");
                var shootWait = await wait.InterruptTask(GCD, () => playerReader.ActionBarUsable.ActionUsable(item.Name));
                if (!shootWait.Item1)
                {
                    item.LogInformation($" waited to end shooting {shootWait.Item2}ms");
                }
            }

            if (sleepBeforeCast > 0)
            {
                item.LogInformation($" Wait {sleepBeforeCast}ms before press.");
                await Task.Delay(sleepBeforeCast);
            }

            long beforeBuff = playerReader.Buffs.Value;
            bool beforeHasTarget = playerReader.HasTarget;

            await PressKey(item.ConsoleKey, item.Name, item.PressDuration);
            item.SetClicked();

            if (!item.HasCastBar)
            {
                var result = await wait.InterruptTask(item.DelayAfterCast, () => beforeHasTarget != playerReader.HasTarget);
                item.LogInformation($" ... no castbar delay after cast {result.Item2}ms");
                if (!result.Item1)
                {
                    item.LogInformation($" .... wait interrupted {result.Item2}ms");
                }

                if (item.AfterCastWaitBuff)
                {
                    var result1 = await wait.InterruptTask(MaxWaitTimeMs, () => beforeBuff != playerReader.Buffs.Value);
                    if (!result1.Item1)
                    {
                        item.LogInformation($"AfterCastWaitBuff .... wait interrupted {result1.Item2}ms");
                    }

                    logger.LogInformation($"AfterCastWaitBuff: Interrupted: {result1.Item1} | Delay: {result1.Item2}ms");
                }

                await InteractOnUIError();
            }
            else
            {
                var startedCasting = await wait.InterruptTask(MaxWaitTimeMs, () => playerReader.IsCasting);
                if (!startedCasting.Item1)
                {
                    item.LogInformation($" start casting after {startedCasting.Item2}ms");
                }

                if (!playerReader.IsCasting && playerReader.HasTarget)
                {
                    await InteractOnUIError();

                    if(item.StopBeforeCast)
                        await stopMoving.Stop();

                    item.LogInformation($"Not casting, pressing it again");

                    await PressKey(item.ConsoleKey, item.Name, item.PressDuration);
                    await wait.InterruptTask(MaxWaitTimeMs, () => playerReader.IsCasting);

                    if (!playerReader.IsCasting && playerReader.HasTarget)
                    {
                        item.LogInformation($"Still not casting !");
                        await InteractOnUIError();
                        return false;
                    }
                }

                item.LogInformation(" waiting for cast bar to end.");
                await wait.InterruptTask(MaxCastTimeMs, () => !playerReader.IsCasting);

                if (item.DelayAfterCast != defaultKeyAction.DelayAfterCast)
                {
                    if (item.DelayUntilCombat) // stop waiting if the mob is targetting me
                    {
                        item.LogInformation($" ... delay after cast {item.DelayAfterCast}ms");

                        var sw = new Stopwatch();
                        sw.Start();
                        while (sw.ElapsedMilliseconds < item.DelayAfterCast)
                        {
                            await wait.Update(1);
                            if (playerReader.PlayerBitValues.TargetOfTargetIsPlayer)
                            {
                                break;
                            }
                        }
                    }
                    else
                    {
                        item.LogInformation($" ... castbar delay after cast {item.DelayAfterCast}ms");
                        var result = await wait.InterruptTask(item.DelayAfterCast, () => beforeHasTarget != playerReader.HasTarget);
                        if (!result.Item1)
                        {
                            item.LogInformation($" .... wait interrupted {result.Item2}ms");
                        }
                    }
                }

                if (item.AfterCastWaitBuff)
                {
                    var result = await wait.InterruptTask(MaxWaitTimeMs, () => beforeBuff != playerReader.Buffs.Value);
                    logger.LogInformation($"AfterCastWaitBuff: Interrupted: {result.Item1} | Delay: {result.Item2}ms");
                }
            }

            if (item.StepBackAfterCast > 0)
            {
                input.SetKeyState(ConsoleKey.DownArrow, true, false, $"Step back for {item.StepBackAfterCast}ms");
                var stepbackResult = await wait.InterruptTask(item.StepBackAfterCast, () => beforeHasTarget != playerReader.HasTarget);
                if(!stepbackResult.Item1)
                {
                    item.LogInformation($" .... interrupted wait stepback {stepbackResult.Item2}ms");
                }
                input.SetKeyState(ConsoleKey.DownArrow, false, false);
            }

            item.ConsumeCharge();
            return true;
        }

        protected async Task<bool> SwitchToCorrectShapeShiftForm(KeyAction item)
        {
            if (this.playerReader.PlayerClass != PlayerClassEnum.Druid || string.IsNullOrEmpty(item.ShapeShiftForm)
                || this.playerReader.Druid_ShapeshiftForm == item.ShapeShiftFormEnum)
            {
                return true;
            }

            var desiredFormKey = this.classConfig.ShapeshiftForm
                .Where(s => s.ShapeShiftFormEnum == item.ShapeShiftFormEnum)
                .FirstOrDefault();

            if (desiredFormKey == null)
            {
                logger.LogWarning($"Unable to find key in ShapeshiftForm to transform into {item.ShapeShiftFormEnum}");
                return false;
            }

            await this.input.KeyPress(desiredFormKey.ConsoleKey, 325);

            return this.playerReader.Druid_ShapeshiftForm == item.ShapeShiftFormEnum;
        }

        public async Task PressKey(ConsoleKey key, string description = "", int duration = 50)
        {
            if (lastKeyPressed == classConfig.Interact.ConsoleKey)
            {
                var distance = WowPoint.DistanceTo(classConfig.Interact.LastClickPostion, this.playerReader.PlayerLocation);

                if (distance > 1)
                {
                    logger.LogInformation($"Stop moving: We have moved since the last interact: {distance}");
                    await input.TapStopKey();
                    classConfig.Interact.SetClicked();
                    await wait.Update(1);
                }
            }

            await input.KeyPress(key, duration, description);

            lastKeyPressed = key;
        }

        public virtual async Task InteractOnUIError()
        {
            var lastError = this.playerReader.LastUIErrorMessage;
            switch (this.playerReader.LastUIErrorMessage)
            {
                case UI_ERROR.ERR_BADATTACKFACING:
                case UI_ERROR.ERR_SPELL_FAILED_S:
                case UI_ERROR.ERR_SPELL_OUT_OF_RANGE:
                case UI_ERROR.ERR_BADATTACKPOS:
                case UI_ERROR.ERR_AUTOFOLLOW_TOO_FAR:

                    await input.TapStopAttack($"{GetType().Name}: " + this.playerReader.LastUIErrorMessage.ToString());
                    var facing = this.playerReader.Direction;
                    var location = this.playerReader.PlayerLocation;

                    if (this.classConfig.Interact.MillisecondsSinceLastClick > 500)
                    {
                        await input.TapInteractKey($"{GetType().Name} InteractOnUIError by Timer");
                        await Task.Delay(50);
                    }

                    if (lastError == UI_ERROR.ERR_SPELL_FAILED_S)
                    {
                        await input.TapInteractKey($"{GetType().Name} InteractOnUIError by ERR_SPELL_FAILED_S");
                        await wait.Update(1); //3
                        if (this.playerReader.LastUIErrorMessage == UI_ERROR.ERR_BADATTACKPOS && this.playerReader.Direction == facing)
                        {
                            var desiredDirection = facing + Math.PI;
                            desiredDirection = desiredDirection > Math.PI * 2 ? desiredDirection - Math.PI * 2 : desiredDirection;
                            await this.direction.SetDirection(desiredDirection, new WowPoint(0, 0), "InteractOnUIError Turning 180 as I have not moved!");
                        }
                    }

                    this.playerReader.LastUIErrorMessage = UI_ERROR.NONE;
                    break;
            }
        }
    }
}