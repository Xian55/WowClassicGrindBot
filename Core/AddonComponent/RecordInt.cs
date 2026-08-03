using System;
using System.Runtime.CompilerServices;
using static System.Diagnostics.Stopwatch;

namespace Core;

public sealed class RecordInt
{
    private const int NO_HIGH_CELL = -1;

    private readonly int cell;

    private readonly int highCell;
    private readonly int highMultiplier;

    public int Value { private set; get; }

    public int _Value() => Value;

    public long LastChanged { private set; get; }

    public int ElapsedMs() => (int)GetElapsedTime(LastChanged).TotalMilliseconds;

    public event Action? Changed;

    public RecordInt(int cell)
    {
        this.cell = cell;
        this.highCell = NO_HIGH_CELL;
    }

    /// <summary>
    /// For a value the addon has to split across two cells because one cannot
    /// hold it - the purse, for one. Reassembled on every read so the rest of
    /// the type, and every caller, stays unaware of the split.
    /// </summary>
    public RecordInt(int lowCell, int highCell, int highMultiplier)
    {
        this.cell = lowCell;
        this.highCell = highCell;
        this.highMultiplier = highMultiplier;
    }

    private int Read(IAddonDataProvider reader) =>
        highCell == NO_HIGH_CELL
            ? reader.GetInt(cell)
            : reader.GetInt(cell) + (reader.GetInt(highCell) * highMultiplier);

    public bool Updated(IAddonDataProvider reader)
    {
        int temp = Value;
        Value = Read(reader);

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
        Value = Read(reader);

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