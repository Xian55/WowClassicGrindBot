using Core.Goals;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SharedLib;
using Game;

namespace Core.GOAP
{
    public sealed class GoapAgent
    {
        private ILogger logger;
        private WowProcess wowProcess;
        private ConfigurableInput input;

        private readonly BagReader bagReader;

        private GoapPlanner planner;
        public IEnumerable<GoapGoal> AvailableGoals { get; set; }
        
        private PlayerReader playerReader;
        private ClassConfiguration classConfiguration;

        public GoapGoal? CurrentGoal { get; set; }
        public HashSet<KeyValuePair<GoapKey, object>> WorldState { get; private set; } = new HashSet<KeyValuePair<GoapKey, object>>();
        private IBlacklist blacklist;

        private readonly Wait wait;

        public GoapAgent(ILogger logger, WowProcess wowProcess, ConfigurableInput input, PlayerReader playerReader, HashSet<GoapGoal> availableGoals, IBlacklist blacklist,  ClassConfiguration classConfiguration, BagReader bagReader, Wait wait)
        {
            this.logger = logger;
            this.wowProcess = wowProcess;
            this.input = input;

            this.playerReader = playerReader;
            this.AvailableGoals = availableGoals.OrderBy(a => a.CostOfPerformingAction);
            this.blacklist = blacklist;

            this.planner = new GoapPlanner(logger);
            this.classConfiguration = classConfiguration;
            this.bagReader = bagReader;

            this.wait = wait;
        }

        public void UpdateWorldState()
        {
            WorldState = GetWorldState(playerReader);
        }

        public async Task<GoapGoal?> GetAction()
        {
            if (playerReader.HealthPercent > 1 && blacklist.IsTargetBlacklisted())
            {
                logger.LogWarning("Target is blacklisted");
                await input.TapStopAttack("Target is blacklisted");
                await input.TapClearTarget("Target is blacklisted");
                UpdateWorldState();
            }

            var goal = new HashSet<KeyValuePair<GoapKey, GoapPreCondition>>();

            //Plan
            Queue<GoapGoal> plan = planner.Plan(AvailableGoals, WorldState, goal);
            if (plan != null && plan.Count > 0)
            {
                CurrentGoal = plan.Peek();
            }
            else
            {
                logger.LogInformation($"Target Health: {playerReader.TargetHealth}, max {playerReader.TargetMaxHealth}, dead {playerReader.PlayerBitValues.TargetIsDead}");

                if (classConfiguration.Mode != Mode.AttendedGrind)
                {
                    await input.TapNearestTarget("GoapAgent");
                    await wait.Update(1);
                    if (playerReader.HasTarget && !playerReader.PlayerBitValues.TargetOfTargetIsPlayer)
                    {
                        await input.TapClearTarget("GoapAgent");
                        await wait.Update(1);
                    }
                }
            }

            return CurrentGoal;
        }

        private HashSet<KeyValuePair<GoapKey, object>> GetWorldState(PlayerReader playerReader)
        {
            var state = new HashSet<KeyValuePair<GoapKey, object>>
            {
                new KeyValuePair<GoapKey, object>(GoapKey.hastarget,!blacklist.IsTargetBlacklisted() && (!string.IsNullOrEmpty(playerReader.Target)|| playerReader.TargetHealth>0)),
                new KeyValuePair<GoapKey, object>(GoapKey.targetisalive,!string.IsNullOrEmpty(this.playerReader.Target) &&  (!playerReader.PlayerBitValues.TargetIsDead || playerReader.TargetHealth>0)),
                new KeyValuePair<GoapKey, object>(GoapKey.incombat, playerReader.PlayerBitValues.PlayerInCombat ),
                new KeyValuePair<GoapKey, object>(GoapKey.withinpullrange, playerReader.WithInPullRange),
                new KeyValuePair<GoapKey, object>(GoapKey.incombatrange, playerReader.WithInCombatRange),
                new KeyValuePair<GoapKey, object>(GoapKey.pulled, false),
                new KeyValuePair<GoapKey, object>(GoapKey.isdead, playerReader.HealthPercent==0),
                new KeyValuePair<GoapKey, object>(GoapKey.isswimming, playerReader.PlayerBitValues.IsSwimming),
                new KeyValuePair<GoapKey, object>(GoapKey.itemsbroken,playerReader.PlayerBitValues.ItemsAreBroken),
                new KeyValuePair<GoapKey, object>(GoapKey.producedcorpse, playerReader.LastCombatKillCount>0),
                new KeyValuePair<GoapKey, object>(GoapKey.consumecorpse, playerReader.ShouldConsumeCorpse)
        };

            actionState.ToList().ForEach(kv => state.Add(kv));

            return state;
        }

        private Dictionary<GoapKey, object> actionState = new Dictionary<GoapKey, object>();

        public void OnActionEvent(object sender, ActionEventArgs e)
        {
            if (!actionState.ContainsKey(e.Key))
            {
                actionState.Add(e.Key, e.Value);
            }
            else
            {
                actionState[e.Key] = e.Value;
            }
        }
    }
}