using Microsoft.Extensions.Logging;

using SharedLib.Extensions;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

using static SharedLib.NpcFinder.NpcNameColors;

namespace SharedLib.NpcFinder;

public sealed partial class NpcNameFinder
{
    private readonly ILogger logger;
    private readonly IScreenImageProvider bitmapProvider;
    private readonly INpcResetEvent resetEvent;

    private const int bytesPerPixel = 4;

    public readonly int screenMid;
    public readonly int screenTargetBuffer;
    public readonly int screenMidBuffer;
    public readonly int screenAddBuffer;

    public readonly Rectangle Area;

    private const float refWidth = 1920;
    private const float refHeight = 1080;

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

    public DateTime LastPotentialAddsSeen { get; private set; }

    private Func<byte, byte, byte, bool> colorMatcher;

    private readonly NpcPositionComparer npcPosComparer;

    private const int colorFuzz = 40;
    private const int topOffset = 117;
    public int WidthDiff { get; set; } = 4;

    private float heightMul;
    public int HeightMulti { get; set; }
    public int MinHeight { get; set; } = 16;
    public int HeightOffset1 { get; set; } = 10;
    public int HeightOffset2 { get; set; } = 2;

    private readonly ArrayCounter counter;

    public NpcNameFinder(ILogger logger, IScreenImageProvider bitmapProvider,
        INpcResetEvent resetEvent)
    {
        this.logger = logger;
        this.bitmapProvider = bitmapProvider;
        this.resetEvent = resetEvent;

        UpdateSearchMode();

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

        counter = new();
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
        UpdateSearchMode();

        LogTypeChanged(logger, type.ToStringF(), searchMode.ToStringF());

        if (nameType == NpcNames.None)
            resetEvent.ChangeReset();

        return true;
    }

    private void UpdateSearchMode()
    {
        switch (searchMode)
        {
            case SearchMode.Simple:
                BakeSimpleColorMatcher();
                break;
            case SearchMode.Fuzzy:
                BakeFuzzyColorMatcher();
                break;
        }
    }


