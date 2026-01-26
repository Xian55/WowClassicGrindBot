using System;

using static Core.ActionBar;
using static System.Diagnostics.Stopwatch;
using static System.Math;

namespace Core;

public sealed class ActionBarCooldownReader : IReader
{
    private readonly struct Data
    {
        private readonly float durationSec;
        private readonly long start;

        public long End => start + (long)(durationSec * TimeSpan.TicksPerSecond);

        public Data(float durationSec, long start)
        {
            this.durationSec = durationSec;
            this.start = start;
        }
    }

    private const float FRACTION_PART = 10f;

    private const int cActionbarNum = 37;

    private readonly Data[] data;

    public ActionBarCooldownReader()
    {
        data = new Data[CELL_COUNT * BIT_PER_CELL];
        Reset();
    }

    public void Update(IAddonDataProvider reader)
    {
        int value = reader.GetInt(cActionbarNum);
        if (value == 0 || value < ACTION_SLOT_MUL)
            return;

        int slotIdx = (value / ACTION_SLOT_MUL) - 1;
        float durationSec = value % ACTION_SLOT_MUL / FRACTION_PART;

        data[slotIdx] = new(durationSec, GetTimestamp());
    }

    public void Reset()
    {
        var span = data.AsSpan();
        span.Fill(new(0, GetTimestamp()));
    }

    public int Get(KeyAction keyAction)
    {
        int index = keyAction.SlotIndex;

        ref readonly Data d = ref data[index];

        return Max((int)((d.End - GetTimestamp()) / TimeSpan.TicksPerMillisecond), 0);
    }
}
