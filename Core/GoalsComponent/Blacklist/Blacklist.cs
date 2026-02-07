using Microsoft.Extensions.Logging;

using SharedLib.Extensions;

using System;
using System.Buffers;

namespace Core;

public sealed partial class Blacklist<T> : IBlacklist where T : IBlacklistSource
{
    private readonly ILogger<Blacklist<T>> logger;

    private readonly SearchValues<string> blacklist;

    private readonly IBlacklistSource source;
    private readonly PlayerReader playerReader;
    private readonly CombatLog combatLog;

    private readonly int above;
    private readonly int below;
    private readonly bool checkGivesExp;
    private readonly UnitClassification targetMask;

    private readonly bool allowPvP;

    private int lastGuid;

    public Blacklist(
        ILogger<Blacklist<T>> logger,
        T source,
        PlayerReader playerReader,
        CombatLog combatLog,
        ClassConfiguration classConfig)
    {
        this.source = source;
        this.playerReader = playerReader;
        this.combatLog = combatLog;

        this.logger = logger;
        this.above = classConfig.NPCMaxLevels_Above;
        this.below = classConfig.NPCMaxLevels_Below;

        this.checkGivesExp = classConfig.CheckTargetGivesExp;
        this.targetMask = classConfig.TargetMask;

        if (classConfig.Blacklist.Length > 0 && logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Name: {Blacklist}", string.Join(", ", classConfig.Blacklist));
        }
        blacklist = SearchValues.Create(classConfig.Blacklist, StringComparison.OrdinalIgnoreCase);

        this.allowPvP = classConfig.AllowPvP;

        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("{TargetMask}: {Flags}", nameof(classConfig.TargetMask), string.Join(", ", targetMask.GetIndividualFlags()));
    }


    public bool Is()
    {
        if (!source.Exists())
        {
            lastGuid = 0;
            return false;
        }
        else if (combatLog.DamageTaken.Contains(source.UnitGuid))
        {
            return false;
        }

        if (playerReader.PetTarget() && source.UnitGuid == playerReader.PetGuid)
        {
            if (lastGuid != source.UnitGuid)
            {
                LogPetTarget(logger, typeof(T),
                    source.UnitId, source.UnitGuid, source.UnitName);

                lastGuid = source.UnitGuid;
            }

            return true;
        }

        if (combatLog.EvadeMobs.Contains(source.UnitGuid))
        {
            if (lastGuid != source.UnitGuid)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                    LogEvade(logger, typeof(T),
                        source.UnitId,
                        source.UnitGuid, source.UnitName,
                        playerReader.TargetClassification.ToStringF());

                lastGuid = source.UnitGuid;
            }
            return true;
        }

        // Check for players/pets BEFORE checking if they're targeting us
        // This ensures we blacklist enemy players even if they're targeting us
        // (unless they've already damaged us - handled by DamageTaken check above)
        if (source.UnitGuid != playerReader.PetGuid &&
            !allowPvP && (source.Unit_Player() || source.Unit_PlayerControlled()))
        {
            if (lastGuid != source.UnitGuid)
            {
                LogPlayerOrPet(logger, typeof(T),
                    source.UnitId,
                    source.UnitGuid, source.UnitName);

                lastGuid = source.UnitGuid;
            }

            return true; // ignore players and pets
        }

        // it is trying to kill me (only applies to NPCs now since players are handled above)
        if (source.UnitTarget_PlayerOrPet())
        {
            return false;
        }

        if (!targetMask.HasFlagF(source.UnitClassification))
        {
            if (lastGuid != source.UnitGuid)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                    LogClassification(logger, typeof(T), source.UnitId,
                        source.UnitGuid, source.UnitName,
                        source.UnitClassification.ToStringF());

                lastGuid = source.UnitGuid;
            }

