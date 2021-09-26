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

            playerReader.LastUIErrorMessage = UI_ERROR.NONE;
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

                await ReactToLastUIErrorMessage($"{GetType().Name}: CastIfReady-NoHasCastBar");
            }
            else
            {
                var startedCasting = await wait.InterruptTask(MaxWaitTimeMs, () => playerReader.IsCasting || playerReader.LastUIErrorMessage != UI_ERROR.NONE);
                if (!startedCasting.Item1)
                {
                    item.LogInformation($" input registered after {startedCasting.Item2}ms");
                }

                if (!playerReader.IsCasting && playerReader.HasTarget)
                {
                    await ReactToLastUIErrorMessage($"{GetType().Name}: CastIfReady-HasCastBar-NotCasting-HasTarget");

                    if (item.StopBeforeCast)
                    {
                        await stopMoving.Stop();
                        await wait.Update(1);
                    }

                    item.LogInformation($"Not casting, pressing it again");

                    playerReader.LastUIErrorMessage = UI_ERROR.NONE;
                    await PressKey(item.ConsoleKey, item.Name, item.PressDuration);
                    await wait.InterruptTask(MaxWaitTimeMs, () => playerReader.IsCasting || playerReader.LastUIErrorMessage != UI_ERROR.NONE);

                    if (!playerReader.IsCasting && playerReader.HasTarget)
                    {
                        item.LogInformation($"Still not casting !");
                        await ReactToLastUIErrorMessage($"{GetType().Name}: CastIfReady-HasCastBar-NotCasting-HasTarget-2ndTime");
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

        public virtual async Task ReactToLastUIErrorMessage(string source)
        {
            //var lastError = playerReader.LastUIErrorMessage;
            switch (playerReader.LastUIErrorMessage)
            {
                case UI_ERROR.NONE:
                    break;
                case UI_ERROR.ERR_SPELL_OUT_OF_RANGE:
                    logger.LogInformation($"React to {UI_ERROR.ERR_SPELL_OUT_OF_RANGE} -- Start moving forward -- {source}");

                    input.SetKeyState(ConsoleKey.UpArrow, true, false, "");
                    playerReader.LastUIErrorMessage = UI_ERROR.NONE;
                    break;
                case UI_ERROR.ERR_BADATTACKFACING:

                    if (playerReader.IsInMeleeRange)
                    {
                        logger.LogInformation($"React to {UI_ERROR.ERR_BADATTACKFACING} -- Interact! -- {source}");
                        await input.TapInteractKey("");
                    }
                    else
                    {
                        logger.LogInformation($"React to {UI_ERROR.ERR_BADATTACKFACING} -- Turning 180! -- {source}");

                        double desiredDirection = playerReader.Direction + Math.PI;
                        desiredDirection = desiredDirection > Math.PI * 2 ? desiredDirection - (Math.PI * 2) : desiredDirection;
                        await direction.SetDirection(desiredDirection, new WowPoint(0, 0), "");
                    }

                    playerReader.LastUIErrorMessage = UI_ERROR.NONE;
                    break;
                case UI_ERROR.SPELL_FAILED_MOVING:
                    logger.LogInformation($"React to {UI_ERROR.SPELL_FAILED_MOVING} -- Stop moving! -- {source}");

                    await stopMoving.Stop();
                    await wait.Update(1);
                    playerReader.LastUIErrorMessage = UI_ERROR.NONE;
                    break;
                case UI_ERROR.ERR_SPELL_FAILED_ANOTHER_IN_PROGRESS:
                    logger.LogInformation($"React to {UI_ERROR.ERR_SPELL_FAILED_ANOTHER_IN_PROGRESS} -- Wait till casting! -- {source}");
                    await wait.While(() => playerReader.IsCasting);
                    break;
                case UI_ERROR.ERR_SPELL_COOLDOWN:
                    logger.LogInformation($"Cant react to {UI_ERROR.ERR_SPELL_FAILED_ANOTHER_IN_PROGRESS} -- {source}");
                    break;
                default:
                    logger.LogInformation($"Didn't know how to React to {playerReader.LastUIErrorMessage} -- {source}");
                    break;
                //case UI_ERROR.ERR_SPELL_FAILED_S:
                //case UI_ERROR.ERR_BADATTACKPOS:
                //case UI_ERROR.ERR_SPELL_OUT_OF_RANGE:
                //case UI_ERROR.ERR_AUTOFOLLOW_TOO_FAR:
                //    this.playerReader.LastUIErrorMessage = UI_ERROR.NONE;
                //    break;
            }
        }
    }
}