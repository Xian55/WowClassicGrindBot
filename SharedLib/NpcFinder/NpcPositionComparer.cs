using SharedLib.Extensions;

using SixLabors.ImageSharp;

using System.Collections.Generic;

namespace SharedLib.NpcFinder;

internal sealed class NpcPositionComparer : IComparer<NpcPosition>
{
    private readonly IScreenImageProvider bitmapProvider;

    public NpcPositionComparer(IScreenImageProvider bitmapProvider)
    {
        this.bitmapProvider = bitmapProvider;
    }

    public int Compare(NpcPosition x, NpcPosition y)
    {
        Point origin = bitmapProvider.ScreenRect.Centre();
        float dx = ImageSharpPointExt.SqrDistance(origin, x.ClickPoint);
        float dy = ImageSharpPointExt.SqrDistance(origin, y.ClickPoint);

        return dx.CompareTo(dy);
    }
}
