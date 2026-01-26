using Game;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using SharedLib.Extensions;
using SharedLib.NpcFinder;

using SixLabors.ImageSharp;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

using static Core.BlacklistSourceType;

namespace Core.Goals;

public sealed partial class NpcNameTargeting : IDisposable
{
    private const int INTERACT_DELAY = 5;

    private readonly ILogger<NpcNameTargeting> logger;
    private readonly CancellationToken token;
    private readonly IWowScreen screen;
    private readonly NpcNameFinder npcNameFinder;
    private readonly NpcNameTargetingLocations locations;
    private readonly IMouseInput input;
    private readonly IMouseOverReader mouseOverReader;
    private readonly Wait wait;

    private readonly CursorClassifier classifier;

    private readonly IBlacklist mouseOverBlacklist;

    private readonly IGameMenuWindowShown gmws;

    private int index;
    private int npcCount = -1;

    public int NpcCount => npcNameFinder.NpcCount;

    private Point[] Targeting => locations.Targeting;
    private Point[] locFindBy => locations.FindBy;

    public NpcNameTargeting(ILogger<NpcNameTargeting> logger,
        CancellationTokenSource cts, IWowScreen screen,
        NpcNameFinder npcNameFinder,
        NpcNameTargetingLocations locations,
        IMouseInput input,
        IMouseOverReader mouseOverReader,
        [FromKeyedServices(MOUSE_OVER)] IBlacklist mouseOverBlacklist,
        Wait wait,
        IGameMenuWindowShown gmws)
    {
        this.logger = logger;
        token = cts.Token;
        this.screen = screen;
        this.npcNameFinder = npcNameFinder;
        this.locations = locations;
        this.input = input;
        this.mouseOverReader = mouseOverReader;
        this.mouseOverBlacklist = mouseOverBlacklist;
        this.wait = wait;

        this.gmws = gmws;

        classifier = new();
    }

    public void Dispose()
    {
        classifier.Dispose();
    }

    public void ChangeNpcType(NpcNames npcNames)
    {
        npcNameFinder.ChangeNpcType(npcNames);
        screen.Enabled = npcNames != NpcNames.None;
    }

    public void Reset()
    {
        npcCount = -1;
        index = 0;
    }

    public void WaitForUpdate(CancellationToken token = default)
    {
        npcNameFinder.WaitForUpdate(token);
    }

    public bool FoundAny()
    {
        return npcNameFinder.NpcCount > 0;
    }

    public bool AcquireNonBlacklisted(CancellationToken token)
    {
        if (npcCount != NpcCount)
        {
            npcCount = NpcCount;
            index = 0;
        }

        if (index > NpcCount - 1)
            return false;

        ReadOnlySpan<NpcPosition> span = npcNameFinder.Npcs;
        ref readonly NpcPosition npc = ref span[index];

        screen.GetRectangle(out Rectangle screenRect);

        Point p = Targeting[Random.Shared.Next(Targeting.Length)];
        p.Offset(npc.ClickPoint);
        p.Offset(npcNameFinder.ToScreenCoordinates());

        if (!screenRect.Contains(p))
        {
            return false;
        }

        input.SetCursorPos(p);
        wait.Update();

        classifier.Classify(out CursorType cls, out _);

        if (cls is CursorType.Kill && mouseOverReader.MouseOverId != 0)
        {
            if (mouseOverBlacklist.Is())
            {
                LogBlacklistAdded(logger, index,
                    mouseOverReader.MouseOverId, npc.Rect);
                index++;
                return false;
            }

            input.InteractMouseOver(token);
            wait.Update();

            LogFoundTarget(logger, cls.ToStringF(), mouseOverReader.MouseOverId,
                npc.Rect);

            return true;
        }

        return false;
    }

    [SkipLocalsInit]
    public bool FindBy(ReadOnlySpan<CursorType> cursors, CancellationToken token)
    {
        int c = locFindBy.Length;
        const int e = 3;
        Span<Point> attempts = stackalloc Point[c + (c * e)];

        float w = npcNameFinder.ScaleToRefWidth;
        float h = npcNameFinder.ScaleToRefHeight;

        screen.GetRectangle(out Rectangle screenRect);

        ReadOnlySpan<NpcPosition> span = npcNameFinder.Npcs;
        for (int i = 0;
            i < span.Length &&
            !token.IsCancellationRequested &&
            !gmws.GameMenuWindowShown();
            i++)
        {
            ref readonly NpcPosition npc = ref span[i];
            for (int j = 0; j < c; j += e)
            {
                Point p = locFindBy[j];
                attempts[j] = p;
                attempts[j + c] = new Point(npc.Rect.Width / 2, p.Y).Scale(w, h);
                attempts[j + c + 1] = new Point(-npc.Rect.Width / 2, p.Y).Scale(w, h);
            }

            for (int k = 0;
                k < attempts.Length &&
                !token.IsCancellationRequested &&
                !gmws.GameMenuWindowShown();
                k++)
            {
                Point p = attempts[k];
                p.Offset(npc.ClickPoint);
                p.Offset(npcNameFinder.ToScreenCoordinates());

                if (!screenRect.Contains(p))
                {
                    continue;
                }

                input.SetCursorPos(p);

                classifier.Classify(out CursorType cls, out _);
                if (cursors.BinarySearch(cls, Comparer<CursorType>.Default) != -1)
                {
                    input.InteractMouseOver(token);
                    LogFoundTarget(logger, cls.ToStringF(), mouseOverReader.MouseOverId, npc.Rect);
                    return true;
                }

                wait.Update(500); // workaround for CoreTests when the wait is not updated by the GlobalTime change
            }
        }
        return false;
    }

    #region Logging

    [LoggerMessage(
        EventId = 0160,
        Level = LogLevel.Warning,
        Message = "NPC {index} added to blacklist {mouseOverId} | {rect}")]
    static partial void LogBlacklistAdded(ILogger logger, int index, int mouseOverId, Rectangle rect);

    [LoggerMessage(
        EventId = 0161,
        Level = LogLevel.Information,
        Message = "NPC found: {cursorType} | {mouseOverId} | {rect}")]
    static partial void LogFoundTarget(ILogger logger, string cursorType, int mouseOverId, Rectangle rect);

    #endregion
}
