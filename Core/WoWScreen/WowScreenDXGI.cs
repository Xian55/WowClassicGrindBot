//#define SAVE_ADDON_IMAGE
//#define SAVE_SCREEN_IMAGE
//#define SAVE_MINIMAP_IMAGE

using Game;

using Microsoft.Extensions.Logging;

using SharedLib;

using SharpGen.Runtime;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

using WinAPI;

using static WinAPI.NativeMethods;

namespace Core;

public sealed class WowScreenDXGI : IWowScreen, IAddonDataProvider, IGpuTextureProvider
{
    private readonly ILogger<WowScreenDXGI> logger;
    private readonly WowProcess process;
    private const int Bgra32Size = ScreenCaptureHelper.Bgra32Size;

    public event Action? OnChanged;

    public bool Enabled { get; set; }
    public bool EnablePostProcess { get; set; }

    public bool MinimapEnabled { get; set; }

    public Rectangle ScreenRect => screenRect;
    private Rectangle screenRect;

    private readonly Vortice.RawRect monitorRect;

    public Image<Bgra32> ScreenImage { get; init; }

    private readonly SixLabors.ImageSharp.Configuration ContiguousJpegConfiguration
        = new(new JpegConfigurationModule()) { PreferContiguousImageBuffers = true };

    public Rectangle MiniMapRect { get; private set; }
    public Image<Bgra32> MiniMapImage { get; init; }

    private static readonly FeatureLevel[] s_featureLevels =
    [
        FeatureLevel.Level_12_1,
        FeatureLevel.Level_12_0,
        FeatureLevel.Level_11_0,
    ];

    private readonly IDXGIAdapter adapter;
    private readonly IDXGIOutput output;
    private readonly IDXGIOutput1 output1;

    private readonly ID3D11Texture2D minimapTexture;
    private readonly ID3D11Texture2D screenTexture;
    private ID3D11Texture2D addonTexture = null!;

    private readonly ID3D11Device device;
    private readonly IDXGIOutputDuplication duplication;

    private readonly bool windowedMode;
    private bool deviceRemoved;

    // IGpuTextureProvider -- Default-usage copy of client area for GPU compute shader
    private ID3D11Texture2D? lastCapturedTexture;
    private readonly Lock gpuTextureLock = new();
    private ID3D11Texture2D? gpuTextureCopy;
    private int gpuTextureWidth;
    private int gpuTextureHeight;

    ID3D11Device IGpuTextureProvider.Device => device;
    ID3D11DeviceContext IGpuTextureProvider.DeviceContext => device.ImmediateContext;

    ID3D11Texture2D? IGpuTextureProvider.GetCapturedTexture()
    {
        using (gpuTextureLock.EnterScope())
        {
            return gpuTextureCopy;
        }
    }

    // IAddonDataProvider

    private SixLabors.ImageSharp.Size addonSize;
    private DataFrame[] frames = null!;
    private Image<Bgra32> addonImage = null!;

    public int[] Data { get; private set; } = [];
    public StringBuilder TextBuilder { get; } = new(3);

    private const int MiniMapSize = 200;

    public MinimapSettings MinimapSettings =>
        Data.Length > 2
        ? new(Data[16], Data[17])
        : new(9013, 220016); //debug only

