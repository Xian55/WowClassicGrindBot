using Core;
using Core.Goals;

using Game;

using Microsoft.Extensions.Logging;

using SharedLib;
using SharedLib.NpcFinder;

using System;
using System.Text;
using System.Threading;

using static System.Diagnostics.Stopwatch;

#pragma warning disable 0162
#nullable enable

namespace CoreTests;

internal sealed class Test_NpcNameFinder : IDisposable
{
    private const bool saveImage = false;
    private const bool showOverlay = true;

    private const bool LogEachUpdate = false;
    private const bool LogEachDetail = false;

    private const bool debugTargeting = false;
    private const bool debugSkinning = false;
    private const bool debugTargetVsAdd = false;

    private readonly ILogger logger;
    private readonly NpcNameFinder npcNameFinder;
    private readonly NpcNameTargeting npcNameTargeting;
    private readonly NpcNameTargetingLocations locations;

    private readonly IWowScreen screen;

    private readonly StringBuilder stringBuilder;

    private readonly NpcNameOverlay? npcNameOverlay;

    private DateTime lastNpcUpdate;
    private double updateDuration;

    public Test_NpcNameFinder(ILogger logger, WowProcess process,
        IWowScreen screen, ILoggerFactory loggerFactory, NpcNames types)
    {
        this.logger = logger;
        this.screen = screen;

        stringBuilder = new();

        INpcResetEvent npcResetEvent = new NpcResetEvent();
        npcNameFinder = new(logger, screen, npcResetEvent);

        locations = new(npcNameFinder);

        MockMouseOverReader mouseOverReader = new();
        MockGameMenuWindowShown gmws = new();

        Wait wait = new(new(false), new());

        WowProcessInput input = new(loggerFactory.CreateLogger<WowProcessInput>(), new(), process);

        npcNameTargeting = new(loggerFactory.CreateLogger<NpcNameTargeting>(),
            new(), screen, npcNameFinder, locations, input,
            mouseOverReader, new NoBlacklist(), wait, gmws);

        npcNameFinder.ChangeNpcType(types);

        if (showOverlay)
            npcNameOverlay = new(process.MainWindowHandle, npcNameFinder,
                locations, debugTargeting, debugSkinning, debugTargetVsAdd);
    }

    ~Test_NpcNameFinder()
    {
        Dispose();
    }

    public void Dispose()
    {
        npcNameOverlay?.Dispose();
    }

    public void UpdateScreen()
    {
        screen.Update();
    }

    public (double capture, double update) Execute(int NpcUpdateIntervalMs)
    {
        long captureStart = GetTimestamp();
        UpdateScreen();
        double captureDuration = GetElapsedTime(captureStart).TotalMilliseconds;

        if (LogEachUpdate)
        {
            stringBuilder.Length = 0;
            stringBuilder.Append("Capture: ");
            stringBuilder.Append($"{captureDuration:F5}");
            stringBuilder.Append("ms");
        }

        if (DateTime.UtcNow > lastNpcUpdate.AddMilliseconds(NpcUpdateIntervalMs))
        {
            long updateStart = GetTimestamp();
            npcNameFinder.Update();
            updateDuration = GetElapsedTime(updateStart).TotalMilliseconds;

            lastNpcUpdate = DateTime.UtcNow;
        }

        if (LogEachUpdate)
        {
            stringBuilder.Append(" | ");
            stringBuilder.Append($"Update: ");
            stringBuilder.Append($"{updateDuration:F5}");
            stringBuilder.Append("ms");

            logger.LogInformation(stringBuilder.ToString());
        }

        if (saveImage)
        {
            // TODO: save image
        }

        if (LogEachUpdate && LogEachDetail)
        {
            stringBuilder.Length = 0;

            if (npcNameFinder.Npcs.Count > 0)
                stringBuilder.AppendLine();

            int i = 0;
            foreach (NpcPosition n in npcNameFinder.Npcs)
            {
                stringBuilder.Append($"{i,2} ");
                stringBuilder.AppendLine(n.ToString());
                i++;
            }

            logger.LogInformation(stringBuilder.ToString());
        }

        return (captureDuration, updateDuration);
    }

    public bool Execute_FindTargetBy(ReadOnlySpan<CursorType> cursorType)
    {
        return npcNameTargeting.FindBy(cursorType, CancellationToken.None);
    }
}
