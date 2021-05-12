using Core.GOAP;
using Core.Looting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Core.Goals
{
    public class LootGoal : GoapGoal
    {
        public override float CostOfPerformingAction { get => 4.4f; }

        private ILogger logger;
        private readonly ConfigurableInput input;

        private readonly PlayerReader playerReader;
        private readonly StopMoving stopMoving;
        private readonly BagReader bagReader;
        private readonly ClassConfiguration classConfiguration;
        private readonly NpcNameFinder npcNameFinder;

        private bool debug = true;
        private bool outOfCombat = false;

        public LootGoal(ILogger logger, ConfigurableInput input, PlayerReader playerReader, BagReader bagReader, StopMoving stopMoving,  ClassConfiguration classConfiguration, NpcNameFinder npcNameFinder)
        {
            this.logger = logger;
            this.input = input;
            this.playerReader = playerReader;
            this.stopMoving = stopMoving;
            this.bagReader = bagReader;
            
            this.classConfiguration = classConfiguration;
            this.npcNameFinder = npcNameFinder;

            outOfCombat = playerReader.PlayerBitValues.PlayerInCombat;
        }

        public virtual void AddPreconditions()
        {
            AddPrecondition(GoapKey.shouldloot, true);
        }

        public override bool CheckIfActionCanRun()
        {
            return !bagReader.BagsFull && playerReader.ShouldConsumeCorpse;
        }

        public override void ResetBeforePlanning()
        {
            outOfCombat = playerReader.PlayerBitValues.PlayerInCombat;
            base.ResetBeforePlanning();
        }

        public override async Task PerformAction()
        {
            WowPoint lastPosition = playerReader.PlayerLocation;

            Log("Search for corpse");
            npcNameFinder.ChangeNpcType(NpcNameFinder.NPCType.Corpse);

            await stopMoving.Stop();
            await npcNameFinder.WaitForNUpdate(1);

            bool lootSuccess = await npcNameFinder.FindByCursorType(Cursor.CursorClassification.Loot);
            if (lootSuccess)
            {
                Log("Found corpse - interact with it");
                await playerReader.WaitForNUpdate(1);

                if (classConfiguration.Skin)
                {
                    var targetSkinnable = !playerReader.Unskinnable;
                    AddEffect(GoapKey.shouldskin, targetSkinnable);
                    Log($"Should skin ? {targetSkinnable}");
                    SendActionEvent(new ActionEventArgs(GoapKey.shouldskin, targetSkinnable));
                }

                bool hadToMove = false;
                if (IsPlayerMoving(lastPosition))
                {
                    hadToMove = true;
                    Log("Goto corpse - Wait till player become stil!");
                }

                while (IsPlayerMoving(lastPosition))
                {
                    lastPosition = playerReader.PlayerLocation;
                    if (!await Wait(100, DiDEnteredCombat()))
                    {
                        await AquireTarget();
                        return;
                    }
                }

                // TODO: damn spell batching
                // arriving to the corpse min distance to interact location
                // and says you are too far away
                // so have to wait and retry the action
                // at this point the player have a target
                // might be a good idea to check the last error message :shrug:
                if (hadToMove)
                {
                    if (!await Wait(200, DiDEnteredCombat()))
                    {
                        await AquireTarget();
                        return;
                    }
                }
                    

                await input.TapInteractKey("Approach corpse");

                // TODO: find a better way to get notified about the successful loot
                // challlange:
                // - the mob might have no loot at all so cant check inventory change
                // - loot window could be checked
                /*
                if (!await Wait(400, DiDEnteredCombat()))
                {
                    await AquireTarget();
                    return;
                }
                */
                if (!await Wait(4000, DiDEnteredCombat()))
                {
                    await AquireTarget();
                    return;
                }

                Log("Loot Successfull");

                await GoalExit();
            }
            else
            {
                Log($"No corpse found - Npc Count: {npcNameFinder.NpcCount}");

                if (!await Wait(100, DiDEnteredCombat()))
                {
                    await AquireTarget();
                }
                else
                {
                    await GoalExit();
                }
            }
        }

        public override void OnActionEvent(object sender, ActionEventArgs e)
        {
            if (sender != this)
            {
                outOfCombat = true;
            }
        }

        public async Task<bool> DiDEnteredCombat()
        {
            await Task.Delay(0);
            if (!outOfCombat && !playerReader.PlayerBitValues.PlayerInCombat)
            {
                Log("Combat Leave");
                outOfCombat = true;
                return false;
            }

            if (outOfCombat && playerReader.PlayerBitValues.PlayerInCombat)
            {
                Log("Combat Enter");
                return true;
            }

            return false;
        }

        private async Task AquireTarget()
        {
            if (this.playerReader.PlayerBitValues.PlayerInCombat && this.playerReader.PetHasTarget)
            {
                await input.TapTargetPet();
                Log($"Pets target {this.playerReader.TargetTarget}");
                if (this.playerReader.TargetTarget == TargetTargetEnum.PetHasATarget)
                {
                    Log("Found target by pet");
                    await input.TapTargetOfTarget();
                    SendActionEvent(new ActionEventArgs(GoapKey.shouldloot, false));
                    SendActionEvent(new ActionEventArgs(GoapKey.newtarget, true));
                    SendActionEvent(new ActionEventArgs(GoapKey.hastarget, true));
                    return;
                }

                await input.TapNearestTarget();
                await playerReader.WaitForNUpdate(1);
                if (this.playerReader.HasTarget && playerReader.PlayerBitValues.TargetInCombat)
                {
                    if (playerReader.PlayerBitValues.TargetOfTargetIsPlayer)
                    {
                        Log("Found from nearest target");
                        SendActionEvent(new ActionEventArgs(GoapKey.shouldloot, false));
                        SendActionEvent(new ActionEventArgs(GoapKey.newtarget, true));
                        SendActionEvent(new ActionEventArgs(GoapKey.hastarget, true));
                        return;
                    }
                }

                await input.TapClearTarget();
                Log("No target found");
            }
        }

        private bool IsPlayerMoving(WowPoint lastPos)
        {
            var distance = WowPoint.DistanceTo(lastPos, playerReader.PlayerLocation);
            return distance > 0.5f;
        }


        private async Task GoalExit()
        {
            await input.TapLastTargetKey("Last target key");
            await input.TapInteractKey("Interract key");
            await Task.Delay(5000);
            AddEffect(GoapKey.shouldloot, false);
            SendActionEvent(new ActionEventArgs(GoapKey.shouldloot, false));

            if (!classConfiguration.Skin)
            {
                npcNameFinder.ChangeNpcType(NpcNameFinder.NPCType.Enemy);
            }

            await input.TapClearTarget();
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