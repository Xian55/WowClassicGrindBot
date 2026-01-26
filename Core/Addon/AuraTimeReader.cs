using System;
using System.Collections;
using System.Collections.Generic;

using static System.Diagnostics.Stopwatch;

namespace Core;

public interface IAuraTimeReader
{
    void Update(IAddonDataProvider reader);
    void Reset();
    int GetRemainingTimeMs(int textureId);
}

public interface IPlayerBuffTimeReader : IAuraTimeReader { }

public interface IPlayerDebuffTimeReader : IAuraTimeReader { }

public interface ITargetDebuffTimeReader : IAuraTimeReader { }

public interface ITargetBuffTimeReader : IAuraTimeReader { }

public interface IFocusBuffTimeReader : IAuraTimeReader { }

public sealed class AuraTimeReader<T> : IAuraTimeReader, IReader
{
    public readonly struct Data
    {
        private long StartTime { get; }

        public int DurationSec { get; }

        public long End => StartTime + (DurationSec * TimeSpan.TicksPerSecond);

        public Data(int duration, long startTime)
        {
            DurationSec = duration;
            StartTime = startTime;
        }
    }

    private const int UNLIMITED = 14400; // 4 hours - anything above considered unlimited duration

    private readonly int cTextureId;
    private readonly int cDurationSec;

    private readonly Dictionary<int, Data> data = [];

    public AuraTimeReader(int cTextureId, int cDurationSec)
    {
        this.cTextureId = cTextureId;
        this.cDurationSec = cDurationSec;
        Reset();
    }

    public void Update(IAddonDataProvider reader)
    {
        int textureId = reader.GetInt(cTextureId);
        if (textureId == 0) return;

        int durationSec = reader.GetInt(cDurationSec);
        data[textureId] = new(durationSec, GetTimestamp());
    }

    public void Reset()
    {
        data.Clear();
    }

    public int GetRemainingTimeMs(int textureId)
    {
        return data.TryGetValue(textureId, out Data d) ?
            Math.Max(0, d.DurationSec >= UNLIMITED ? 1 : (int)((d.End - GetTimestamp()) / TimeSpan.TicksPerMillisecond))
            : 0;
    }

    public int GetTotalTimeMs(KeyAction keyAction)
    {
        return data[keyAction.SlotIndex].DurationSec * 1000;
    }

}
