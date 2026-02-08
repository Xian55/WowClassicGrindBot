using Core.Database;

using Microsoft.Extensions.DependencyInjection;

using SharedLib;

using System;
using System.Collections.Immutable;
using System.Threading;

using static System.Diagnostics.Stopwatch;

namespace Core;

public sealed class AddonReader : IAddonReader
{
    private readonly IAddonDataProvider reader;

    private readonly PlayerReader playerReader;
    private readonly CreatureDB creatureDb;

    private readonly CombatLog combatLog;
    private readonly TextReader textReader;

    private readonly ImmutableArray<IReader> readers;

    public event Action? AddonDataChanged;

    public ManualResetEventSlim DataReady { get; }

    public RecordInt GlobalTime { get; }

    private int previousGlobalTime;

    private int lastTargetGuid = -1;
    public string TargetName { get; private set; } = string.Empty;

    private int lastMouseOverId = -1;
    public string MouseOverName { get; private set; } = string.Empty;

    public double AvgUpdateLatency { private set; get; }

    public AddonReader(IAddonDataProvider reader,
        PlayerReader playerReader, ManualResetEventSlim resetEvent,
        CreatureDB creatureDb,
        CombatLog combatLog,
        TextReader textReader,
        DataFrame[] frames,
        IServiceProvider sp)
    {
        this.reader = reader;
        this.creatureDb = creatureDb;
        this.combatLog = combatLog;
        this.textReader = textReader;
        this.playerReader = playerReader;
        DataReady = resetEvent;

        GlobalTime = new(frames.Length - 2);

        readers = sp.GetServices<IReader>().ToImmutableArray();
    }

    public void Update()
    {
        IAddonDataProvider reader = this.reader;
        reader.UpdateData();

        long lastUpdate = GlobalTime.LastChanged;

        if (!GlobalTime.Updated(reader))
            return;

        AvgUpdateLatency = GetElapsedTime(lastUpdate).TotalMilliseconds;

        if (GlobalTime.Value < AddonTicks.INIT_PHASE || GlobalTime.Value < previousGlobalTime)
        {
            previousGlobalTime = GlobalTime.Value;
            FullReset();
            return;
        }

        previousGlobalTime = GlobalTime.Value;

        ReadOnlySpan<IReader> span = readers.AsSpan();
        for (int i = 0; i < span.Length; i++)
        {
            span[i].Update(reader);
        }

        if (lastTargetGuid != playerReader.TargetGuid)
        {
            lastTargetGuid = playerReader.TargetGuid;

            TargetName =
                creatureDb.Entries.TryGetValue(playerReader.TargetId, out Creature c)
                ? c.Name
                : textReader.LastTargetName;
        }

        if (lastMouseOverId != playerReader.MouseOverId)
        {
            lastMouseOverId = playerReader.MouseOverId;
            MouseOverName =
                creatureDb.Entries.TryGetValue(playerReader.MouseOverId, out Creature c)
                ? c.Name
                : string.Empty;
        }

        DataReady.Set();
    }

    public void SessionReset()
    {
        combatLog.Reset();
    }

    public void FullReset()
    {
        ReadOnlySpan<IReader> span = readers.AsSpan();
        for (int i = 0; i < span.Length; i++)
        {
            span[i].Reset();
        }

        SessionReset();
    }

    public void UpdateUI()
    {
        AddonDataChanged?.Invoke();
    }
}