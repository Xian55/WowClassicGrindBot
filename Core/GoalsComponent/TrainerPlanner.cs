using Core.Database;

using Microsoft.Extensions.Logging;

using SharedLib;

using System;
using System.Collections.Generic;
using System.Text;

namespace Core;

/// <summary>
/// Decides whether a trip to a class trainer is worth making, and which spell ids to
/// hand the addon once there.
/// <para>
/// Session scoped, so everything it remembers is dropped when a profile is reloaded -
/// a different profile means different spells, and a trainer written off under the old
/// one deserves another look. The eligibility rule itself is in
/// <see cref="TrainerSpells"/>, which the HasTrainableSpell requirement also uses.
/// </para>
/// </summary>
public sealed partial class TrainerPlanner
{
    private readonly ILogger<TrainerPlanner> logger;
    private readonly PlayerReader playerReader;
    private readonly SpellBookReader spellBookReader;
    private readonly SpellDB spellDB;

    private readonly int[][] whitelist;

    // Nothing in the game says "this trainer was no use to me", so it has to be
    // remembered. Without it the planner keeps reporting work, the goal keeps walking
    // back, and the bot loops between the same trainers forever.
    private readonly HashSet<int> uselessNpcs = [];

    // Ids the era's spells.json does not carry - a profile written for a later client.
    // Kept so the warning is emitted once rather than on every evaluation.
    private readonly HashSet<int> unknownIds = [];

    // Set to the wallet at the moment a trainer reported a service it could not afford.
    // Suppression lifts by itself once the balance rises above it, so no timer is needed.
    private int suppressUntilMoneyAbove = -1;

    // The zone whose trainer search came up empty. Cleared by walking into a different
    // one, which is the only thing that can change the answer: CrossZoneSearch is off by
    // default, so a zone with no trainer of this class has none to find however long the
    // bot waits - and HasTrainableSpell can stay true for a whole zone's worth of levels.
    private int noTrainerInZone = -1;

    // The player level at which a train-all visit came back having bought nothing. Only
    // levelling changes what a trainer offers, so anything short of that would just walk
    // there to be told the same thing - and train-all has no whitelist to go quiet on.
    private int nothingToTrainAtLevel = -1;

    private int lastSeenLevel = -1;

    public TrainerPlanner(ILogger<TrainerPlanner> logger, ClassConfiguration classConfig,
        PlayerReader playerReader, SpellBookReader spellBookReader, SpellDB spellDB)
    {
        this.logger = logger;
        this.playerReader = playerReader;
        this.spellBookReader = spellBookReader;
        this.spellDB = spellDB;

        whitelist = TrainerSpells.BuildWhitelist(classConfig);
    }

    /// <summary>
    /// Levelling is the only thing that changes what a trainer will teach, so every
    /// "there was nothing here" memory has to let go on a level-up - otherwise a trainer
    /// written off at level 4 is never revisited at level 6. Checked on read rather than
    /// driven off Level.Changed, so there is no subscription to unwind.
    /// </summary>
    private void ForgetOnLevelUp()
    {
        int level = playerReader.Level.Value;
        if (level <= lastSeenLevel)
            return;

        if (lastSeenLevel >= 0)
        {
            uselessNpcs.Clear();
            nothingToTrainAtLevel = -1;
            noTrainerInZone = -1;
        }

        lastSeenLevel = level;
    }

    /// <summary>
    /// Upper bound on what <see cref="TryGetTrainable"/> can write, so a caller can size
    /// a stack buffer without allocating.
    /// </summary>
    public int MaxTrainable => TrainerSpells.MaxCount(whitelist);

    /// <summary>
    /// True when a visit could actually achieve something. The goal gates on this as
    /// well as on its profile requirements, because the reasons a visit is pointless -
    /// no money, every nearby trainer already tried - are session state the requirement
    /// language cannot see.
    /// </summary>
    public bool CanVisit => CanVisitInternal(trainAll: false);

    /// <summary>
    /// Train-all has no whitelist to reason about - what a trainer sells is only knowable
    /// once the window is open - so it is gated on the suppressions alone. A profile is
    /// expected to add its own <c>Money</c> or <c>SecondsSinceTrained</c> requirement.
    /// </summary>
    public bool CanVisitAll => CanVisitInternal(trainAll: true);

    private bool CanVisitInternal(bool trainAll)
    {
        ForgetOnLevelUp();

        if (MoneySuppressed() || NoTrainerReachable())
            return false;

        if (!trainAll)
            return whitelist.Length > 0 &&
                TrainerSpells.Any(whitelist, spellBookReader, spellDB, playerReader.Level.Value);

        return playerReader.Level.Value > nothingToTrainAtLevel;
    }

    /// <summary>
    /// A train-all visit finished without buying anything. Nothing will change until the
    /// player levels, so stop asking until then.
    /// </summary>
    public void OnNothingTrainedAtThisLevel()
    {
        int level = playerReader.Level.Value;
        if (nothingToTrainAtLevel == level)
            return;

        nothingToTrainAtLevel = level;
        LogNothingAtLevel(logger, level);
    }

