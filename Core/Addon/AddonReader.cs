using Core.Database;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using SharedLib;

using System;
using System.Collections.Immutable;
using System.Threading;

using static System.Diagnostics.Stopwatch;

namespace Core;

public sealed partial class AddonReader : IAddonReader
{
    private readonly ILogger<AddonReader> logger;
    private readonly IAddonDataProvider reader;

    private readonly PlayerReader playerReader;
    private readonly CreatureDB creatureDb;

    private readonly CombatLog combatLog;
    private readonly TextReader textReader;

    private readonly ImmutableArray<IReader> readers;

    private readonly SpellBookReader spellBookReader;
    private readonly KeyBindingsReader keyBindingsReader;
    private readonly ActionBarTextureReader textureReader;

    private bool awaitingReinitialization;
    private long reinitStartTime;

    public event Action? AddonDataChanged;

    public ManualResetEventSlim DataReady { get; }

    public RecordInt GlobalTime { get; }

    private int previousGlobalTime;

    private int lastTargetGuid = -1;
    public string TargetName { get; private set; } = string.Empty;

    private int lastMouseOverId = -1;
    public string MouseOverName { get; private set; } = string.Empty;

    public double AvgUpdateLatency { private set; get; }

    public AddonReader(ILogger<AddonReader> logger,
        IAddonDataProvider reader,
        PlayerReader playerReader, ManualResetEventSlim resetEvent,
        CreatureDB creatureDb,
        CombatLog combatLog,
        TextReader textReader,
        SpellBookReader spellBookReader,
        KeyBindingsReader keyBindingsReader,
        ActionBarTextureReader textureReader,
        DataFrame[] frames,
        IServiceProvider sp)
    {
        this.logger = logger;
        this.reader = reader;
        this.creatureDb = creatureDb;
        this.combatLog = combatLog;
        this.textReader = textReader;
        this.playerReader = playerReader;
        this.spellBookReader = spellBookReader;
        this.keyBindingsReader = keyBindingsReader;
        this.textureReader = textureReader;
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

        // After FullReset, wait for queue-based readers to reinitialize
        // before signaling DataReady. This pauses the GOAP agent until
        // spell book, bindings, and textures have been repopulated.
        if (awaitingReinitialization)
        {
            if (spellBookReader.IsInitialized &&
                keyBindingsReader.IsInitialized &&
                textureReader.IsInitialized)
            {
                awaitingReinitialization = false;
                float elapsed = (float)GetElapsedTime(reinitStartTime).TotalSeconds;
                LogReinitComplete(logger, elapsed);
            }
            else
            {
                return;
            }
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

        awaitingReinitialization = true;
        reinitStartTime = GetTimestamp();
        LogFullReset(logger);

        SessionReset();
    }

    public void UpdateUI()
    {
        AddonDataChanged?.Invoke();
    }

    [LoggerMessage(
        EventId = 100,
        Level = LogLevel.Information,
        Message = "FullReset: pausing bot until readers reinitialize")]
    static partial void LogFullReset(ILogger logger);

    [LoggerMessage(
        EventId = 101,
        Level = LogLevel.Information,
        Message = "Readers reinitialized after {elapsedSec:F1}s, resuming bot")]
    static partial void LogReinitComplete(ILogger logger, float elapsedSec);
}