    #region Simple Color matcher

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void BakeSimpleColorMatcher()
    {
        switch (nameType)
        {
            case NpcNames.Enemy | NpcNames.Neutral:
                colorMatcher = CombinedEnemyNeutrual;
                return;
            case NpcNames.Friendly | NpcNames.Neutral:
                colorMatcher = CombinedFriendlyNeutrual;
                return;
            case NpcNames.Enemy:
                colorMatcher = SimpleColorEnemy;
                return;
            case NpcNames.Friendly:
                colorMatcher = SimpleColorFriendly;
                return;
            case NpcNames.Neutral:
                colorMatcher = SimpleColorNeutral;
                return;
            case NpcNames.Corpse:
                colorMatcher = SimpleColorCorpse;
                return;
            case NpcNames.NamePlate:
                colorMatcher = SimpleColorNamePlate;
                return;
            case NpcNames.None:
                colorMatcher = NoMatch;
                return;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool SimpleColorEnemy(byte r, byte g, byte b)
    {
        return r > sE_R && g <= sE_G && b <= sE_B;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool SimpleColorFriendly(byte r, byte g, byte b)
    {
        return r == sF_R && g > sF_G && b == sF_B;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool SimpleColorNeutral(byte r, byte g, byte b)
    {
        return r > sN_R && g > sN_G && b == sN_B;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool SimpleColorCorpse(byte r, byte g, byte b)
    {
        return r == fC_RGB && g == fC_RGB && b == fC_RGB;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool SimpleColorNamePlate(byte r, byte g, byte b)
    {
        return
            r is sNamePlate_N or sNamePlate_H_R &&
            g is sNamePlate_N or sNamePlate_H_G &&
            b is sNamePlate_N or sNamePlate_H_B;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CombinedFriendlyNeutrual(byte r, byte g, byte b)
    {
        return SimpleColorFriendly(r, g, b) || SimpleColorNeutral(r, g, b);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CombinedEnemyNeutrual(byte r, byte g, byte b)
    {
        return SimpleColorEnemy(r, g, b) || SimpleColorNeutral(r, g, b);
    }

    private static bool NoMatch(byte r, byte g, byte b)
    {
        return false;
    }

    #endregion


    #region Color Fuzziness matcher

    private void BakeFuzzyColorMatcher()
    {
        switch (nameType)
        {
            case NpcNames.Enemy | NpcNames.Neutral | NpcNames.NamePlate:
                colorMatcher = FuzzyEnemyOrNeutralOrNamePlate;
                return;
            case NpcNames.Enemy | NpcNames.Neutral:
                colorMatcher = FuzzyEnemyOrNeutral;
                return;
            case NpcNames.Friendly | NpcNames.Neutral:
                colorMatcher = FuzzyFriendlyOrNeutral;
                return;
            case NpcNames.Enemy:
                colorMatcher = FuzzyEnemy;
                return;
            case NpcNames.Friendly:
                colorMatcher = FuzzyFriendly;
                return;
            case NpcNames.Neutral:
                colorMatcher = FuzzyNeutral;
                return;
            case NpcNames.Corpse:
                colorMatcher = FuzzyCorpse;
                return;
            case NpcNames.NamePlate:
                colorMatcher = FuzzyNamePlate;
                return;
        }
    }

    [SkipLocalsInit]
    private static bool FuzzyColor(
        byte rr, byte gg, byte bb,
        byte r, byte g, byte b,
        int fuzzy)
    {
        unchecked
        {
            int sqrDistance =
                ((rr - r) * (rr - r)) +
                ((gg - g) * (gg - g)) +
                ((bb - b) * (bb - b));
            return sqrDistance <= fuzzy * fuzzy;
        }
    }

    private static bool FuzzyEnemyOrNeutral(byte r, byte g, byte b) =>
        FuzzyColor(fE_R, fE_G, fE_B, r, g, b, colorFuzz) ||
        FuzzyColor(fN_R, fN_G, fN_B, r, g, b, colorFuzz);

    private static bool FuzzyEnemyOrNeutralOrNamePlate(byte r, byte g, byte b) =>
        FuzzyEnemyOrNeutral(r, g, b) ||
        FuzzyNamePlate(r, g, b);

    private static bool FuzzyFriendlyOrNeutral(byte r, byte g, byte b) =>
        FuzzyColor(fF_R, fF_G, fF_B, r, g, b, colorFuzz) ||
        FuzzyColor(fN_R, fN_G, fN_B, r, g, b, colorFuzz);

    private static bool FuzzyEnemy(byte r, byte g, byte b) =>
        FuzzyColor(fE_R, fE_G, fE_B, r, g, b, colorFuzz);

    private static bool FuzzyFriendly(byte r, byte g, byte b) =>
        FuzzyColor(fF_R, fF_G, fF_B, r, g, b, colorFuzz);

    private static bool FuzzyNeutral(byte r, byte g, byte b) =>
        FuzzyColor(fN_R, fN_G, fN_B, r, g, b, colorFuzz);

    private static bool FuzzyCorpse(byte r, byte g, byte b) =>
        FuzzyColor(fC_RGB, fC_RGB, fC_RGB, r, g, b, fuzzCorpse);

    private static bool FuzzyNamePlate(byte r, byte g, byte b) =>
        FuzzyColor(sNamePlate_N, sNamePlate_N, sNamePlate_N, r, g, b, fuzzCorpse) ||
        FuzzyColor(sNamePlate_H_R, sNamePlate_H_G, sNamePlate_H_B, r, g, b, fuzzCorpse);

    #endregion

    public void WaitForUpdate(CancellationToken token = default)
    {
        resetEvent.Wait(token);
    }

    public void Update()
    {
        resetEvent.ChangeReset();
        resetEvent.Reset();

        ReadOnlySpan<LineSegment> lineSegments =
            PopulateLines(colorMatcher, Area,
            ScaleWidth(MinHeight), ScaleWidth(WidthDiff));

        Npcs = DetermineNpcs(lineSegments);

        TargetCount = Npcs.Count(TargetsCount);
        AddCount = Npcs.Count(IsAdd);

        if (AddCount > 0 && TargetCount >= 1)
        {
            PotentialAddsExist = true;
            LastPotentialAddsSeen = DateTime.UtcNow;
        }
        else
        {
            if (PotentialAddsExist &&
                (DateTime.UtcNow - LastPotentialAddsSeen).TotalSeconds > 1)
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

    [SkipLocalsInit]
    private ReadOnlySpan<LineSegment> PopulateLines(
        Func<byte, byte, byte, bool> colorMatcher,
        Rectangle area, float minLength, float lengthDiff)
    {
        const int RESOLUTION = 16;
        int rowSize = (area.Right - area.Left) / RESOLUTION;
        int height = (area.Bottom - area.Top) / RESOLUTION;
        int totalSize = rowSize * height;

        var pooler = ArrayPool<LineSegment>.Shared;
        LineSegment[] segments = pooler.Rent(totalSize);

        float minEndLength = minLength - lengthDiff;

        Rectangle rectangle = new(area.X, area.Y, area.Width, area.Height);

        counter.count = 0;
        LineSegmentOperation operation = new(
            segments,
            rowSize,
            rectangle,
            minLength,
            minEndLength,
            counter,
            colorMatcher,
            bitmapProvider.ScreenImage.Frames[0].PixelBuffer);

        ParallelRowIterator.IterateRows<LineSegmentOperation, LineSegment>(
            Configuration.Default,
            rectangle,
            in operation);

        pooler.Return(segments);
        return new(segments, 0, Math.Min(segments.Length, counter.count));
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