    public bool TryGetTrainable(Span<int> destination, out int written)
    {
        written = TrainerSpells.Collect(whitelist,
            spellBookReader, spellDB, playerReader.Level.Value, destination, unknownIds);

        // Collect only adds to unknownIds; report each one the first time it shows up.
        ReportUnknownIds();

        return written > 0;
    }

    private void ReportUnknownIds()
    {
        if (unknownIds.Count == 0 || !logger.IsEnabled(LogLevel.Warning))
            return;

        foreach (int id in unknownIds)
            LogUnknownSpell(logger, id);

        // Cleared rather than kept: the set exists to rate-limit the warning, and
        // Collect refills it on the next call if the ids are still missing.
        unknownIds.Clear();
    }

    private bool MoneySuppressed()
    {
        if (suppressUntilMoneyAbove < 0)
            return false;

        if (playerReader.Money.Value > suppressUntilMoneyAbove)
        {
            suppressUntilMoneyAbove = -1;
            return false;
        }

        return true;
    }

    private bool NoTrainerReachable()
    {
        if (noTrainerInZone < 0)
            return false;

        if (noTrainerInZone != playerReader.UIMapId.Value)
        {
            noTrainerInZone = -1;
            return false;
        }

        return true;
    }

    /// <summary>
    /// The search turned up no trainer of this class to walk to. Without this the goal
    /// stays runnable, gets picked for its low cost, finds nothing again and occupies the
    /// agent doing nothing - the blacklist cannot cover it, since that only fires once a
    /// trainer has actually been reached.
    /// </summary>
    public void OnNoTrainerFound()
    {
        int zone = playerReader.UIMapId.Value;
        if (noTrainerInZone == zone)
            return;

        noTrainerInZone = zone;
        LogNoTrainerInZone(logger, zone);
    }

    /// <summary>
    /// The trainer offered something wanted but unaffordable. Stops asking until the
    /// wallet grows past what it was worth at that moment.
    /// </summary>
    public void OnNotEnoughMoney()
    {
        suppressUntilMoneyAbove = playerReader.Money.Value;
        LogNotEnoughMoney(logger, suppressUntilMoneyAbove);
    }

    /// <summary>
    /// The trainer had nothing on the whitelist - the wrong class, or a profile listing
    /// spells it does not teach. Skipped from here on so the search moves to the next.
    /// </summary>
    public void OnNothingToTrain(int npcId)
    {
        if (npcId != 0 && uselessNpcs.Add(npcId))
            LogNothingToTrain(logger, npcId);
    }

    public bool IsUseless(int npcId) => uselessNpcs.Contains(npcId);

    /// <summary>
    /// Resolves spell ids to their names. Only ids the DB knows are added, so a caller
    /// matching these against profile KeyAction names cannot be fooled by a stray id.
    /// </summary>
    public void CollectNames(ReadOnlySpan<int> ids, ICollection<string> destination)
    {
        for (int i = 0; i < ids.Length; i++)
        {
            if (spellDB.Spells.TryGetValue(ids[i], out Spell spell) &&
                !string.IsNullOrEmpty(spell.Name) &&
                !destination.Contains(spell.Name))
            {
                destination.Add(spell.Name);
            }
        }
    }

    /// <summary>
    /// "Charge(100), Rend rank 2(6546)" - for logs, so a trainer visit says which spells
    /// it meant rather than a bare count. Allocates, but runs once per visit.
    /// </summary>
    public string Describe(ReadOnlySpan<int> ids)
    {
        if (ids.Length == 0)
            return "none";

        StringBuilder sb = new();

        for (int i = 0; i < ids.Length; i++)
        {
            if (sb.Length > 0)
                sb.Append(", ");

            if (spellDB.Spells.TryGetValue(ids[i], out Spell spell))
                sb.Append(spell.Name).Append('(').Append(ids[i]).Append(')');
            else
                sb.Append(ids[i]);
        }

        return sb.ToString();
    }

    #region Logging

    [LoggerMessage(
        EventId = 0310,
        Level = LogLevel.Warning,
        Message = "Trainer: spell {SpellId} is not in this client's spells.json - skipped")]
    static partial void LogUnknownSpell(ILogger logger, int spellId);

    [LoggerMessage(
        EventId = 0311,
        Level = LogLevel.Information,
        Message = "Trainer: cannot afford training, waiting for the purse to rise above {Copper}")]
    static partial void LogNotEnoughMoney(ILogger logger, int copper);

    [LoggerMessage(
        EventId = 0312,
        Level = LogLevel.Information,
        Message = "Trainer: npc {NpcId} teaches nothing on the whitelist - skipping it from now on")]
    static partial void LogNothingToTrain(ILogger logger, int npcId);

    [LoggerMessage(
        EventId = 0313,
        Level = LogLevel.Information,
        Message = "Trainer: no reachable trainer of this class in zone {UIMapId} - not asking again until the zone changes")]
    static partial void LogNoTrainerInZone(ILogger logger, int uiMapId);

    [LoggerMessage(
        EventId = 0314,
        Level = LogLevel.Information,
        Message = "Trainer: nothing left to learn at level {Level} - not asking again until the next level")]
    static partial void LogNothingAtLevel(ILogger logger, int level);

    #endregion
}
