using Core.GOAP;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using Core.Database;

namespace Core.Goals
{
    public class PostKillLootGoal : LootGoal
    {
        public override float CostOfPerformingAction { get => 4.5f; }

        public PostKillLootGoal(ILogger logger, ConfigurableInput input, Wait wait, PlayerReader playerReader, BagReader bagReader, StopMoving stopMoving, ClassConfiguration classConfiguration, NpcNameFinder npcNameFinder, CombatUtil combatUtil, AreaDB areaDb)
            : base(logger, input, wait, playerReader, bagReader, stopMoving, classConfiguration, npcNameFinder, combatUtil, areaDb)
        {
        }

        public override void AddPreconditions()
        {
            AddPrecondition(GoapKey.incombat, false);
            AddPrecondition(GoapKey.hastarget, false);
            AddPrecondition(GoapKey.shouldloot, true);
        }

        public override async Task PerformAction()
        {
            SendActionEvent(new ActionEventArgs(GoapKey.shouldloot, false));
            await base.PerformAction();
        }
    }
}