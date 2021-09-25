using Core.Database;
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
        public override bool Repeatable => false;

        private ILogger logger;
        private readonly ConfigurableInput input;

        private readonly Wait wait;
        private readonly PlayerReader playerReader;
        private readonly AreaDB areaDb;
        private readonly StopMoving stopMoving;
        private readonly BagReader bagReader;
        private readonly ClassConfiguration classConfiguration;
        private readonly NpcNameFinder npcNameFinder;
        private readonly CombatUtil combatUtil;

        private bool debug = true;
        private long lastLoot;

        public LootGoal(ILogger logger, ConfigurableInput input, Wait wait, PlayerReader playerReader, BagReader bagReader, StopMoving stopMoving,  ClassConfiguration classConfiguration, NpcNameFinder npcNameFinder, CombatUtil combatUtil, AreaDB areaDb)
        {
            this.logger = logger;
            this.input = input;
            this.wait = wait;
            this.playerReader = playerReader;
            this.areaDb = areaDb;
            this.stopMoving = stopMoving;
            this.bagReader = bagReader;
            
            this.classConfiguration = classConfiguration;
            this.npcNameFinder = npcNameFinder;
            this.combatUtil = combatUtil;
        }

        public virtual void AddPreconditions()
        {
            AddPrecondition(GoapKey.shouldloot, true);
            AddEffect(GoapKey.shouldloot, false);
        }

        public override bool CheckIfActionCanRun()
        {
            return !bagReader.BagsFull && playerReader.ShouldConsumeCorpse;
        }

        public override async Task PerformAction()
        {
            lastLoot = playerReader.LastLootTime;
            combatUtil.Update();

            Log("Search for corpse");
            npcNameFinder.ChangeNpcType(NpcNameFinder.NPCType.Corpse);

            await npcNameFinder.WaitForNUpdate(1);
            bool foundCursor = await npcNameFinder.FindByCursorType(Cursor.CursorClassification.Loot);
            if (foundCursor)
            {
                Log("Found corpse - clicked");
                await wait.Update(1);

                CheckForSkinning();

                (bool foundTarget, bool moved) = await combatUtil.FoundTargetWhileMoved();
                if (foundTarget)
                {
                    Log("Interrupted!");
                    EmergencyExit();
                    return;
                }

                if (moved)
                {
                    await input.TapInteractKey($"{GetType().Name}: Had to move so interact again");
                    await wait.Update(1);
                }
            }
            else
            {
                await input.TapLastTargetKey($"{GetType().Name}: No corpse name found - check last dead target exists");
                await wait.Update(2);
                if (playerReader.HasTarget)
                {
                    if(playerReader.PlayerBitValues.TargetIsDead)
                    {
                        CheckForSkinning();

                        await input.TapInteractKey($"{GetType().Name}: Found last dead target");
                        await wait.Update(1);

                        (bool foundTarget, bool moved) = await combatUtil.FoundTargetWhileMoved();
                        if (foundTarget)
                        {
                            Log("Goal interrupted!");
                            EmergencyExit();
                            return;
                        }

                        if (moved)
                        {
                            await input.TapInteractKey($"{GetType().Name}: Last dead target double");
                        }
                    }
                    else
                    {
                        await input.TapClearTarget($"{GetType().Name}: Dont attak the target!");
                    }
                }
            }

            await GoalExit();
        }

        private void CheckForSkinning()
        {
            if (classConfiguration.Skin)
            {
                var targetSkinnable = !playerReader.Unskinnable;

                if (areaDb.CurrentArea != null && areaDb.CurrentArea.skinnable != null)
                {
                    targetSkinnable = areaDb.CurrentArea.skinnable.Contains(playerReader.TargetId);
                }

                Log($"Should skin ? {targetSkinnable}");
                AddEffect(GoapKey.shouldskin, targetSkinnable);
                SendActionEvent(new ActionEventArgs(GoapKey.shouldskin, targetSkinnable));
            }
        }

        private async Task GoalExit()
        {
            if (!await wait.Interrupt(1000, () => lastLoot != playerReader.LastLootTime))
            {
                Log($"Loot Successfull");
            }
            else
            {
                Log($"Loot Failed");
            }

            lastLoot = playerReader.LastLootTime;

            SendActionEvent(new ActionEventArgs(GoapKey.shouldloot, false));

            if (!classConfiguration.Skin)
            {
                npcNameFinder.ChangeNpcType(NpcNameFinder.NPCType.Enemy);
                await npcNameFinder.WaitForNUpdate(1);
            }

            if (playerReader.HasTarget && playerReader.PlayerBitValues.TargetIsDead)
            {
                await input.TapClearTarget($"{GetType().Name}: Exit Goal");
                await wait.Update(1);
            }
        }

        private void EmergencyExit()
        {
            SendActionEvent(new ActionEventArgs(GoapKey.shouldloot, false));
            npcNameFinder.ChangeNpcType(NpcNameFinder.NPCType.Enemy);
        }

        private void Log(string text)
        {
            if (debug)
            {
                logger.LogInformation($"{GetType().Name}: {text}");
            }
        }
    }
}