    public WowScreenDXGI(ILogger<WowScreenDXGI> logger,
        WowProcess process, DataFrame[] frames)
    {
        this.logger = logger;
        this.process = process;

        GetRectangle(out screenRect);
        ScreenImage = new(ContiguousJpegConfiguration, screenRect.Width, screenRect.Height);

        MiniMapRect = new(0, 0, MiniMapSize, MiniMapSize);
        MiniMapImage = new(ContiguousJpegConfiguration, MiniMapSize, MiniMapSize);

        IntPtr hMonitor =
            MonitorFromWindow(process.MainWindowHandle, MONITOR_DEFAULT_TO_NULL);

        Result result;

        IDXGIFactory1 factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        result = factory.EnumAdapters(0, out adapter);
        if (result == Result.Fail)
            throw new Exception($"Unable to enumerate adapter! {result.Description}");

        uint srcIdx = 0;
        do
        {
            result = adapter.EnumOutputs(srcIdx, out output);
            if (result == Result.Ok &&
                output.Description.Monitor == hMonitor)
            {
                monitorRect = output.Description.DesktopCoordinates;
                windowedMode =
                    (monitorRect.Right - monitorRect.Left) != screenRect.Width ||
                    (monitorRect.Bottom - monitorRect.Top) != screenRect.Height;

                NormalizeScreenRect();

                break;
            }
            srcIdx++;
        } while (result != Result.Fail);

        output1 = output.QueryInterface<IDXGIOutput1>();
        result = D3D11.D3D11CreateDevice(adapter, DriverType.Unknown,
            DeviceCreationFlags.None, s_featureLevels, out device!);

        if (result == Result.Fail)
            throw new Exception($"device is null {result.Description}");

        using (ID3D11Multithread multithread = device.QueryInterface<ID3D11Multithread>())
        {
            multithread.SetMultithreadProtected(true);
        }

        duplication = output1.DuplicateOutput(device);

        Texture2DDescription screenTextureDesc = new()
        {
            CPUAccessFlags = CpuAccessFlags.Read,
            BindFlags = BindFlags.None,
            Format = Format.B8G8R8A8_UNorm,
            Width = (uint)screenRect.Right,
            Height = (uint)screenRect.Bottom,
            MiscFlags = ResourceOptionFlags.None,
            MipLevels = 1,
            ArraySize = 1,
            SampleDescription = { Count = 1, Quality = 0 },
            Usage = ResourceUsage.Staging
        };
        screenTexture = device.CreateTexture2D(screenTextureDesc);

        InitFrames(frames);

        Texture2DDescription miniMapTextureDesc = new()
        {
            CPUAccessFlags = CpuAccessFlags.Read,
            BindFlags = BindFlags.None,
            Format = Format.B8G8R8A8_UNorm,
            Width = (uint)MiniMapRect.Right,
            Height = (uint)MiniMapRect.Bottom,
            MiscFlags = ResourceOptionFlags.None,
            MipLevels = 1,
            ArraySize = 1,
            SampleDescription = { Count = 1, Quality = 0 },
            Usage = ResourceUsage.Staging
        };
        minimapTexture = device.CreateTexture2D(miniMapTextureDesc);

        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("{ScreenRect} - Windowed Mode: {WindowedMode} - Scale: {Scale:F2} - Monitor Rect: {MonitorRect} - Monitor Index: {MonitorIndex}",
                screenRect, windowedMode, DPI2PPI(GetDpi()), monitorRect, srcIdx);
    }

    public void Dispose()
    {
        try { duplication?.ReleaseFrame(); } catch { }
        try { duplication?.Dispose(); } catch { }

        try { gpuTextureCopy?.Dispose(); } catch { }
        try { lastCapturedTexture?.Dispose(); } catch { }
        try { minimapTexture.Dispose(); } catch { }
        try { addonTexture.Dispose(); } catch { }
        try { screenTexture.Dispose(); } catch { }

        try { device.Dispose(); } catch { }
        try { adapter.Dispose(); } catch { }
        try { output1.Dispose(); } catch { }
        try { output.Dispose(); } catch { }
    }

    public void InitFrames(DataFrame[] frames)
    {
        this.frames = frames;
        Data = new int[frames.Length];

        addonSize = new();
        for (int i = 0; i < frames.Length; i++)
        {
            addonSize.Width = Math.Max(addonSize.Width, frames[i].X);
            addonSize.Height = Math.Max(addonSize.Height, frames[i].Y);
        }
        addonSize.Width++;
        addonSize.Height++;

        addonImage = new(ContiguousJpegConfiguration, addonSize.Width, addonSize.Height);

        Texture2DDescription addonTextureDesc = new()
        {
            CPUAccessFlags = CpuAccessFlags.Read,
            BindFlags = BindFlags.None,
            Format = Format.B8G8R8A8_UNorm,
            Width = (uint)addonSize.Width,
            Height = (uint)addonSize.Height,
            MiscFlags = ResourceOptionFlags.None,
            MipLevels = 1,
            ArraySize = 1,
            SampleDescription = { Count = 1, Quality = 0 },
            Usage = ResourceUsage.Staging
        };

        addonTexture?.Dispose();
        addonTexture = device.CreateTexture2D(addonTextureDesc);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("DataFrames {FrameCount} - Texture: {AddonSize}", frames.Length, addonSize);
    }

