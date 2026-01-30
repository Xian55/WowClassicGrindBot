using SharedLib;

using System;

namespace Game;

public interface IWowScreen : IRectProvider, IScreenImageProvider, IMinimapImageProvider, IDisposable
{
    bool Enabled { get; set; }

    /// <summary>
    /// When true, continue capturing screen frames even if <see cref="Enabled"/> is false.
    /// </summary>
    bool AlwaysCapture { get; set; }

    bool MinimapEnabled { get; set; }

    bool EnablePostProcess { get; set; }
    void PostProcess();

    event Action OnChanged;

    void Update();
}
