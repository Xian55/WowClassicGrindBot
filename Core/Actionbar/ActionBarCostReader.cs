using Microsoft.Extensions.Logging;

using System;
using System.Runtime.CompilerServices;

using static Core.ActionBar;

namespace Core;

#pragma warning disable CS0162

public readonly record struct ActionBarCost(PowerType PowerType, int Cost);

public sealed class ActionBarCostReader : IReader
{
#if DEBUG
    private const bool DEBUG = true;
#else
    private const bool DEBUG = false;
#endif

    private const int COST_ORDER = 10000;
    private const int POWER_TYPE_MOD = 100;

    private const int cActionbarMeta = 35;
    private const int cActionbarNum = 36;

    private static readonly ActionBarCost defaultCost = new(PowerType.Mana, 0);
    public static ref readonly ActionBarCost DefaultCost => ref defaultCost;

    public ActionBarCost[] Data { get; init; }

    public int Count { get; private set; }

    private readonly ILogger<ActionBarCostReader> logger;

    public ActionBarCostReader(ILogger<ActionBarCostReader> logger)
    {
        this.logger = logger;

        Data = new ActionBarCost[CELL_COUNT * BIT_PER_CELL * NUM_OF_COST];

        Reset();
    }

    public void Update(IAddonDataProvider reader)
    {
        int meta = reader.GetInt(cActionbarMeta);
        int cost = reader.GetInt(cActionbarNum);
        if ((cost == 0 && meta == 0) || meta < ACTION_SLOT_MUL)
            return;

        int slotIdx = (meta / ACTION_SLOT_MUL) - 1;
        int costIdx = (meta / COST_ORDER % 10) - 1;
        int type = meta % POWER_TYPE_MOD;

        int index = (slotIdx * NUM_OF_COST) + costIdx;

        ActionBarCost old = Data[index];
        Data[index] = new((PowerType)type, cost);

        if (!old.Equals(Data[index]))
        {
            if (DEBUG)
                logger.LogInformation($"[{index,3}][{slotIdx + 1,3}][{costIdx}] {cost} {((PowerType)type).ToStringF()}");

            Count++;
        }
    }

    public void Reset()
    {
        Count = 0;

        var span = Data.AsSpan();
        span.Fill(DefaultCost);
    }

    [SkipLocalsInit]
    public ActionBarCost Get(KeyAction keyAction, int costIndex = 0)
    {
        int slotIdx = keyAction.SlotIndex;
        int index = (slotIdx * NUM_OF_COST) + costIndex;
        return Data[index];
    }
}