    [SkipLocalsInit]
    public void Update()
    {
        if (deviceRemoved)
            return;

        if (windowedMode)
        {
            GetRectangle(out screenRect);
            NormalizeScreenRect();

            if (screenRect.X < 0 ||
                screenRect.Y < 0 ||
                screenRect.Right > output.Description.DesktopCoordinates.Right ||
                screenRect.Bottom > output.Description.DesktopCoordinates.Bottom)
                return;
        }

        try
        {
            duplication.ReleaseFrame();
        }
        catch (SharpGenException) when (CheckDeviceRemoved())
        {
            return;
        }

        Result result = duplication.AcquireNextFrame(5,
            out OutduplFrameInfo frame,
            out IDXGIResource idxgiResource);

        // If only the pointer was updated(that is, the desktop image was not updated),
        // the AccumulatedFrames, TotalMetadataBufferSize, LastPresentTime members are set to zero.
        if (!result.Success ||
            frame.AccumulatedFrames == 0 ||
            frame.TotalMetadataBufferSize == 0 ||
            frame.LastPresentTime == 0)
        {
            return;
        }

        try
        {
            ID3D11Texture2D texture
                = idxgiResource.QueryInterface<ID3D11Texture2D>();

            lastCapturedTexture?.Dispose();
            lastCapturedTexture = texture;

            // Copy client area to GPU texture for compute shader
            // (desktop duplication captures the full monitor; the shader uses
            //  0-based coordinates relative to the client area)
            using (gpuTextureLock.EnterScope())
            {
                if (gpuTextureCopy == null ||
                    gpuTextureWidth != screenRect.Width ||
                    gpuTextureHeight != screenRect.Height)
                {
                    ID3D11Texture2D? oldTexture = gpuTextureCopy;
                    gpuTextureCopy = device.CreateTexture2D(new Texture2DDescription
                    {
                        Width = (uint)screenRect.Width,
                        Height = (uint)screenRect.Height,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = Format.B8G8R8A8_UNorm,
                        SampleDescription = new(1, 0),
                        Usage = ResourceUsage.Default,
                        BindFlags = BindFlags.ShaderResource
                    });
                    gpuTextureWidth = screenRect.Width;
                    gpuTextureHeight = screenRect.Height;
                    oldTexture?.Dispose();
                }
            }

            Box gpuBox = new(
                screenRect.X, screenRect.Y, 0,
                screenRect.Right, screenRect.Bottom, 1);
            device.ImmediateContext.CopySubresourceRegion(
                gpuTextureCopy, 0, 0, 0, 0, texture, 0, gpuBox);

            if (frames.Length > 2)
                UpdateAddonImage(texture);

            if (Enabled)
                UpdateScreenImage(texture);

            if (MinimapEnabled)
                UpdateMinimapImage(texture);
        }
        catch (SharpGenException) when (CheckDeviceRemoved())
        {
            // Device lost — silently stop capturing
        }
    }

    private bool CheckDeviceRemoved()
    {
        try
        {
            if (device.DeviceRemovedReason.Success)
                return false;
        }
        catch { }

        deviceRemoved = true;
        logger.LogError("GPU device removed (DXGI_ERROR_DEVICE_REMOVED). Screen capture disabled. Restart the bot to recover.");
        return true;
    }

