using Core.Database;
using Core.GOAP;

using Game;

using Microsoft.Extensions.Logging;

using SharedLib;
using SharedLib.Data;
using SharedLib.Extensions;
using SharedLib.NpcFinder;

using System;
using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;

#pragma warning disable 162

namespace Core.Goals;

public sealed partial class AdhocNPCGoal : GoapGoal, IGoapEventListener, IRouteProvider, IDisposable
{
    private enum PathState
    {
        ApproachPathStart,
        FollowPath,
        Finished,
    }

    private enum MerchantResult
    {
        Success,
        Failed,
        TryNextNPC,
    }

    private const bool debug = false;

    private const int MAX_TIME_TO_REACH_MELEE = 10000;
    private const int TIMEOUT = 5000;

    // The addon buys one service per timer tick and rescans between them, so a full
    // whitelist takes noticeably longer than a merchant interaction.
    private const int TRAIN_TIMEOUT = 30000;
    private const int MAX_SELL_NOTHING_RETRIES = 2;

    // Ordered longest flag name first, so a name is matched against the most specific
    // flag that fits it. Enum order would test Trainer (1<<4) before ClassTrainer
    // (1<<5) and "ClassTrainer" contains "Trainer", which sent a class trainer entry
    // searching the generic superset - and every Vendor* subtype to plain Vendor.
    private readonly (NpcFlags Flag, SearchValues<string> Pattern)[] npcSearchPatterns;

    public override float Cost => key.Cost;

    private readonly ILogger<AdhocNPCGoal> logger;
    private readonly ConfigurableInput input;
    private readonly KeyAction key;
    private readonly Wait wait;
    private readonly Navigation navigation;
    private readonly PlayerReader playerReader;
    private readonly AddonBits bits;
    private readonly StopMoving stopMoving;
    private readonly ClassConfiguration classConfig;
    private readonly NpcNameTargeting npcNameTargeting;
    private readonly IMountHandler mountHandler;
    private readonly CancellationToken token;
    private readonly ExecGameCommand execGameCommand;
    private readonly GossipReader gossipReader;
    private readonly AreaDB areaDB;
    private readonly BagReader bagReader;
    private readonly SessionStat sessionStat;
    private readonly TrainerReader trainerReader;
    private readonly TrainerPlanner trainerPlanner;
    private readonly AddonConfigurator addonConfigurator;
    private readonly ActionBarPopulator actionBarPopulator;

    private PathState pathState = PathState.Finished;

    private readonly NpcFlags npcFlag;
    private readonly string[] allowedNames;
    private readonly bool tryFindClosestNPC;
    private Creature npc;
    private NpcSearchResult[] searchResult = [];
    private int searchCount;
    private int searchIndex;
    private int sellNothingCount;

    #region IRouteProvider

    public Vector3[] MapRoute()
    {
        return Array.Empty<Vector3>();
    }

    public Vector3[] PathingRoute()
    {
        return navigation.TotalRoute;
    }

    public bool HasNext()
    {
        return navigation.HasNext();
    }

    public Vector3 NextMapPoint()
    {
        return navigation.NextMapPoint();
    }

    public DateTime LastActive => navigation.LastActive;

    #endregion

