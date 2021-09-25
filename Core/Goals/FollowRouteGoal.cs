using Core.GOAP;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Core.Goals
{
    public class FollowRouteGoal : GoapGoal, IRouteProvider
    {
        public override float CostOfPerformingAction { get => 20f; }

        private double RADIAN = Math.PI * 2;

        private readonly ILogger logger;
        private readonly ConfigurableInput input;

        private readonly Wait wait;
        private readonly PlayerReader playerReader;
        private readonly IPlayerDirection playerDirection;
        private readonly StopMoving stopMoving;
        private readonly NpcNameFinder npcNameFinder;
        private readonly StuckDetector stuckDetector;
        private readonly ClassConfiguration classConfiguration;
        private readonly IPPather pather;
        private readonly MountHandler mountHandler;

        private const int MinDistance = 8;

        private double lastDistance = 999;
        public DateTime LastActive { get; set; } = DateTime.Now.AddDays(-1);

        private Random random = new Random();

        private readonly IBlacklist blacklist;
        private bool shouldMount = true;

        private readonly List<WowPoint> pointsList;
        private Stack<WowPoint> routeToWaypoint = new Stack<WowPoint>();
        private Stack<WowPoint> wayPoints = new Stack<WowPoint>();
        private DateTime LastReset = DateTime.Now;

        public List<WowPoint> PathingRoute()
        {
            return routeToWaypoint.ToList();
        }

        public WowPoint? NextPoint()
        {
            return routeToWaypoint.Count == 0 ? null : routeToWaypoint.Peek();
        }


        private bool debug = false;

        private double avgDistance = 0;
        private bool firstLoad = true;

        public FollowRouteGoal(ILogger logger, ConfigurableInput input, Wait wait, PlayerReader playerReader,  IPlayerDirection playerDirection, List<WowPoint> points, StopMoving stopMoving, NpcNameFinder npcNameFinder, IBlacklist blacklist, StuckDetector stuckDetector, ClassConfiguration classConfiguration, IPPather pather, MountHandler mountHandler)
        {
            this.logger = logger;
            this.input = input;

            this.wait = wait;
            this.playerReader = playerReader;
            this.playerDirection = playerDirection;
            this.stopMoving = stopMoving;

            this.pointsList = points;

            this.npcNameFinder = npcNameFinder;
            this.blacklist = blacklist;
            
            this.stuckDetector = stuckDetector;
            this.classConfiguration = classConfiguration;
            this.pather = pather;
            this.mountHandler = mountHandler;

            if (classConfiguration.Mode != Mode.AttendedGather)
            {
                AddPrecondition(GoapKey.incombat, false);
            }
        }

        public override bool CheckIfActionCanRun()
        {
            return !playerReader.ShouldConsumeCorpse && playerReader.LastCombatKillCount == 0;
        }

        private void RefillWaypoints(bool findClosest = false)
        {
            if (firstLoad)
            {
                // start path at closest point
                firstLoad = false;
                var me = this.playerReader.PlayerLocation;
                var closest = pointsList.OrderBy(p => WowPoint.DistanceTo(me, p)).FirstOrDefault();

                for (int i = 0; i < pointsList.Count; i++)
                {
                    wayPoints.Push(pointsList[i]);
                    if (pointsList[i] == closest) { break; }
                }
            }
            else
            {
                if (findClosest)
                {
                    pointsList.ForEach(p => wayPoints.Push(p));
                    AdjustNextPointToClosest();
                }
                else
                {
                    pointsList.ForEach(p => wayPoints.Push(p));
                }
            }
        }

        public override void OnActionEvent(object sender, ActionEventArgs e)
        {
            if (sender != this)
            {
                shouldMount = this.classConfiguration.UseMount;
                this.routeToWaypoint.Clear();
            }
        }

        private int lastGatherKey = 0;
        private DateTime lastGatherClick = DateTime.Now.AddSeconds(-10);

        public override async Task PerformAction()
        {
            SendActionEvent(new ActionEventArgs(GoapKey.fighting, false));

            if (playerReader.HasTarget || await AquireTarget())
            {
                await stopMoving.StopTurn();
                return;
            }

            await SwitchGatherType();

            if (this.playerReader.PlayerBitValues.PlayerInCombat && this.classConfiguration.Mode != Mode.AttendedGather) { return; }

            var timeSinceResetSeconds = (DateTime.Now - LastReset).TotalSeconds;
            if ((DateTime.Now - LastActive).TotalSeconds > 10 || routeToWaypoint.Count == 0 || timeSinceResetSeconds > 80)
            {
                logger.LogInformation("Trying to path a new route.");
                // recalculate next waypoint
                var pointsRemoved = 0;
                while (AdjustNextPointToClosest() && pointsRemoved < 5) { pointsRemoved++; };
                await RefillRouteToNextWaypoint(true);
                if (routeToWaypoint.Count == 0)
                {
                    logger.LogError("Didn't found path.");
                }
            }
            else
            {
                var playerLocation = new WowPoint(playerReader.XCoord, playerReader.YCoord);
                if(routeToWaypoint.Count > 0)
                {
                    var distanceToRoute = WowPoint.DistanceTo(playerLocation, routeToWaypoint.Peek());
                    if (routeToWaypoint.Count <= 1 && distanceToRoute > 200)
                    {
                        logger.LogError($"No route To Waypoint or too far {distanceToRoute}>200");
                        routeToWaypoint.Pop();
                        return;
                    }
                }

                //wowProcess.SetKeyState(ConsoleKey.UpArrow, true, false, "FollowRouteAction 1");
                input.SetKeyState(ConsoleKey.UpArrow, true, false);
            }

            bool jumped = await RandomJump();

            var location = new WowPoint(playerReader.XCoord, playerReader.YCoord);
            var distance = WowPoint.DistanceTo(location, routeToWaypoint.Peek());
            var heading = DirectionCalculator.CalculateHeading(location, routeToWaypoint.Peek());

            await AdjustHeading(heading);

            if (lastDistance < distance)
            {
                AdjustNextPointToClosest();

                await playerDirection.SetDirection(heading, routeToWaypoint.Peek(), "Further away");
            }
            else if (!this.stuckDetector.IsGettingCloser())
            {
                // stuck so jump
                input.SetKeyState(ConsoleKey.UpArrow, true, false, "FollowRouteAction 2");
                await wait.Update(1);
                if (HasBeenActiveRecently())
                {
                    await this.stuckDetector.Unstick();
                    distance = WowPoint.DistanceTo(location, routeToWaypoint.Peek());
                }
                else
                {
                    await wait.Update(1);
                    logger.LogInformation("Resuming movement");
                }
            }
            else // distance closer
            {
                await AdjustHeading(heading);
            }

            lastDistance = distance;

            if (distance < PointReachedDistance(MinDistance))
            {
                Log($"Move to next point");

                ReduceRouteByDistance(MinDistance);

                lastDistance = 999;
                if (routeToWaypoint.Count == 0)
                {
                    wayPoints.Pop();

                    await RefillRouteToNextWaypoint(false);
                }

                this.stuckDetector.SetTargetLocation(this.routeToWaypoint.Peek());

                heading = DirectionCalculator.CalculateHeading(location, routeToWaypoint.Peek());
                await playerDirection.SetDirection(heading, routeToWaypoint.Peek(), "Move to next point");
            }

            // should mount
            await MountIfRequired(jumped);

            LastActive = DateTime.Now;
        }

        private async Task SwitchGatherType()
        {
            if (this.classConfiguration.Mode == Mode.AttendedGather && this.lastGatherClick.AddSeconds(3) < DateTime.Now && this.classConfiguration.GatherFindKeyConfig.Count > 0)
            {
                lastGatherKey++;
                if (lastGatherKey >= this.classConfiguration.GatherFindKeyConfig.Count)
                {
                    lastGatherKey = 0;
                }

                await input.KeyPress(classConfiguration.GatherFindKeyConfig[lastGatherKey].ConsoleKey, 200, "Gatherkey 1");
                lastGatherClick = DateTime.Now;
            }
        }

        private async Task<bool> AquireTarget()
        {
            if (this.classConfiguration.Mode != Mode.AttendedGather)
            {
                if (!this.playerReader.PlayerBitValues.PlayerInCombat && classConfiguration.TargetNearestTarget.MillisecondsSinceLastClick > 1000)
                {
                    if (await LookForTarget())
                    {
                        if (this.playerReader.HasTarget && !playerReader.PlayerBitValues.TargetIsDead)
                        {
                            logger.LogInformation("Has target!");
                            return true;
                        }
                        else
                        {
                            await input.TapClearTarget("Target is dead!");
                            await wait.Update(1);
                        }
                    }
                }
            }

            return false;
        }

        private async Task MountIfRequired(bool jumped)
        {
            if (shouldMount && !this.playerReader.PlayerBitValues.IsMounted && !playerReader.PlayerBitValues.PlayerInCombat)
            {
                if (this.classConfiguration.Mode != Mode.AttendedGather)
                {
                    shouldMount = false;
                    //if (await LookForTarget()) { return; }
                }

                Log("Mounting if level >=40 (druid 30) and no NPC in sight");
                if (!this.npcNameFinder.MobsVisible)
                {
                    if (jumped) 
                        await Task.Delay(700);

                    await mountHandler.MountUp();
                }
                else
                {
                    logger.LogInformation("Not mounting as can see NPC.");
                }
                input.SetKeyState(ConsoleKey.UpArrow, true, false, "Move Forward");
            }
        }

        private void ReduceRouteByDistance(int minDistance)
        {
            if (routeToWaypoint.Any())
            {
                var location = new WowPoint(playerReader.XCoord, playerReader.YCoord);
                var distance = WowPoint.DistanceTo(location, routeToWaypoint.Peek());
                while (distance < PointReachedDistance(minDistance - 1) && routeToWaypoint.Any())
                {
                    routeToWaypoint.Pop();
                    if (routeToWaypoint.Any())
                    {
                        distance = WowPoint.DistanceTo(location, routeToWaypoint.Peek());
                    }
                }
            }
        }

        private void SimplyfyRouteToWaypoint()
        {
            var simple = PathSimplify.Simplify(routeToWaypoint.ToArray(), 0.01f);
            simple.Reverse();
            routeToWaypoint = new Stack<WowPoint>(simple);
        }

        private async Task RefillRouteToNextWaypoint(bool forceUsePathing)
        {
            LastReset = DateTime.Now;

            if (wayPoints.Count == 0)
            {
                RefillWaypoints();
                CalculateAvgDistance();
            }

            this.routeToWaypoint.Clear();

            var location = new WowPoint(playerReader.XCoord, playerReader.YCoord);
            var heading = DirectionCalculator.CalculateHeading(location, wayPoints.Peek());
            await playerDirection.SetDirection(heading, wayPoints.Peek(), "Reached waypoint").ConfigureAwait(false);

            //Create path back to route
            var distance = WowPoint.DistanceTo(location, wayPoints.Peek());
            if (forceUsePathing || distance > 200)
            {
                await this.stopMoving.Stop();
                var path = await this.pather.FindRouteTo(this.playerReader, wayPoints.Peek());
                path.Reverse();
                path.ForEach(p => this.routeToWaypoint.Push(p));
            }

            //this.ReduceRouteByDistance();
            SimplyfyRouteToWaypoint();
            if (this.routeToWaypoint.Count == 0)
            {
                this.routeToWaypoint.Push(this.wayPoints.Peek());
            }

            this.stuckDetector.SetTargetLocation(this.routeToWaypoint.Peek());
        }

        private async Task AdjustHeading(double heading)
        {
            var diff1 = Math.Abs(RADIAN + heading - playerReader.Direction) % RADIAN;
            var diff2 = Math.Abs(heading - playerReader.Direction - RADIAN) % RADIAN;

            var wanderAngle = 0.3;

            if (this.classConfiguration.Mode != Mode.AttendedGather)
            {
                wanderAngle = 0.05;
            }

            var diff = Math.Min(diff1, diff2);
            if (diff > wanderAngle)
            {
                if(diff > wanderAngle * 3)
                {
                    await stopMoving.StopForward();
                }

                await playerDirection.SetDirection(heading, routeToWaypoint.Peek(), "Correcting direction");
            }
            else
            {
                //logger.LogInformation($"Direction ok heading: {heading}, player direction {playerReader.Direction}");
            }
        }

        private int PointReachedDistance(int distance)
        {
            if (this.playerReader.PlayerClass == PlayerClassEnum.Druid && this.playerReader.Druid_ShapeshiftForm == ShapeshiftForm.Druid_Travel)
            {
                return 50;
            }

            return (this.playerReader.PlayerBitValues.IsMounted ? 50 : distance);
        }

        private async Task<bool> LookForTarget()
        {
            if (this.playerReader.HasTarget && !this.playerReader.PlayerBitValues.TargetIsDead && !blacklist.IsTargetBlacklisted())
            {
                return true;
            }
            else
            {
                await input.TapNearestTarget();
                if (!playerReader.HasTarget)
                {
                    npcNameFinder.ChangeNpcType(NpcNameFinder.NPCType.Enemy);
                    if(npcNameFinder.NpcCount > 0)
                    {
                        await this.npcNameFinder.TargetingAndClickNpc(0, true);
                        await wait.Update(1);
                    }
                }
            }

            if (this.playerReader.HasTarget && !blacklist.IsTargetBlacklisted())
            {
                if (playerReader.PlayerBitValues.IsMounted)
                {
                    await input.TapDismount();
                }
                await input.TapInteractKey("FollowRouteAction 4");
                return true;
            }
            return false;
        }

        private bool HasBeenActiveRecently()
        {
            return (DateTime.Now - LastActive).TotalSeconds < 2;
        }

        private bool AdjustNextPointToClosest()
        {
            if (wayPoints.Count < 2) { return false; }

            var A = wayPoints.Pop();
            var B = wayPoints.Peek();
            var result = GetClosestPointOnLineSegment(A.Vector2(), B.Vector2(), new Vector2((float)this.playerReader.XCoord, (float)this.playerReader.YCoord));
            var newPoint = new WowPoint(result.X, result.Y);
            if (WowPoint.DistanceTo(newPoint, wayPoints.Peek()) >= 4)
            {
                wayPoints.Push(newPoint);
                logger.LogInformation($"Adjusted resume point");
                return false;
            }
            else
            {
                logger.LogInformation($"Skipped next point in path");
                // skiped next point
                return true;
            }
        }

        private async Task<bool> RandomJump()
        {
            if (classConfiguration.Jump.MillisecondsSinceLastClick > 10000)
            {
                if (random.Next(1) == 0 /*&& HasBeenActiveRecently()*/)
                {
                    logger.LogInformation($"Random jump");

                    await input.TapJump();
                    return true;
                }
            }
            return false;
        }

        public async Task Reset()
        {
            await this.stopMoving.Stop();
        }

        public override async Task Abort()
        {
            await this.stopMoving.Stop();
        }

        public static Vector2 GetClosestPointOnLineSegment(Vector2 A, Vector2 B, Vector2 P)
        {
            Vector2 AP = P - A;       //Vector from A to P
            Vector2 AB = B - A;       //Vector from A to B

            float magnitudeAB = AB.LengthSquared();     //Magnitude of AB vector (it's length squared)
            float ABAPproduct = Vector2.Dot(AP, AB);    //The DOT product of a_to_p and a_to_b
            float distance = ABAPproduct / magnitudeAB; //The normalized "distance" from a to your closest point

            if (distance < 0)     //Check if P projection is over vectorAB
            {
                return A;
            }
            else if (distance > 1)
            {
                return B;
            }
            else
            {
                return A + AB * distance;
            }
        }

        private void CalculateAvgDistance()
        {
            if(pointsList.Count < 2)
            {
                avgDistance = 5;
                return;
            }

            var distances = pointsList.Zip(pointsList.Skip(1), WowPoint.DistanceTo);
            avgDistance = distances.Sum() / pointsList.Count;
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