using System;
using System.Runtime.CompilerServices;
using static System.Diagnostics.Stopwatch;

namespace Core;

public sealed class RecordInt
{
    private readonly int cell;

    public int Value { private set; get; }

    public int _Value() => Value;

    public long LastChanged { private set; get; }

    public int ElapsedMs() => (int)GetElapsedTime(LastChanged).TotalMilliseconds;

    public event Action? Changed;

    public RecordInt(int cell)
    {
        this.cell = cell;
    }

    public bool Updated(IAddonDataProvider reader)
    {
        int temp = Value;
        Value = reader.GetInt(cell);

        if (temp == Value)
        {
            return false;
        }

        Changed?.Invoke();
        UpdateTime();
        return true;
    }

    public void Update(IAddonDataProvider reader)
    {
        int temp = Value;
        Value = reader.GetInt(cell);

        if (temp == Value)
        {
            return;
        }

        Changed?.Invoke();
        UpdateTime();
    }

    public void UpdateExcludingLeastSignificantDigits(IAddonDataProvider reader, int excludeDigit)
    {
        int temp = Value / excludeDigit;
        Value = reader.GetInt(cell) / excludeDigit;

        if (temp == Value)
        {
            return;
        }

        Changed?.Invoke();
        UpdateTime();
    }

    public void UpdateIncludeLeastSignificantDigit(IAddonDataProvider reader, int includeDigit)
    {
        int temp = Value % includeDigit;
        Value = reader.GetInt(cell) % includeDigit;

        if (temp == Value)
        {
            return;
        }

        Changed?.Invoke();
        UpdateTime();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UpdateTime()
    {
        LastChanged = GetTimestamp();
    }

    public void Reset()
    {
        Value = 0;
        LastChanged = default;
    }

    public void ForceUpdate(int value)
    {
        Value = value;
    }
}