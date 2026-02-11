using Microsoft.Extensions.Logging;

using SharedLib.Extensions;

using SixLabors.ImageSharp;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

using static System.Diagnostics.Stopwatch;

namespace SharedLib.NpcFinder;

public sealed partial class NpcNameFinder
{
    private readonly ILogger logger;
    private readonly IScreenImageProvider bitmapProvider;
    private readonly INpcResetEvent resetEvent;
    private readonly INpcLineSegmentProvider lineSegmentProvider;

    private const int bytesPerPixel = 4;

    public readonly int screenMid;
    public readonly int screenTargetBuffer;
    public readonly int screenMidBuffer;
    public readonly int screenAddBuffer;

    public readonly Rectangle Area;

    private const float refWidth = 1920;
    private const float refHeight = 1080;

    private const long RemoveAddThreatAfterMS = 1500;

    public readonly float ScaleToRefWidth = 1;
    public readonly float ScaleToRefHeight = 1;

    private SearchMode searchMode = SearchMode.Fuzzy;
    public NpcNames nameType { private set; get; } =
        NpcNames.Enemy | NpcNames.Neutral;

    public ArraySegment<NpcPosition> Npcs { get; private set; } =
        Array.Empty<NpcPosition>();

    public int NpcCount => Npcs.Count;
    public int AddCount { private set; get; }
    public int TargetCount { private set; get; }
    public bool MobsVisible => NpcCount > 0;
    public bool PotentialAddsExist { get; private set; }
    public bool _PotentialAddsExist() => PotentialAddsExist;

    private long LastPotentialAddsSeen;

    private readonly NpcPositionComparer npcPosComparer;

    private const int topOffset = 117;
    public int WidthDiff { get; set; } = 4;

    private float heightMul;
    public int HeightMulti { get; set; }
    public int MinHeight { get; set; } = 16;
    public int HeightOffset1 { get; set; } = 10;
    public int HeightOffset2 { get; set; } = 2;

    public NpcNameFinder(ILogger logger, IScreenImageProvider bitmapProvider,
        INpcResetEvent resetEvent, INpcLineSegmentProvider lineSegmentProvider)
    {
        this.logger = logger;
        this.bitmapProvider = bitmapProvider;
        this.resetEvent = resetEvent;
        this.lineSegmentProvider = lineSegmentProvider;

        ConfigureProvider();

        npcPosComparer = new(bitmapProvider);

        ScaleToRefWidth = ScaleWidth(1);
        ScaleToRefHeight = ScaleHeight(1);

        CalculateHeightMultipiler();

        Area = new Rectangle(new Point(0, (int)ScaleHeight(topOffset)),
            new Size(
                (int)(bitmapProvider.ScreenImage.Width * 0.87f),
                (int)(bitmapProvider.ScreenImage.Height * 0.6f)));

        int screenWidth = bitmapProvider.ScreenRect.Width;
        screenMid = screenWidth / 2;
        screenMidBuffer = screenWidth / 15;
        screenTargetBuffer = screenMidBuffer / 2;
        screenAddBuffer = screenMidBuffer * 3;
    }

    private float ScaleWidth(int value)
    {
        return value * (bitmapProvider.ScreenRect.Width / refWidth);
    }

    private float ScaleHeight(int value)
    {
        return value * (bitmapProvider.ScreenRect.Height / refHeight);
    }

    private void CalculateHeightMultipiler()
    {
        HeightMulti = nameType == NpcNames.Corpse ? 10 : 4;
        heightMul = ScaleHeight(HeightMulti);
    }

    private void ConfigureProvider()
    {
        if (lineSegmentProvider is IConfigurableLineSegmentProvider configurable)
        {
            configurable.Configure(searchMode, nameType);
        }
    }

    public bool ChangeNpcType(NpcNames type)
    {
        if (nameType == type)
            return false;

        resetEvent.ChangeSet();

        TargetCount = 0;
        AddCount = 0;
        Npcs = Array.Empty<NpcPosition>();

        nameType = type;

        switch (type)
        {
            case NpcNames.Corpse:
                searchMode = SearchMode.Simple;
                break;
            default:
                searchMode = SearchMode.Fuzzy;
                break;
        }

        CalculateHeightMultipiler();
        ConfigureProvider();

        if (logger.IsEnabled(LogLevel.Information))
            LogTypeChanged(logger, type.ToStringF(), searchMode.ToStringF());

        if (nameType == NpcNames.None)
            resetEvent.ChangeReset();

        return true;
    }

    public void WaitForUpdate(CancellationToken token = default)
    {
        resetEvent.Wait(token);
    }

    public void Update()
    {
        resetEvent.ChangeReset();
        resetEvent.Reset();

        float minLength = ScaleWidth(MinHeight);
        float minEndLength = minLength - ScaleWidth(WidthDiff);

        ReadOnlySpan<LineSegment> lineSegments =
            lineSegmentProvider.GetLineSegments(Area, minLength, minEndLength);

        Npcs = DetermineNpcs(lineSegments);

        TargetCount = Npcs.Count(TargetsCount);
        AddCount = Npcs.Count(IsAdd);

        if (AddCount > 0)
        {
            PotentialAddsExist = true;
            LastPotentialAddsSeen = GetTimestamp();
        }
        else
        {
            if (PotentialAddsExist &&
                GetElapsedTime(LastPotentialAddsSeen).TotalMilliseconds > RemoveAddThreatAfterMS)
            {
                PotentialAddsExist = false;
                AddCount = 0;
            }
        }

        resetEvent.Set();
    }

