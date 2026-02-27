using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using SharedLib;

using System;

namespace Core;

internal sealed class NullScreenImageProvider : IScreenImageProvider, IDisposable
{
    public Image<Bgra32> ScreenImage { get; } = new(1920, 1080);

    public Rectangle ScreenRect { get; } = new(0, 0, 1920, 1080);

    public void Dispose() => ScreenImage.Dispose();
}