            return true; // ignore non white listed unit classification
        }

        if (!source.Unit_Dead() && source.Unit_Tagged())
        {
            if (lastGuid != source.UnitGuid)
            {
                LogTagged(logger, typeof(T), source.UnitId,
                    source.UnitGuid, source.UnitName);

                lastGuid = source.UnitGuid;
            }

            return true; // ignore tagged mobs
        }


        if (source.Unit_Hostile() && source.UnitLevel > playerReader.Level.Value + above)
        {
            if (lastGuid != source.UnitGuid)
            {
                LogLevelHigh(logger, typeof(T), source.UnitId,
                    source.UnitGuid, source.UnitName);

                lastGuid = source.UnitGuid;
            }

            return true; // ignore if current level + 2
        }

        if (checkGivesExp)
        {
            if (source.Unit_Trivial())
            {
                if (lastGuid != source.UnitGuid)
                {
                    LogNoExperienceGain(logger, typeof(T), source.UnitId,
                        source.UnitGuid, source.UnitName);

                    lastGuid = source.UnitGuid;
                }
                return true;
            }
        }
        else if (source.Unit_Hostile() && source.UnitLevel < playerReader.Level.Value - below)
        {
            if (lastGuid != source.UnitGuid)
            {
                LogLevelLow(logger, typeof(T), source.UnitId,
                    source.UnitGuid, source.UnitName);

                lastGuid = source.UnitGuid;
            }
            return true; // ignore if current level - 7
        }

        ReadOnlySpan<char> name = source.UnitName.AsSpan();
        if (name.IndexOfAny(blacklist) >= 0)
        {
            if (lastGuid != source.UnitGuid)
            {
                LogNameMatch(logger, typeof(T), source.UnitId,
                    source.UnitGuid, source.UnitName);

                lastGuid = source.UnitGuid;
            }
            return true;
        }

        return false;
    }

    #region logging

    [LoggerMessage(
        EventId = 0060,
        Level = LogLevel.Warning,
        Message = "{type} ({id},{guid},{name}) is player or pet!")]
    static partial void LogPlayerOrPet(ILogger logger, Type type, int id, int guid, string name);

    [LoggerMessage(
        EventId = 0061,
        Level = LogLevel.Warning,
        Message = "{type} ({id},{guid},{name}) is tagged!")]
    static partial void LogTagged(ILogger logger, Type type, int id, int guid, string name);

    [LoggerMessage(
        EventId = 0062,
        Level = LogLevel.Warning,
        Message = "{type} ({id},{guid},{name}) too high level!")]
    static partial void LogLevelHigh(ILogger logger, Type type, int id, int guid, string name);

    [LoggerMessage(
        EventId = 0063,
        Level = LogLevel.Warning,
        Message = "{type} ({id},{guid},{name}) too low level!")]
    static partial void LogLevelLow(ILogger logger, Type type, int id, int guid, string name);

    [LoggerMessage(
        EventId = 0064,
        Level = LogLevel.Warning,
        Message = "{type} ({id},{guid},{name}) not yield experience!")]
    static partial void LogNoExperienceGain(ILogger logger, Type type, int id, int guid, string name);

    [LoggerMessage(
        EventId = 0065,
        Level = LogLevel.Warning,
        Message = "{type} ({id},{guid},{name}) name match!")]
    static partial void LogNameMatch(ILogger logger, Type type, int id, int guid, string name);

    [LoggerMessage(
        EventId = 0066,
        Level = LogLevel.Warning,
        Message = "{type} ({id},{guid},{name},{classification}) not defined in the TargetMask!")]
    static partial void LogClassification(ILogger logger, Type type, int id, int guid, string name, string classification);

    [LoggerMessage(
        EventId = 0067,
        Level = LogLevel.Warning,
        Message = "{type} ({id},{guid},{name},{classification}) evade on attack!")]
    static partial void LogEvade(ILogger logger, Type type, int id, int guid, string name, string classification);

    [LoggerMessage(
        EventId = 0068,
        Level = LogLevel.Warning,
        Message = "{type} ({id},{guid},{name}) Pet Target!")]
    static partial void LogPetTarget(ILogger logger, Type type, int id, int guid, string name);

    #endregion
}