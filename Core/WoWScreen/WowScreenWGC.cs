//#define SAVE_ADDON_IMAGE
//#define SAVE_SCREEN_IMAGE
//#define SAVE_RAW_FRAME

using Game;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

using WinAPI;

using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

using static WinAPI.NativeMethods;

namespace Core;

/// <summary>
/// Windows Graphics Capture based screen capture implementation.
/// Supports capturing WoW window even when it's behind other windows.
/// Requires Windows 10 version 2004 (build 19041) or later for borderless capture.
/// </summary>
public sealed class WowScreenWGC : IWowScreen, IAddonDataProvider
{
    private readonly ILogger<WowScreenWGC> logger;
    private readonly WowProcess process;
    private readonly int Bgra32Size;

    public event Action? OnChanged;

    public bool Enabled { get; set; }
    public bool EnablePostProcess { get; set; }
    public bool MinimapEnabled { get; set; }

    public Rectangle ScreenRect => screenRect;
    private Rectangle screenRect;

    public Image<Bgra32> ScreenImage { get; init; }

    private readonly SixLabors.ImageSharp.Configuration ContiguousJpegConfiguration
        = new(new JpegConfigurationModule()) { PreferContiguousImageBuffers = true };

    public const int MiniMapSize = 200;
    public Rectangle MiniMapRect { get; private set; }
    public Image<Bgra32> MiniMapImage { get; init; }

    // D3D11 resources
    private static readonly FeatureLevel[] s_featureLevels =
    [
        FeatureLevel.Level_12_1,
        FeatureLevel.Level_12_0,
        FeatureLevel.Level_11_0,
    ];

    // Cached reflection for borderless capture (properties not in SDK 19041)
    private static readonly PropertyInfo? s_borderRequiredProp = typeof(GraphicsCaptureSession)
        .GetProperty("IsBorderRequired", BindingFlags.Public | BindingFlags.Instance);
    private static readonly PropertyInfo? s_cursorEnabledProp = typeof(GraphicsCaptureSession)
        .GetProperty("IsCursorCaptureEnabled", BindingFlags.Public | BindingFlags.Instance);

    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext deviceContext;

    private ID3D11Texture2D? minimapTexture;
    private ID3D11Texture2D? screenTexture;
    private ID3D11Texture2D? addonTexture;

    // WGC resources
    private readonly IDirect3DDevice winrtDevice;
    private GraphicsCaptureItem? captureItem;
    private Direct3D11CaptureFramePool? framePool;
    private GraphicsCaptureSession? captureSession;

    // Double-buffer: WGC writes async, Update() reads
    private readonly Lock frameLock = new();
    private ID3D11Texture2D? writeStagingTexture;
    private ID3D11Texture2D? readStagingTexture;
    private SizeInt32 stagingTextureSize;
    private SizeInt32 latestFrameSize;
    private bool hasNewFrame;

    // Client area offset (WGC captures full window including title bar)
    private Point clientOffset;

    // IAddonDataProvider
    private SixLabors.ImageSharp.Size addonSize;
    private DataFrame[] frames = null!;
    private Image<Bgra32> addonImage = null!;

    public int[] Data { get; private set; } = [];
    public StringBuilder TextBuilder { get; } = new(3);

    public WowScreenWGC(ILogger<WowScreenWGC> logger, WowProcess process, DataFrame[] frames)
    {
        this.logger = logger;
        this.process = process;

        Bgra32Size = Unsafe.SizeOf<Bgra32>();

        GetRectangle(out screenRect);
        clientOffset = NativeMethods.GetClientAreaOffset(process.MainWindowHandle);
        ScreenImage = new(ContiguousJpegConfiguration, screenRect.Width, screenRect.Height);

        MiniMapRect = new(0, 0, MiniMapSize, MiniMapSize);
        MiniMapImage = new(ContiguousJpegConfiguration, MiniMapSize, MiniMapSize);

        // Create D3D11 device
        D3D11.D3D11CreateDevice(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            s_featureLevels,
            out device!);

        deviceContext = device.ImmediateContext;

        // Create WinRT device for WGC
        winrtDevice = GraphicsCaptureInterop.CreateDirect3DDeviceFromD3D11(device)
            ?? throw new InvalidOperationException("Failed to create WinRT Direct3D device");

        InitFrames(frames);
        InitializeCapture();

        logger.LogInformation(
            $"WGC initialized - {screenRect} - ClientOffset: ({clientOffset.X}, {clientOffset.Y}) - " +
            $"Borderless: {GraphicsCaptureInterop.IsBorderlessSupported}");
    }

