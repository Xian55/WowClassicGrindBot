using System;

namespace Core
{
    public class LevelTracker
    {
        private readonly PlayerReader playerReader;

        private long level = 0;
        private long lastXp = 0;

        private DateTime levelStartTime = DateTime.Now;
        private long levelStartXP = 0;

        public DateTime PredictedLevelTime { get; private set; } = DateTime.Now;
        public long MobsKilled { get; private set; } = 0;
        public string TimeToLevel { get; private set; } = string.Empty;

        public LevelTracker(PlayerReader playerReader)
        {
            this.playerReader = playerReader;
        }

        public void Update()
        {
            if (level != playerReader.PlayerLevel)
            {
                level = playerReader.PlayerLevel;
                lastXp = playerReader.PlayerXp;
                levelStartTime = DateTime.Now;
                levelStartXP = playerReader.PlayerXp;
            }
            else
            {
                if (lastXp != playerReader.PlayerXp)
                {
                    lastXp = playerReader.PlayerXp;
                    MobsKilled++;

                    var runningSeconds = (DateTime.Now - levelStartTime).TotalSeconds;
                    var xpPerSecond = (playerReader.PlayerXp - levelStartXP) / runningSeconds;
                    var secondsLeft = (playerReader.PlayerMaxXp - playerReader.PlayerXp) / xpPerSecond;

                    TimeToLevel = new TimeSpan(0, 0, (int)secondsLeft).ToString();

                    if (secondsLeft > 0 && secondsLeft < 60 * 60 * 10)
                    {
                        PredictedLevelTime = DateTime.Now.AddSeconds(secondsLeft);
                    }
                }
            }
        }
    }
}