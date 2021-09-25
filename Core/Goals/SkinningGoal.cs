using Core.GOAP;
using Core.Looting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Core.Goals
{
    public class SkinningGoal : GoapGoal
    {
        public override float CostOfPerformingAction { get => 4.6f; }

        private ILogger logger;
        private readonly ConfigurableInput input;

        private readonly Wait wait;
        private readonly PlayerReader playerReader;
        private readonly StopMoving stopMoving;
        private readonly BagReader bagReader;
        private readonly EquipmentReader equipmentReader;
        private readonly NpcNameFinder npcNameFinder;
        private readonly CombatUtil combatUtil;

        private long lastLoot;

        public SkinningGoal(ILogger logger, ConfigurableInput input, Wait wait, PlayerReader playerReader, BagReader bagReader, EquipmentReader equipmentReader, StopMoving stopMoving,  NpcNameFinder npcNameFinder, CombatUtil combatUtil)
        {
            this.logger = logger;
            this.input = input;

            this.wait = wait;
            this.playerReader = playerReader;
            this.stopMoving = stopMoving;
            this.bagReader = bagReader;
            this.equipmentReader = equipmentReader;

            this.npcNameFinder = npcNameFinder;
            this.combatUtil = combatUtil;

            AddPrecondition(GoapKey.incombat, false);
            AddPrecondition(GoapKey.shouldskin, true);

            AddEffect(GoapKey.shouldskin, false);
        }

        public override bool CheckIfActionCanRun()
        {
            return !bagReader.BagsFull && 
                playerReader.ShouldConsumeCorpse && 
                (
                bagReader.HasItem(7005) ||
                bagReader.HasItem(12709) ||
                bagReader.HasItem(19901) ||

                equipmentReader.HasItem(7005) ||
                equipmentReader.HasItem(12709) ||
                equipmentReader.HasItem(19901)
                );
        }

        public override async Task PerformAction()
        {
            lastLoot = playerReader.LastLootTime;

            combatUtil.Update();

            Log("Try to find Corpse");
            npcNameFinder.ChangeNpcType(NpcNameFinder.NPCType.Corpse);
            //await stopMoving.Stop();
            await npcNameFinder.WaitForNUpdate(1);

            if (await combatUtil.EnteredCombat())
            {
                if(await combatUtil.AquiredTarget())
                {
                    EmergencyExit();
                    return;
                }
            }

            bool foundCursor = await npcNameFinder.FindByCursorType(Cursor.CursorClassification.Skin);
            if (foundCursor)
            {
                Log("Found corpse - interacted with right click");
                await wait.Update(1);

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

                //this.playerReader.LastUIErrorMessage = UI_ERROR.NONE;

                // wait until start casting
                await wait.Interrupt(500, () => playerReader.IsCasting);
                Log("Started casting...");

                await wait.Interrupt(3000, () => !playerReader.IsCasting);
                Log("Cast finished!");

                // Wait for to update the LastUIErrorMessage
                await wait.Update(1);
                var lastError = playerReader.LastUIErrorMessage;
                if (lastError != UI_ERROR.ERR_SPELL_FAILED_S /*&& lastLoot != playerReader.LastLootTime*/)
                {
                    this.playerReader.LastUIErrorMessage = UI_ERROR.NONE;
                    Log("Skinning Successful!");
                    await GoalExit();
                }
                else
                {
                    Log("Skinning Failed! Retry...");
                }
            }
            else
            {
                Log($"Target is not skinnable - NPC Count: {npcNameFinder.NpcCount}");
                await GoalExit();
            }
        }

        private async Task GoalExit()
        {
            if (!await wait.Interrupt(1000, () => lastLoot != playerReader.LastLootTime))
            {
                Log($"Skin-Loot Successfull");
            }
            else
            {
                Log($"Skin-Loot Failed");
            }

            lastLoot = playerReader.LastLootTime;

            EmergencyExit();
            await npcNameFinder.WaitForNUpdate(1);

            if (playerReader.HasTarget && playerReader.PlayerBitValues.TargetIsDead)
            {
                await input.TapClearTarget();
                await wait.Update(1);
            }
        }

        private void EmergencyExit()
        {
            npcNameFinder.ChangeNpcType(NpcNameFinder.NPCType.Enemy);
            SendActionEvent(new ActionEventArgs(GoapKey.shouldskin, false));
        }

        private void Log(string text)
        {
            logger.LogInformation($"{this.GetType().Name}: {text}");
        }

    }
}