    private void InitializeCapture()
    {
        // Create capture item for WoW window
        captureItem = GraphicsCaptureInterop.CreateCaptureItemForWindow(process.MainWindowHandle)
            ?? throw new InvalidOperationException(
                $"Failed to create GraphicsCaptureItem for window handle {process.MainWindowHandle}");

        // Subscribe to size changes
        captureItem.Closed += OnCaptureItemClosed;

        // Create frame pool with room for 2 frames
        framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            winrtDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            captureItem.Size);

        framePool.FrameArrived += OnFrameArrived;

        // Create capture session
        captureSession = framePool.CreateCaptureSession(captureItem);

        // Try to disable yellow border on Windows 10 20348+ using reflection
        // (properties not available in SDK 19041, but may be present at runtime)
        TrySetBorderlessCapture(captureSession);

        captureSession.StartCapture();
    }

    private void OnCaptureItemClosed(GraphicsCaptureItem sender, object args)
    {
        logger.LogWarning("Capture item closed - WoW window may have been closed");
        StopCapture();
    }

    private int frameCount;
    private int successfulFrameCount;

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        frameCount++;

        using Direct3D11CaptureFrame? frame = sender.TryGetNextFrame();
        if (frame == null)
        {
            logger.LogWarning("OnFrameArrived: TryGetNextFrame returned null (frame #{FrameCount})", frameCount);
            return;
        }

        // Get the surface and convert to D3D11 texture
        IDirect3DSurface surface = frame.Surface;
        IntPtr dxgiSurfacePtr = GraphicsCaptureInterop.GetDXGISurface(surface, out int hresult);

        if (dxgiSurfacePtr == IntPtr.Zero)
        {
            logger.LogWarning("OnFrameArrived: GetDXGISurface returned Zero, HRESULT=0x{Hr:X8} (frame #{FrameCount})",
                hresult, frameCount);
            return;
        }

        try
        {
            IDXGISurface dxgiSurface = new(dxgiSurfacePtr);
            ID3D11Texture2D frameTexture = dxgiSurface.QueryInterface<ID3D11Texture2D>();

            using (frameLock.EnterScope())
            {
                SizeInt32 contentSize = frame.ContentSize;

                // Recreate staging textures only when frame size changes
                if (writeStagingTexture == null ||
                    stagingTextureSize.Width != contentSize.Width ||
                    stagingTextureSize.Height != contentSize.Height)
                {
                    writeStagingTexture?.Dispose();
                    readStagingTexture?.Dispose();

                    Texture2DDescription desc = frameTexture.Description;
                    desc.Usage = ResourceUsage.Staging;
                    desc.BindFlags = BindFlags.None;
                    desc.CPUAccessFlags = CpuAccessFlags.Read;
                    desc.MiscFlags = ResourceOptionFlags.None;

                    writeStagingTexture = device.CreateTexture2D(desc);
                    readStagingTexture = device.CreateTexture2D(desc);
                    stagingTextureSize = contentSize;
                }

                deviceContext.CopyResource(writeStagingTexture, frameTexture);

                // Swap buffers: write becomes read, read becomes write
                (writeStagingTexture, readStagingTexture) = (readStagingTexture, writeStagingTexture);

                latestFrameSize = contentSize;
                hasNewFrame = true;
                successfulFrameCount++;
            }

            if (successfulFrameCount == 1)
            {
                logger.LogInformation("OnFrameArrived: First successful frame captured! Size: {Width}x{Height}, SurfacePtr: 0x{Ptr:X}",
                    latestFrameSize.Width, latestFrameSize.Height, dxgiSurfacePtr);
            }

            frameTexture.Dispose();
            dxgiSurface.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OnFrameArrived: Error processing captured frame #{FrameCount}", frameCount);
        }
    }

    /// <summary>
    /// Attempts to set borderless capture properties using reflection.
    /// These properties are only available on Windows 10 build 20348+ but may not be
    /// present in the SDK we're targeting (19041). Using reflection allows the code
    /// to compile against 19041 while still utilizing newer features at runtime.
    /// </summary>
    private void TrySetBorderlessCapture(GraphicsCaptureSession session)
    {
        if (!GraphicsCaptureInterop.IsBorderlessSupported)
            return;

        try
        {
            s_borderRequiredProp?.SetValue(session, false);
            s_cursorEnabledProp?.SetValue(session, false);

            logger.LogDebug("Borderless capture enabled via reflection");
        }
        catch (Exception ex)
        {
            // Properties not available on this Windows version - yellow border will show
            logger.LogDebug(ex, "Could not enable borderless capture - yellow border may appear");
        }
    }

    public void Dispose()
    {
        StopCapture();

        writeStagingTexture?.Dispose();
        readStagingTexture?.Dispose();
        minimapTexture?.Dispose();
        addonTexture?.Dispose();
        screenTexture?.Dispose();
        deviceContext?.Dispose();
        device?.Dispose();
    }

    private void StopCapture()
    {
        try { captureSession?.Dispose(); } catch { }
        captureSession = null;

        try { framePool?.Dispose(); } catch { }
        framePool = null;

        if (captureItem != null)
        {
            captureItem.Closed -= OnCaptureItemClosed;
            captureItem = null;
        }
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

        logger.LogDebug($"DataFrames {frames.Length} - Texture: {addonSize}");
    }

    [SkipLocalsInit]
    public void Update()
    {
        // Get latest window rect
        GetRectangle(out Rectangle newRect);

        // Handle window resize
        if (newRect.Width != screenRect.Width || newRect.Height != screenRect.Height)
        {
            screenRect = newRect;
            RecreateFramePool();
        }

        ID3D11Texture2D? frameToProcess;
        SizeInt32 frameSize;

        using (frameLock.EnterScope())
        {
            if (!hasNewFrame || readStagingTexture == null)
                return;

            frameToProcess = readStagingTexture;
            frameSize = latestFrameSize;
            hasNewFrame = false;
        }

#if SAVE_RAW_FRAME
        SaveRawFrame(frameToProcess, frameSize);
#endif

        if (frames.Length > 2)
            UpdateAddonImage(frameToProcess);

        if (Enabled)
            UpdateScreenImage(frameToProcess, frameSize);

        if (MinimapEnabled)
            UpdateMinimapImage(frameToProcess, frameSize);
    }