    [SkipLocalsInit]
    private void UpdateAddonImage(ID3D11Texture2D texture)
    {
        if (!addonImage.DangerousTryGetSinglePixelMemory(out Memory<Bgra32> memory))
            return;

        Box areaOnScreen = new(
            screenRect.X, screenRect.Y, 0,
            screenRect.X + addonSize.Width,
            screenRect.Y + addonSize.Height, 1);

        device.ImmediateContext
            .CopySubresourceRegion(addonTexture, 0, 0, 0, 0, texture, 0, areaOnScreen);

        MappedSubresource resource = device.ImmediateContext
            .Map(addonTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);

        try
        {
            int rowPitch = (int)resource.RowPitch;
            ReadOnlySpan<byte> src = resource.AsSpan(addonSize.Height * rowPitch);
            Span<byte> dest = MemoryMarshal.Cast<Bgra32, byte>(memory.Span);

            ScreenCaptureHelper.CopyRegion(src, rowPitch, 0, 0, dest, addonSize.Width, addonSize.Height);

#if SAVE_ADDON_IMAGE
            addonImage.SaveAsJpeg("addon.jpg");
#endif
        }
        finally
        {
            device.ImmediateContext.Unmap(addonTexture, 0);
        }
    }

    [SkipLocalsInit]
    private void UpdateScreenImage(ID3D11Texture2D texture)
    {
        if (!ScreenImage.DangerousTryGetSinglePixelMemory(out Memory<Bgra32> memory))
            return;

        Box areaOnScreen = new(
            screenRect.X, screenRect.Y, 0,
            screenRect.Right, screenRect.Bottom, 1);

        device.ImmediateContext
            .CopySubresourceRegion(screenTexture, 0, 0, 0, 0, texture, 0, areaOnScreen);

        MappedSubresource resource = device.ImmediateContext
            .Map(screenTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);

        try
        {
            int rowPitch = (int)resource.RowPitch;
            ReadOnlySpan<byte> src = resource.AsSpan(screenRect.Height * rowPitch);
            Span<byte> dest = MemoryMarshal.Cast<Bgra32, byte>(memory.Span);

            ScreenCaptureHelper.CopyRegion(src, rowPitch, 0, 0, dest, screenRect.Width, screenRect.Height);

#if SAVE_SCREEN_IMAGE
            ScreenImage.SaveAsJpeg("screen.jpg");
#endif
        }
        finally
        {
            device.ImmediateContext.Unmap(screenTexture, 0);
        }
    }

    [SkipLocalsInit]
    private void UpdateMinimapImage(ID3D11Texture2D texture)
    {
        if (!MiniMapImage.DangerousTryGetSinglePixelMemory(out Memory<Bgra32> memory))
            return;

        Box areaOnScreen = new(
            screenRect.Right - MiniMapSize, screenRect.Y, 0,
            screenRect.Right, screenRect.Top + MiniMapRect.Bottom, 1);

        device.ImmediateContext
            .CopySubresourceRegion(minimapTexture, 0, 0, 0, 0, texture, 0, areaOnScreen);

        MappedSubresource resource = device.ImmediateContext
            .Map(minimapTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);

        try
        {
            int rowPitch = (int)resource.RowPitch;
            ReadOnlySpan<byte> src = resource.AsSpan(MiniMapRect.Height * rowPitch);
            Span<byte> dest = MemoryMarshal.Cast<Bgra32, byte>(memory.Span);

            ScreenCaptureHelper.CopyRegion(src, rowPitch, 0, 0, dest, MiniMapRect.Width, MiniMapRect.Height);
        }
        finally
        {
            device.ImmediateContext.Unmap(minimapTexture, 0);
        }

#if SAVE_MINIMAP_IMAGE
        MiniMapImage.SaveAsJpeg("minimap.jpg");
#endif
    }

    public void UpdateData()
    {
        if (frames.Length <= 2)
            return;

        IAddonDataProvider.InternalUpdate(addonImage, frames, Data);
    }

    public void PostProcess()
    {
        OnChanged?.Invoke();
    }

    public void GetPosition(ref Point point)
    {
        NativeMethods.GetPosition(process.MainWindowHandle, ref point);
    }

    public void GetRectangle(out Rectangle rect)
    {
        NativeMethods.GetWindowRect(process.MainWindowHandle, out rect);
    }

    private void NormalizeScreenRect()
    {
        screenRect.X -= monitorRect.Left;
        screenRect.Y -= monitorRect.Top;

        if (screenRect.X < 0)
            screenRect.X = 0;

        if (screenRect.Y < 0)
            screenRect.Y = 0;
    }
}