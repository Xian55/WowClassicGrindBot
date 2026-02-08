using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SharedLib;

public interface IMinimapImageProvider
{
    Image<Bgra32> MiniMapImage { get; }

    Rectangle MiniMapRect { get; }

    MinimapSettings MinimapSettings { get; }
}