#if SAVE_RAW_FRAME
    private bool rawFrameSaved;
    private void SaveRawFrame(ID3D11Texture2D sourceTexture, SizeInt32 frameSize)
    {
        if (rawFrameSaved)
            return;

        try
        {
            // Create a staging texture for the full frame
            Texture2DDescription desc = new()
            {
                CPUAccessFlags = CpuAccessFlags.Read,
                BindFlags = BindFlags.None,
                Format = Format.B8G8R8A8_UNorm,
                Width = (uint)frameSize.Width,
                Height = (uint)frameSize.Height,
                MiscFlags = ResourceOptionFlags.None,
                MipLevels = 1,
                ArraySize = 1,
                SampleDescription = { Count = 1, Quality = 0 },
                Usage = ResourceUsage.Staging
            };

            using ID3D11Texture2D stagingTexture = device.CreateTexture2D(desc);
            deviceContext.CopyResource(stagingTexture, sourceTexture);

            MappedSubresource resource = deviceContext.Map(stagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);

            using Image<Bgra32> rawImage = new(frameSize.Width, frameSize.Height);
            if (rawImage.DangerousTryGetSinglePixelMemory(out Memory<Bgra32> memory))
            {
                int rowPitch = (int)resource.RowPitch;
                ReadOnlySpan<byte> src = resource.AsSpan(frameSize.Height * rowPitch);
                Span<byte> dest = MemoryMarshal.Cast<Bgra32, byte>(memory.Span);

                int bytesToCopy = frameSize.Width * Bgra32Size;
                for (int y = 0; y < frameSize.Height; y++)
                {
                    ReadOnlySpan<byte> srcRow = src.Slice(y * rowPitch, bytesToCopy);
                    Span<byte> destRow = dest.Slice(y * bytesToCopy, bytesToCopy);
                    srcRow.TryCopyTo(destRow);
                }

                rawImage.SaveAsJpeg("raw_frame_wgc.jpg");
                logger.LogInformation("Saved raw frame: {Width}x{Height}", frameSize.Width, frameSize.Height);
            }

            deviceContext.Unmap(stagingTexture, 0);
            rawFrameSaved = true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save raw frame");
        }
    }