    public AdhocNPCGoal(KeyAction key, ILogger<AdhocNPCGoal> logger, ConfigurableInput input,
        Wait wait, PlayerReader playerReader, GossipReader gossipReader, AddonBits bits,
        Navigation navigation, StopMoving stopMoving, AreaDB areaDB,
        NpcNameTargeting npcNameTargeting, ClassConfiguration classConfig,
        BagReader bagReader, SessionStat sessionStat,
        TrainerReader trainerReader, TrainerPlanner trainerPlanner,
        AddonConfigurator addonConfigurator, ActionBarPopulator actionBarPopulator,
        IMountHandler mountHandler, ExecGameCommand exec, CancellationTokenSource cts)
        : base(nameof(AdhocNPCGoal))
    {
        this.logger = logger;
        this.input = input;
        this.key = key;
        this.wait = wait;
        this.playerReader = playerReader;
        this.bits = bits;
        this.stopMoving = stopMoving;
        this.areaDB = areaDB;
        this.npcNameTargeting = npcNameTargeting;
        this.classConfig = classConfig;
        this.bagReader = bagReader;
        this.sessionStat = sessionStat;
        this.trainerReader = trainerReader;
        this.trainerPlanner = trainerPlanner;
        this.addonConfigurator = addonConfigurator;
        this.actionBarPopulator = actionBarPopulator;
        this.mountHandler = mountHandler;
        token = cts.Token;
        this.execGameCommand = exec;
        this.gossipReader = gossipReader;

        this.navigation = navigation;
        navigation.OnDestinationReached += Navigation_OnDestinationReached;
        navigation.OnWayPointReached += Navigation_OnWayPointReached;
        navigation.OnNoPathFound += Navigation_OnNoPathFound;

        if (bool.TryParse(key.InCombat, out bool result))
        {
            if (!result)
                AddPrecondition(GoapKey.dangercombat, result);
            else
                AddPrecondition(GoapKey.incombat, result);
        }

        Keys = [key];

        npcSearchPatterns = Enum.GetValues<NpcFlags>()
            .OrderByDescending(static flag => flag.ToString().Length)
            .Select(static flag =>
            {
                string[] strings = flag switch
                {
                    NpcFlags.Vendor => [flag.ToString(), "Sell"],
                    _ => [flag.ToString()]
                };

                return (flag, SearchValues.Create(strings, StringComparison.OrdinalIgnoreCase));
            })
            .ToArray();

        npcFlag = ResolveNpcFlag(key.Name);
        allowedNames = ResolveAllowedNames(key.Name);

        if (allowedNames.Length > 0 && logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Search for {NpcFlag} like {AllowedNames}", npcFlag, string.Join(',', allowedNames));

        tryFindClosestNPC = key.Path.Length == 0;
    }

    /// <summary>
    /// Resolved once from the KeyAction name rather than per search, so it is known even
    /// for an entry that ships a PathFilename and never runs the closest-NPC search.
    /// </summary>
    private NpcFlags ResolveNpcFlag(ReadOnlySpan<char> name)
    {
        for (int i = 0; i < npcSearchPatterns.Length; i++)
        {
            if (name.ContainsAny(npcSearchPatterns[i].Pattern))
                return npcSearchPatterns[i].Flag;
        }

        return NpcFlags.None;
    }

    // TODO: faction specific filter?
    // try to detect pattern
    // [TYPE][ ][npc1 | npc2 | npc3]
    private static string[] ResolveAllowedNames(ReadOnlySpan<char> name)
    {
        int separator = name.IndexOf(' ');
        if (separator == -1)
            return [];

        return name[(separator + 1)..]
            .ToString()
            .Split('|', options: StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    public void Dispose()
    {
        navigation.Dispose();
    }

    /// <summary>
    /// A trainer entry additionally answers to the planner. The profile can say
    /// HasTrainableSpell, but not "every trainer near me already turned out to teach
    /// none of them" or "the price was more than I have" - that is session state, and
    /// without it the goal walks back to the same trainers forever.
    /// </summary>
    public override bool CanRun() =>
        key.CanRun() &&
        (npcFlag != NpcFlags.ClassTrainer ||
         (key.TrainAll ? trainerPlanner.CanVisitAll : trainerPlanner.CanVisit));

    public void OnGoapEvent(GoapEventArgs e)
    {
        if (e.GetType() == typeof(ResumeEvent))
        {
            Resume();

        }
        else if (e.GetType() == typeof(AbortEvent))
        {
            Abort();
        }
    }

    private void Resume()
    {
        if (tryFindClosestNPC && !TryAutoSelectNPCAndSetPath())
        {
            pathState = PathState.Finished;
            LogWarn("No NPC with the criteria!");

            // Guarded on the area being loaded: a null CurrentArea is a transient
            // startup state, not evidence that the zone has no trainer.
            if (npcFlag == NpcFlags.ClassTrainer && areaDB.CurrentArea != null)
                trainerPlanner.OnNoTrainerFound();

            return;
        }

        input.PressClearTarget();
        stopMoving.Stop();

        SetClosestWaypoint();

        navigation.Resume();

        pathState = PathState.ApproachPathStart;

        MountIfPossible();
    }

    private void Abort()
    {
        navigation.StopMovement();
        navigation.Stop();
        npcNameTargeting.ChangeNpcType(NpcNames.None);

        if (tryFindClosestNPC)
        {
            key.Path = [];
            npc = default;
            searchResult = [];
            searchCount = 0;
            searchIndex = 0;
        }
    }


    public override void OnEnter() => Resume();

    public override void OnExit() => Abort();

    public override void Update()
    {
        if (bits.Drowning())
            input.PressJump();

        if (pathState != PathState.Finished)
            navigation.Update();

        wait.Update();
    }


    private void SetClosestWaypoint()
    {
        Span<Vector3> path = stackalloc Vector3[key.Path.Length];
        key.Path.CopyTo(path);

        bool isWorldCoords = IsWorldCoords(path);

        Vector3 playerPos;
        int closestIndex = 0;
        Vector3 closestPoint = Vector3.Zero;
        float distance = float.MaxValue;

        if (isWorldCoords)
        {
            playerPos = playerReader.WorldPos;

            for (int i = 0; i < path.Length; i++)
            {
                float d = playerPos.WorldDistanceXYTo(path[i]);
                if (d < distance)
                {
                    distance = d;
                    closestIndex = i;
                    closestPoint = path[i];
                }
            }
        }
        else
        {
            playerPos = playerReader.MapPos;

            for (int i = 0; i < path.Length; i++)
            {
                float d = playerPos.MapDistanceXYTo(path[i]);
                if (d < distance)
                {
                    distance = d;
                    closestIndex = i;
                    closestPoint = path[i];
                }
            }
        }

        if (closestPoint == path[0] || closestPoint == path[^1])
        {
            navigation.SetWayPoints(path);
        }
        else
        {
            Span<Vector3> points = path[closestIndex..];
            navigation.SetWayPoints(points);
        }
    }

    private static bool IsWorldCoords(ReadOnlySpan<Vector3> path)
    {
        for (int i = 0; i < path.Length; i++)
        {
            Vector3 p = path[i];
            if (p.X is < 0 or > 100 || p.Y is < 0 or > 100)
                return true;
        }
        return false;
    }

    private void UpdateClosestNPC(NpcFlags npcFlag)
    {
        if (searchResult.Length == 0 || searchCount == 0)
            return;

        npc = searchResult[searchIndex].Creature;
        Vector3 worldPos = searchResult[searchIndex].WorldPosition;
        key.Path = [worldPos];

        LogFoundCloesestNPCByType(logger, npc.Name, npcFlag, worldPos);
    }

    private void Navigation_OnNoPathFound()
    {
        if (pathState != PathState.ApproachPathStart || token.IsCancellationRequested)
            return;

        logger.LogError("No path found!");

        Resume();
    }

    private void Navigation_OnWayPointReached()
    {
        if (pathState is PathState.ApproachPathStart)
        {
            LogDebug("1 Reached the start point of the path.");
            navigation.SimplifyRouteToWaypoint = false;
        }
    }

    private void Navigation_OnDestinationReached()
    {
        if (pathState != PathState.ApproachPathStart || token.IsCancellationRequested)
            return;

        LogDebug("Reached defined path end");
        navigation.StopMovement();
        stopMoving.Stop();
        wait.Update();

        input.PressClearTarget();
        wait.Update();

        if (tryFindClosestNPC && npc != default)
        {
            execGameCommand.Run($"/target {npc.Name}");
            wait.Update();
        }

        bool hasTarget = bits.Target();

        if (bits.SoftInteract() &&
            !bits.SoftInteract_Hostile())
        {
            input.PressInteract();
            wait.Update();

            LogWarn($"Soft Interact found NPC with id {playerReader.SoftInteract_Id}");

            hasTarget = MoveToTargetAndReached();
        }

        if (!hasTarget && !input.KeyboardOnly)
        {
            npcNameTargeting.ChangeNpcType(NpcNames.Friendly | NpcNames.Neutral);
            npcNameTargeting.WaitForUpdate();

            ReadOnlySpan<CursorType> types = [
                CursorType.Loot,
                CursorType.Vendor,
                CursorType.Repair,
                CursorType.Innkeeper,
                CursorType.Speak
            ];

            hasTarget = npcNameTargeting.FindBy(types, token);
            wait.Update();

            if (!hasTarget)
            {
                LogWarn($"No target found by cursor({CursorType.Vendor.ToString()}, {CursorType.Repair.ToString()}, {CursorType.Innkeeper.ToString()})!");
            }
        }

        if (!hasTarget)
        {
            Log($"Use KeyAction.Key macro to acquire target");
            input.PressRandom(key);
            wait.Update();
        }

        wait.Until(400, bits.Target);
        if (!bits.Target())
        {
            LogWarn("No target found! Turn left to find NPC");
            input.PressFixed(input.TurnLeftKey, 250, token);
            return;
        }

        Log($"Found Target!");

        // Interacting again on an open window closes it. The soft-interact branch above
        // can already have opened the merchant - and the grey items can already be sold
        // by the time it returns - at which point this press would shut it and the wait
        // below would time out on a visit that had in fact succeeded.
        if (!bits.MerchantFrameShown() && !bits.TrainerFrameShown())
        {
            input.PressInteract();
            wait.Update();
        }

        bool training = npcFlag == NpcFlags.ClassTrainer;

        MerchantResult merchantResult = training
            ? OpenTrainerWindow()
            : OpenMerchantWindow();

        if (merchantResult == MerchantResult.TryNextNPC && tryFindClosestNPC)
        {
            input.PressClearTarget();
            Resume();
            return;
        }

        if (merchantResult != MerchantResult.Success)
        {
            // Only the success path used to clean up, so a failed interaction walked away
            // still targeting the NPC. Observed: a vendor whose window had in fact opened
            // and sold, was toggled shut by the second interact, timed out, and stayed
            // targeted for minutes while FollowRouteGoal tab-hunted around it.
            input.PressRandom(ConsoleKey.Escape, InputDuration.DefaultPress);
            input.PressClearTarget();
            wait.Update();
            return;
        }

        // Signal that vendor/repair completed successfully
        // MailGoal uses this to know it can run. Training moves no items and costs
        // rather than earns, so it is not what MailGoal is waiting for.
        if (!training)
            sessionStat.OnVendored();

        input.PressRandom(ConsoleKey.Escape, InputDuration.DefaultPress);
        input.PressClearTarget();
        wait.Update();

        return;
        // The following code no longer needed as we know for a fact we are close to an NPC spawnpoint
        // thus we know the world coordinate and Z/height component
        // then the pathfinder can reliable locate the player exact location

        Span<Vector3> reversePath = stackalloc Vector3[key.Path.Length];
        key.Path.CopyTo(reversePath);
        reversePath.Reverse();
        navigation.SetWayPoints(reversePath);

        pathState++;

        LogDebug("Go back reverse to the start point of the path.");
        navigation.ResetStuckParameters();

        // At this point the BagsFull is false
        // which mean it it would exit the Goal
        // instead keep it trapped to follow the route back
        while (navigation.HasWaypoint() &&
            !token.IsCancellationRequested &&
            pathState == PathState.FollowPath)
        {
            navigation.Update();
            wait.Update();
        }

        pathState = PathState.Finished;

        LogDebug("2 Reached the start point of the path.");
        stopMoving.Stop();

        navigation.SimplifyRouteToWaypoint = true;
        MountIfPossible();
    }

    private bool MoveToTargetAndReached()
    {
        wait.While(input.Approach.OnCooldown);

        float elapsedMs = wait.Until(MAX_TIME_TO_REACH_MELEE,
            bits.NotMoving, input.PressApproachOnCooldown);

        //LogReachedCorpse(logger, bits.Target(), elapsedMs);

        return bits.Target() && playerReader.MinRangeZero();
    }

    private void MountIfPossible()
    {
        float totalDistance = VectorExt.TotalDistance<Vector3>(navigation.TotalRoute, VectorExt.WorldDistanceXY);

        if ((classConfig.UseMount || key.UseMount) && mountHandler.CanMount() &&
            (MountHandler.ShouldMount(totalDistance) ||
            (navigation.TotalRoute.Length > 0 &&
            mountHandler.ShouldMount(navigation.TotalRoute[^1]))
            ))
        {
            Log("Mount up");
            mountHandler.MountUp();
            navigation.ResetStuckParameters();
        }
    }

    private MerchantResult OpenMerchantWindow()
    {
        // Watches GossipEnd rather than GossipStart, for the reason spelled out in
        // OpenTrainerWindow: the gossip cell latches its last value, so by the time this
        // polls, the queue has already run past 69 to GOSSIP_END. Hunting the start value
        // missed it every time and burned the whole timeout, then the second wait found
        // the end value already sitting there - two waits, one of them always wasted.
        float e = wait.Until(TIMEOUT,
            () => gossipReader.GossipEnd() || gossipReader.MerchantWindowOpened());

        if (gossipReader.MerchantWindowOpened())
        {
            LogWarn($"Gossip no options! {e}ms");
        }
        else
        {
            if (e < 0)
            {
                LogWarn($"Gossip - {nameof(gossipReader.GossipEnd)} not fired after {e}ms");
                return MerchantResult.Failed;
            }
            else
            {
                if (gossipReader.Gossips.TryGetValue(Gossip.Vendor, out int orderNum))
                {
                    Log($"Picked {orderNum}th for {Gossip.Vendor.ToString()}");
                    execGameCommand.Run($"/run SelectGossipOption({orderNum})--");
                }
                else
                {
                    LogWarn($"Target({playerReader.TargetId}) has no {Gossip.Vendor.ToString()} option!");
                    return MerchantResult.TryNextNPC;
                }
            }
        }

        Log($"Merchant window opened after {e}ms");

        if (key.ConsoleKey != default)
            input.PressRandom(key);

        if (bagReader.AnyGreyItem())
        {
            e = wait.Until(TIMEOUT, gossipReader.MerchantWindowSelling);
            if (e < 0)
            {
                sellNothingCount++;
                if (sellNothingCount >= MAX_SELL_NOTHING_RETRIES)
                {
                    LogWarn($"Merchant sell nothing {sellNothingCount} times! Skip to next NPC.");
                    sellNothingCount = 0;
                    return MerchantResult.TryNextNPC;
                }

                Log($"Merchant sell nothing! {e}ms");
                goto exit;
            }

            sellNothingCount = 0;
            Log($"Merchant sell grey items started after {e}ms");

            e = wait.Until(TIMEOUT, gossipReader.MerchantWindowSellingFinished);
            if (e >= 0)
            {
                Log($"Merchant sell grey items finished, took {e}ms");
            }
            else
            {
                Log($"Merchant sell grey items timeout! Too many items to sell?! Increase {nameof(TIMEOUT)} - {e}ms");
            }
        }

    exit:
        if (!string.IsNullOrEmpty(key.MacroText))
        {
            string text = key.Macro();
            execGameCommand.Run(text);
            wait.Update();
        }

        return MerchantResult.Success;
    }

    /// <summary>
    /// Opens the class trainer and hands the addon the spells worth buying. The addon
    /// owns the purchase: it is the only side that can see what this trainer actually
    /// offers and what each service costs.
    /// </summary>
    private MerchantResult OpenTrainerWindow()
    {
        int npcId = playerReader.TargetId;

        // Waits on GossipEnd rather than GossipStart: the gossip cell latches its last
        // value, and by the time this runs the queue has usually already drained through
        // 69 (start) and the option hashes to GOSSIP_END. Watching for the start value
        // therefore missed it every time and burned the full timeout before the end
        // value - which was already sitting there - was checked.
        float e = wait.Until(TIMEOUT,
            () => gossipReader.GossipEnd() || bits.TrainerFrameShown());

        // A trainer that greets with a menu has to be told which service to open. One
        // that opens the trainer frame outright has already answered.
        if (!bits.TrainerFrameShown())
        {
            if (e < 0)
            {
                LogWarn($"Gossip - {nameof(gossipReader.GossipEnd)} not fired after {e}ms");
                return MerchantResult.Failed;
            }

            if (!gossipReader.Gossips.TryGetValue(Gossip.Trainer, out int orderNum))
            {
                LogWarn($"Target({playerReader.TargetId}) has no {Gossip.Trainer.ToString()} option!");
                trainerPlanner.OnNothingToTrain(npcId);
                return MerchantResult.TryNextNPC;
            }

            Log($"Picked {orderNum}th for {Gossip.Trainer.ToString()}");
            execGameCommand.Run($"/run SelectGossipOption({orderNum})--");

            e = wait.Until(TIMEOUT, bits.TrainerFrameShown);
            if (e < 0)
            {
                LogWarn($"Trainer window did not open after {e}ms");
                return MerchantResult.Failed;
            }
        }

        Log($"Trainer window opened after {e}ms");

        Span<int> buffer = stackalloc int[trainerPlanner.MaxTrainable];
        bool trainAll = key.TrainAll;
        int count = 0;

        if (!trainAll && !trainerPlanner.TryGetTrainable(buffer, out count))
        {
            // Nothing eligible any more - levelled past it, or another visit got it.
            // Not the trainer's fault, so it is not blacklisted.
            Log("Nothing left to train.");
            return MerchantResult.Success;
        }

        string addonName = addonConfigurator.Config.Title;

        ReadOnlySpan<int> wanted = buffer[..count];
        int walletBefore = playerReader.Money.Value;

        // Locals, not inline arguments: CA1873 does not track the guard for
        // source-generated log methods, only local variable access.
        if (logger.IsEnabled(LogLevel.Information))
        {
            string wantedNames = trainAll ? "everything offered" : trainerPlanner.Describe(wanted);
            string wallet = Coin.Format(walletBefore);
            LogTrainerWanted(logger, npcId, count, wantedNames, wallet);
        }

        trainerReader.Reset();
        execGameCommand.Run($"/run {addonName}:TC()--");
        wait.Update();

        if (!trainAll)
            SendTrainSpellIdsBatched(addonName, wanted);

        // TGo(1) tells the addon to ignore the whitelist and buy whatever it can afford.
        execGameCommand.Run($"/run {addonName}:TGo({(trainAll ? "1" : "")})--");

        e = wait.Until(TRAIN_TIMEOUT, () => trainerReader.Completed);
        if (e < 0)
        {
            LogWarn($"Training did not report back after {e}ms");
            return MerchantResult.Failed;
        }

        if (trainerReader.NoMoney)
        {
            trainerPlanner.OnNotEnoughMoney();
        }
        else if (trainerReader.NoMatch)
        {
            LogWarn($"Target({npcId}) teaches none of the wanted spells!");

            // Train-all asked for everything, so an empty answer is about the player's
            // level, not this trainer - blacklisting it and walking to the next one
            // would just repeat the same trip.
            if (trainAll)
            {
                trainerPlanner.OnNothingTrainedAtThisLevel();
                return MerchantResult.Success;
            }

            trainerPlanner.OnNothingToTrain(npcId);
            return MerchantResult.TryNextNPC;
        }

        int bought = trainerReader.Bought.Count;

        // The addon reports what it bought one id per addon tick. Seeing none of them
        // while the trainer clearly sold something means the batch was not decoded, not
        // that nothing happened - worth saying out loud rather than logging "trained 0".
        if (bought == 0 && !trainerReader.NoMoney)
        {
            if (trainAll)
            {
                trainerPlanner.OnNothingTrainedAtThisLevel();
            }
            else
            {
                LogWarn($"Trainer reported no purchases for the {count} spell(s) offered - " +
                    $"the report may have been missed. Check the spellbook.");
            }
        }
        else
        {
            Span<int> boughtIds = stackalloc int[bought];
            for (int i = 0; i < bought; i++)
                boughtIds[i] = trainerReader.Bought[i];

            sessionStat.OnTrained(bought);

            if (logger.IsEnabled(LogLevel.Information))
            {
                int walletAfter = playerReader.Money.Value;

                string boughtNames = trainerPlanner.Describe(boughtIds);
                // Derived from the two snapshots rather than reported by the trainer, so
                // clamped - anything else that moved the wallet mid-visit lands here too.
                string spent = Coin.Format(Math.Max(0, walletBefore - walletAfter));
                string wallet = Coin.Format(walletAfter);

                LogTrained(logger, bought, boughtNames, spent, wallet, e);
            }

            PlaceTrainedOnActionBar(boughtIds);
        }

        return MerchantResult.Success;
    }

    /// <summary>
    /// Puts the freshly learnt spells back on their action bar slots. Learning a rank
    /// leaves the button pointing at the rank that was dragged onto it, so without this
    /// the bot keeps casting the old one.
    /// <para>
    /// Only the slots holding a spell that was just bought are touched - re-placing the
    /// whole bar would be a chat command per slot and would disturb entries that are
    /// resolved from live state, such as Food, Drink and the trinkets.
    /// </para>
    /// </summary>
    private void PlaceTrainedOnActionBar(ReadOnlySpan<int> boughtIds)
    {
        List<string> names = [];
        trainerPlanner.CollectNames(boughtIds, names);

        if (names.Count == 0)
            return;

        int placed = actionBarPopulator.PlaceByNames(names);
        wait.Update();

        if (logger.IsEnabled(LogLevel.Information))
        {
            string joined = string.Join(", ", names);
            LogActionBarPlaced(logger, placed, joined);
        }
    }

    /// <summary>
    /// The chat box truncates past 255 characters, so the id list is paginated the same
    /// way MailGoal paginates its excluded items.
    /// </summary>
    private void SendTrainSpellIdsBatched(string addonName, ReadOnlySpan<int> ids)
    {
        const int MAX_CMD_LENGTH = 250;  // Leave margin for safety (WoW limit is 255)
        string prefix = $"/run {addonName}:TAdd(\"";
        const string suffix = "\")--";
        int overhead = prefix.Length + suffix.Length;

        StringBuilder batch = new();
        for (int i = 0; i < ids.Length; i++)
        {
            string idStr = ids[i].ToString();
            if (batch.Length > 0 && batch.Length + 1 + idStr.Length + overhead > MAX_CMD_LENGTH)
            {
                execGameCommand.Run($"{prefix}{batch}{suffix}");
                wait.Update();
                batch.Clear();
            }

            if (batch.Length > 0) batch.Append(',');
            batch.Append(idStr);
        }

        if (batch.Length > 0)
        {
            execGameCommand.Run($"{prefix}{batch}{suffix}");
            wait.Update();
        }
    }

    private bool TryAutoSelectNPCAndSetPath()
    {
        if (areaDB.CurrentArea == null)
        {
            return false;
        }

        // Read live rather than cached in the constructor: the addon may not have
        // reported the class yet when the goal is built. Empty for UnitClass.None,
        // which filters nothing rather than filtering everything out.
        string? subNameContains = npcFlag == NpcFlags.ClassTrainer
            ? playerReader.Class.TrainerSubName()
            : null;

        if (string.IsNullOrEmpty(subNameContains))
            subNameContains = null;

        if (searchResult.Length == 0)
        {
            searchResult = new NpcSearchResult[8];

            int found = areaDB.GetNearestNpcs(playerReader.Faction, npcFlag, playerReader.WorldPos, allowedNames, searchResult.AsSpan(), out searchCount, classConfig.CrossZoneSearch, subNameContains);
            if (found == 0 || searchCount == 0)
            {
                return false;
            }

            LogFoundPotentialNPCByType(logger, searchCount, npcFlag);
            searchIndex = 0;
        }
        else
        {
            searchIndex++;
        }

        // A trainer that turned out to teach nothing on the whitelist is not worth
        // walking to a second time. Empty for every other NPC type.
        while (searchIndex < searchCount &&
            trainerPlanner.IsUseless(searchResult[searchIndex].Creature.Entry))
        {
            searchIndex++;
        }

        if (searchIndex >= searchCount)
        {
            pathState = PathState.Finished;
            LogWarn("No more NPC to try!");

            searchIndex = 0;
            searchResult = [];
            searchCount = 0;

            return false;
        }

        LogWarn($"Try next closest NPC -- {searchIndex}");

        UpdateClosestNPC(npcFlag);

        return true;
    }


    private void Log(string text)
    {
        logger.LogInformation(text);
    }

    private void LogDebug(string text)
    {
        if (debug)
            logger.LogDebug(text);
    }

    private void LogWarn(string text)
    {
        logger.LogWarning(text);
    }


    #region Logging

    [LoggerMessage(
        EventId = 0300,
        Level = LogLevel.Information,
        Message = "Closest NPC found {type} {name} at {pos}")]
    static partial void LogFoundCloesestNPCByType(ILogger logger, string name, NpcFlags type, Vector3 pos);

    [LoggerMessage(
        EventId = 0301,
        Level = LogLevel.Information,
        Message = "Found {count} potential {type} NPC.")]
    static partial void LogFoundPotentialNPCByType(ILogger logger, int count, NpcFlags type);

    [LoggerMessage(
        EventId = 0302,
        Level = LogLevel.Information,
        Message = "Trainer({npcId}): offering {count} spell(s) to learn - {spells}. Wallet {wallet}")]
    static partial void LogTrainerWanted(ILogger logger, int npcId, int count, string spells, string wallet);

    [LoggerMessage(
        EventId = 0303,
        Level = LogLevel.Information,
        Message = "Trained {count} spell(s): {spells}. Spent {spent}, wallet now {wallet}, took {elapsedMs}ms")]
    static partial void LogTrained(ILogger logger, int count, string spells, string spent, string wallet, float elapsedMs);

    [LoggerMessage(
        EventId = 0304,
        Level = LogLevel.Information,
        Message = "Action bar: re-placed {placed} slot(s) for the newly trained {spells}")]
    static partial void LogActionBarPlaced(ILogger logger, int placed, string spells);


    #endregion
}