    [SkipLocalsInit]
    private ArraySegment<NpcPosition> DetermineNpcs(ReadOnlySpan<LineSegment> data)
    {
        int count = 0;

        var pool = ArrayPool<NpcPosition>.Shared;
        NpcPosition[] npcs = pool.Rent(data.Length);

        float offset1 = ScaleHeight(HeightOffset1);
        float offset2 = ScaleHeight(HeightOffset2);

        const int MAX_GROUP = 64;
        Span<bool> inGroup = stackalloc bool[data.Length];
        Span<LineSegment> group = stackalloc LineSegment[MAX_GROUP];

        for (int i = 0; i < data.Length; i++)
        {
            if (inGroup[i])
                continue;

            ref readonly LineSegment current = ref data[i];

            int gc = 0;
            group[gc++] = current;
            int lastY = current.Y;

            for (int j = i + 1; j < data.Length; j++)
            {
                if (gc + 1 >= MAX_GROUP) break;

                ref readonly LineSegment next = ref data[j];
                if (next.Y > current.Y + offset1) break;
                if (next.Y > lastY + offset2) break;

                if (next.XStart > current.XCenter ||
                    next.XEnd < current.XCenter ||
                    next.Y <= lastY)
                    continue;

                lastY = next.Y;

                inGroup[j] = true;
                group[gc++] = next;
            }

            if (gc <= 0)
                continue;

            ref LineSegment n = ref group[0];
            Rectangle rect = new(n.XStart, n.Y, n.XEnd - n.XStart, 1);

            for (int g = 1; g < gc; g++)
            {
                n = group[g];

                rect.X = Math.Min(rect.X, n.XStart);
                rect.Y = Math.Min(rect.Y, n.Y);

                if (rect.Right < n.XEnd)
                    rect.Width = n.XEnd - n.XStart;

                if (rect.Bottom < n.Y)
                    rect.Height = n.Y - rect.Y;
            }
            int yOffset = YOffset(Area, rect);
            npcs[count++] = new NpcPosition(
                rect.Location, rect.Max(), yOffset, heightMul);
        }

        int lineHeight = 2 * (int)ScaleHeight(MinHeight);

        for (int i = 0; i < count - 1; i++)
        {
            ref readonly NpcPosition ii = ref npcs[i];
            if (ii.Equals(NpcPosition.Empty))
                continue;

            for (int j = i + 1; j < count; j++)
            {
                ref readonly NpcPosition jj = ref npcs[j];
                if (jj.Equals(NpcPosition.Empty))
                    continue;

                Point pi = ii.Rect.Centre();
                Point pj = jj.Rect.Centre();
                float midDistance = ImageSharpPointExt.SqrDistance(pi, pj);

                if (ii.Rect.IntersectsWith(jj.Rect) ||
                    midDistance <= lineHeight * lineHeight)
                {
                    Rectangle unionRect = Rectangle.Union(ii.Rect, jj.Rect);

                    int yOffset = YOffset(Area, unionRect);
                    npcs[i] = new(unionRect, yOffset, heightMul);
                    npcs[j] = NpcPosition.Empty;
                }
            }
        }

        int length = MoveEmptyToEnd(npcs, count, NpcPosition.Empty);
        Array.Sort(npcs, 0, length, npcPosComparer);

        pool.Return(npcs);

        return new ArraySegment<NpcPosition>(npcs, 0, Math.Max(0, length - 1));
    }

    [SkipLocalsInit]
    private static int MoveEmptyToEnd<T>(Span<T> span, int count, in T empty)
    {
        int left = 0;
        int right = count - 1;

        while (left <= right)
        {
            if (EqualityComparer<T>.Default.Equals(span[left], empty))
            {
                Swap(span, left, right);
                right--;
            }
            else
            {
                left++;
            }
        }

        return left;

        static void Swap(Span<T> span, int i, int j)
        {
            (span[j], span[i]) = (span[i], span[j]);
        }
    }

    private bool TargetsCount(NpcPosition c)
    {
        return !IsAdd(c) &&
            Math.Abs(c.ClickPoint.X - screenMid) < screenTargetBuffer;
    }

    public bool IsAdd(NpcPosition c)
    {
        return
            (c.ClickPoint.X < screenMid - screenTargetBuffer &&
             c.ClickPoint.X > screenMid - screenAddBuffer) ||
            (c.ClickPoint.X > screenMid + screenTargetBuffer &&
             c.ClickPoint.X < screenMid + screenAddBuffer);
    }

    private int YOffset(Rectangle area, Rectangle npc)
    {
        return npc.Top / area.Top * MinHeight / 4;
    }

    public Point ToScreenCoordinates()
    {
        return new(bitmapProvider.ScreenRect.Location.X, bitmapProvider.ScreenRect.Location.Y);
    }


    #region Logging

    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Information,
        Message = "[NpcNameFinder] type = {type} | mode = {mode}")]
    static partial void LogTypeChanged(ILogger logger, string type, string mode);

    #endregion
}