#endif

    private void RecreateFramePool()
    {
        if (captureItem == null || framePool == null)
            return;

        try
        {
            SizeInt32 size = new()
            {
                Width = screenRect.Width,
                Height = screenRect.Height
            };

            framePool.Recreate(
                winrtDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                size);

            // Recreate screen texture with new size
            screenTexture?.Dispose();
            Texture2DDescription screenTextureDesc = new()
            {
                CPUAccessFlags = CpuAccessFlags.Read,
                BindFlags = BindFlags.None,
                Format = Format.B8G8R8A8_UNorm,
                Width = (uint)screenRect.Width,
                Height = (uint)screenRect.Height,
                MiscFlags = ResourceOptionFlags.None,
                MipLevels = 1,
                ArraySize = 1,
                SampleDescription = { Count = 1, Quality = 0 },
                Usage = ResourceUsage.Staging
            };
            screenTexture = device.CreateTexture2D(screenTextureDesc);

            logger.LogDebug("Frame pool recreated for size: {Width}x{Height}", screenRect.Width, screenRect.Height);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to recreate frame pool");
        }
    }

    [SkipLocalsInit]
    private void UpdateAddonImage(ID3D11Texture2D sourceTexture)
    {
        if (!addonImage.DangerousTryGetSinglePixelMemory(out Memory<Bgra32> memory))
            return;

        // WGC captures full window including title bar/borders, offset to client area
        Vortice.Mathematics.Box areaOnWindow = new(
            clientOffset.X, clientOffset.Y, 0,
            clientOffset.X + addonSize.Width, clientOffset.Y + addonSize.Height, 1);

        deviceContext.CopySubresourceRegion(addonTexture!, 0, 0, 0, 0, sourceTexture, 0, areaOnWindow);

        MappedSubresource resource = deviceContext.Map(addonTexture!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);

        int rowPitch = (int)resource.RowPitch;
        ReadOnlySpan<byte> src = resource.AsSpan(addonSize.Height * rowPitch);
        Span<byte> dest = MemoryMarshal.Cast<Bgra32, byte>(memory.Span);

        if (addonSize.Height == 1 && src.TryCopyTo(dest))
        {
            goto Cleanup;
        }

        int bytesToCopy = addonSize.Width * Bgra32Size;
        for (int y = 0; y < addonSize.Height; y++)
        {
            ReadOnlySpan<byte> srcRow = src.Slice(y * rowPitch, bytesToCopy);
            Span<byte> destRow = dest.Slice(y * bytesToCopy, bytesToCopy);
            srcRow.TryCopyTo(destRow);
        }

#if SAVE_ADDON_IMAGE
        addonImage.SaveAsJpeg("addon_wgc.jpg");
#endif

    Cleanup:
        deviceContext.Unmap(addonTexture!, 0);
    }

    [SkipLocalsInit]
    private void UpdateScreenImage(ID3D11Texture2D sourceTexture, SizeInt32 frameSize)
    {
        if (!ScreenImage.DangerousTryGetSinglePixelMemory(out Memory<Bgra32> memory))
            return;

        // Ensure screen texture exists and is correct size for client area
        if (screenTexture == null ||
            screenTexture.Description.Width != (uint)screenRect.Width ||
            screenTexture.Description.Height != (uint)screenRect.Height)
        {
            screenTexture?.Dispose();
            Texture2DDescription screenTextureDesc = new()
            {
                CPUAccessFlags = CpuAccessFlags.Read,
                BindFlags = BindFlags.None,
                Format = Format.B8G8R8A8_UNorm,
                Width = (uint)screenRect.Width,
                Height = (uint)screenRect.Height,
                MiscFlags = ResourceOptionFlags.None,
                MipLevels = 1,
                ArraySize = 1,
                SampleDescription = { Count = 1, Quality = 0 },
                Usage = ResourceUsage.Staging
            };
            screenTexture = device.CreateTexture2D(screenTextureDesc);
        }

        // Copy client area (offset past title bar/borders)
        Vortice.Mathematics.Box clientArea = new(
            clientOffset.X, clientOffset.Y, 0,
            clientOffset.X + screenRect.Width, clientOffset.Y + screenRect.Height, 1);

        deviceContext.CopySubresourceRegion(screenTexture, 0, 0, 0, 0, sourceTexture, 0, clientArea);

        MappedSubresource resource = deviceContext.Map(screenTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);

        int rowPitch = (int)resource.RowPitch;
        ReadOnlySpan<byte> src = resource.AsSpan(screenRect.Height * rowPitch);
        Span<byte> dest = MemoryMarshal.Cast<Bgra32, byte>(memory.Span);

        int bytesToCopy = screenRect.Width * Bgra32Size;
        for (int y = 0; y < screenRect.Height; y++)
        {
            ReadOnlySpan<byte> srcRow = src.Slice(y * rowPitch, bytesToCopy);
            Span<byte> destRow = dest.Slice(y * bytesToCopy, bytesToCopy);
            srcRow.TryCopyTo(destRow);
        }

#if SAVE_SCREEN_IMAGE
        ScreenImage.SaveAsJpeg("screen_wgc.jpg");
#endif

        deviceContext.Unmap(screenTexture, 0);
    }

    [SkipLocalsInit]
    private void UpdateMinimapImage(ID3D11Texture2D sourceTexture, SizeInt32 frameSize)
    {
        if (!MiniMapImage.DangerousTryGetSinglePixelMemory(out Memory<Bgra32> memory))
            return;

        // Ensure minimap texture exists
        if (minimapTexture == null)
        {
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
        }

        // Minimap is at top-right of client area
        int minimapX = Math.Max(clientOffset.X, clientOffset.X + screenRect.Width - MiniMapSize);
        Vortice.Mathematics.Box areaOnWindow = new(
            minimapX, clientOffset.Y, 0,
            minimapX + MiniMapSize, clientOffset.Y + MiniMapRect.Bottom, 1);

        deviceContext.CopySubresourceRegion(minimapTexture, 0, 0, 0, 0, sourceTexture, 0, areaOnWindow);

        MappedSubresource resource = deviceContext.Map(minimapTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);

        int rowPitch = (int)resource.RowPitch;
        ReadOnlySpan<byte> src = resource.AsSpan(MiniMapRect.Height * rowPitch);
        Span<byte> dest = MemoryMarshal.Cast<Bgra32, byte>(memory.Span);

        int bytesToCopy = MiniMapRect.Width * Bgra32Size;
        for (int y = 0; y < MiniMapRect.Height; y++)
        {
            ReadOnlySpan<byte> srcRow = src.Slice(y * rowPitch, bytesToCopy);
            Span<byte> destRow = dest.Slice(y * bytesToCopy, bytesToCopy);
            srcRow.TryCopyTo(destRow);
        }

        deviceContext.Unmap(minimapTexture, 0);
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
}
