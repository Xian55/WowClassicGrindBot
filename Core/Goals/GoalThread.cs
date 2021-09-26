using Core.GOAP;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Goals
{
    public class GoalThread
    {
        private readonly ILogger logger;
        private readonly ConfigurableInput input;

        private readonly PlayerReader playerReader;
        private readonly GoapAgent goapAgent;

        private GoapGoal? currentGoal;
        public bool Active { get; set; }

        public GoalThread(ILogger logger, ConfigurableInput input, PlayerReader playerReader, GoapAgent goapAgent)
        {
            this.logger = logger;
            this.input = input;
            this.playerReader = playerReader;
            this.goapAgent = goapAgent;
        }

        public void OnActionEvent(object sender, ActionEventArgs e)
        {
            if (e.Key == GoapKey.abort)
            {
                logger.LogInformation($"Abort from: {sender.GetType().Name}");

                var location = this.playerReader.PlayerLocation;
                input?.TapHearthstone();
                Active = false;
            }
        }

        public async Task GoapPerformGoal()
        {
            if (this.goapAgent != null)
            {
                var newGoal = await this.goapAgent.GetAction();

                if (newGoal != null)
                {
                    if (newGoal != this.currentGoal)
                    {
                        this.currentGoal?.DoReset();
                        this.currentGoal = newGoal;

                        logger.LogInformation("---------------------------------");
                        logger.LogInformation($"New Plan= {newGoal.GetType().Name}");
                        
                        try
                        {
                            await this.currentGoal.OnEnter();
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, $"OnEnter on {newGoal.GetType().Name}");
                        }
                    }
                    else if(!this.currentGoal.Repeatable)
                    {
                        logger.LogInformation($"Current Plan= {newGoal.GetType().Name} is not Repeatable!");
                        return;
                    }

                    try
                    {
                        await newGoal.PerformAction();
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, $"PerformAction on {newGoal.GetType().Name}");
                    }
                }
                else
                {
                    logger.LogInformation($"New Plan= NULL");
                    Thread.Sleep(50);
                }
            }
        }
    }
}