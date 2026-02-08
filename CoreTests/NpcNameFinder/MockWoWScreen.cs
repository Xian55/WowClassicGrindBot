using Game;

using SharedLib;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System;

namespace CoreTests;

internal sealed class MockWoWScreen : IWowScreen
{
    public bool Enabled { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public Rectangle ScreenRect => throw new NotImplementedException();

    public nint ProcessHwnd => throw new NotImplementedException();

    public Rectangle MiniMapRect => throw new NotImplementedException();

    public bool EnablePostProcess { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public Image<Bgra32> ScreenImage => throw new NotImplementedException();

    public Image<Bgra32> MiniMapImage => throw new NotImplementedException();

    public bool MinimapEnabled { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public MinimapSettings MinimapSettings => throw new NotImplementedException();

#pragma warning disable CS0067 // The event 'MockWoWScreen.OnScreenChanged' is never used
    public event Action OnChanged;
#pragma warning restore CS0067 // The event 'MockWoWScreen.OnScreenChanged' is never used

    public void GetPosition(ref Point point)
    {
        throw new NotImplementedException();
    }

    public void GetRectangle(out Rectangle rect)
    {
        throw new NotImplementedException();
    }

    public void Update() { }

    public void PostProcess()
    {
        throw new NotImplementedException();
    }
    public void Dispose()
    {
        throw new NotImplementedException();
    }
}
