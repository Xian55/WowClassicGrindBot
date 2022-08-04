using Microsoft.Extensions.Logging;
using System;

namespace Core
{
    public partial class Blacklist : IBlacklist
    {
        private readonly string[] blacklist;

        private readonly AddonReader addonReader;
        private readonly PlayerReader playerReader;
        private readonly ILogger logger;
        private readonly int above;
        private readonly int below;
        private readonly bool checkTargetGivesExp;
        private readonly bool allowTargetingAbnormal;

        private int lastGuid;

        public Blacklist(ILogger logger, AddonReader addonReader, ClassConfiguration classConfig)
        {
            this.addonReader = addonReader;
            playerReader = addonReader.PlayerReader;
            this.logger = logger;
            this.above = classConfig.NPCMaxLevels_Above;
            this.below = classConfig.NPCMaxLevels_Below;

            this.checkTargetGivesExp = classConfig.CheckTargetGivesExp;
            this.allowTargetingAbnormal = classConfig.AllowTargetingAbnormal;

            this.blacklist = classConfig.Blacklist;

            if (blacklist.Length > 0)
                logger.LogInformation($"[{nameof(Blacklist)}] {string.Join(", ", blacklist)}");
        }

        public bool IsTargetBlacklisted()
        {
            if (!playerReader.Bits.HasTarget())
            {
                lastGuid = 0;
                return false;
            }
            else if (addonReader.CombatLog.DamageTaken.Contains(playerReader.TargetGuid))
            {
                return false;
            }

            if (playerReader.PetHasTarget && playerReader.TargetGuid == playerReader.PetGuid)
            {
                return true;
            }

            // it is trying to kill me
            if (playerReader.Bits.TargetOfTargetIsPlayerOrPet())
            {
                return false;
            }

            if (!playerReader.Bits.TargetIsNormal() && !allowTargetingAbnormal)
            {
                if (lastGuid != playerReader.TargetGuid)
                {
                    LogNotNormal(logger, playerReader.TargetId, playerReader.TargetGuid, addonReader.TargetName);
                    lastGuid = playerReader.TargetGuid;
                }

                return true; // ignore elites
            }

            if (!playerReader.Bits.TargetIsDead() && playerReader.Bits.IsTagged())
            {
                if (lastGuid != playerReader.TargetGuid)
                {
                    LogTagged(logger, playerReader.TargetId, playerReader.TargetGuid, addonReader.TargetName);
                    lastGuid = playerReader.TargetGuid;
                }

                return true; // ignore tagged mobs
            }


            if (playerReader.Bits.TargetCanBeHostile() && playerReader.TargetLevel > playerReader.Level.Value + above)
            {
                if (lastGuid != playerReader.TargetGuid)
                {
                    LogLevelHigh(logger, playerReader.TargetId, playerReader.TargetGuid, addonReader.TargetName);
                    lastGuid = playerReader.TargetGuid;
                }

                return true; // ignore if current level + 2
            }

            if (checkTargetGivesExp)
            {
                if (!playerReader.TargetYieldXP())
                {
                    if (lastGuid != playerReader.TargetGuid)
                    {
                        LogNoExperienceGain(logger, playerReader.TargetId, playerReader.TargetGuid, addonReader.TargetName);
                        lastGuid = playerReader.TargetGuid;
                    }
                    return true;
                }
            }
            else if (playerReader.Bits.TargetCanBeHostile() && playerReader.TargetLevel < playerReader.Level.Value - below)
            {
                if (lastGuid != playerReader.TargetGuid)
                {
                    LogLevelLow(logger, playerReader.TargetId, playerReader.TargetGuid, addonReader.TargetName);
                    lastGuid = playerReader.TargetGuid;
                }
                return true; // ignore if current level - 7
            }

            if (blacklist.Length > 0 && Contains())
            {
                if (lastGuid != playerReader.TargetGuid)
                {
                    LogNameMatch(logger, playerReader.TargetId, playerReader.TargetGuid, addonReader.TargetName);
                    lastGuid = playerReader.TargetGuid;
                }
                return true;
            }

            return false;
        }

        private bool Contains()
        {
            for (int i = 0; i < blacklist.Length; i++)
            {
                if (addonReader.TargetName.Contains(blacklist[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        #region logging

        [LoggerMessage(
            EventId = 60,
            Level = LogLevel.Warning,
            Message = "Blacklist ({id},{guid},{name}) not a normal mob!")]
        static partial void LogNotNormal(ILogger logger, int id, int guid, string name);

        [LoggerMessage(
            EventId = 61,
            Level = LogLevel.Warning,
            Message = "Blacklist ({id},{guid},{name}) is tagged!")]
        static partial void LogTagged(ILogger logger, int id, int guid, string name);

        [LoggerMessage(
            EventId = 62,
            Level = LogLevel.Warning,
            Message = "Blacklist ({id},{guid},{name}) too high level!")]
        static partial void LogLevelHigh(ILogger logger, int id, int guid, string name);

        [LoggerMessage(
            EventId = 63,
            Level = LogLevel.Warning,
            Message = "Blacklist ({id},{guid},{name}) too low level!")]
        static partial void LogLevelLow(ILogger logger, int id, int guid, string name);

        [LoggerMessage(
            EventId = 64,
            Level = LogLevel.Warning,
            Message = "Blacklist ({id},{guid},{name}) not yield experience!")]
        static partial void LogNoExperienceGain(ILogger logger, int id, int guid, string name);

        [LoggerMessage(
            EventId = 65,
            Level = LogLevel.Warning,
            Message = "Blacklist ({id},{guid},{name}) name match!")]
        static partial void LogNameMatch(ILogger logger, int id, int guid, string name);

        #endregion
    }
}