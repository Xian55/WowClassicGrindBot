using Vortice.Direct3D11;

namespace Game;

public interface IGpuTextureProvider
{
    ID3D11Device Device { get; }
    ID3D11DeviceContext DeviceContext { get; }

    /// <summary>
    /// Gets the most recently captured GPU texture.
    /// Returns null if no frame is available.
    /// The texture may have Staging usage and no ShaderResource bind flag --
    /// callers should copy to a ShaderResource-capable texture before binding to shaders.
    /// </summary>
    ID3D11Texture2D? GetCapturedTexture();
}
