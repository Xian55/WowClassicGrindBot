//#define SAVE_ADDON_IMAGE
//#define SAVE_SCREEN_IMAGE
//#define SAVE_RAW_FRAME
//#define SAVE_MINIMAP_IMAGE

using Game;

using Microsoft.Extensions.Logging;

using SharpGen.Runtime;

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
using Vortice.Mathematics;

using SharedLib;

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
public sealed class WowScreenWGC : IWowScreen, IAddonDataProvider, IGpuTextureProvider
{
    private readonly ILogger<WowScreenWGC> logger;
    private readonly WowProcess process;
    private const int Bgra32Size = ScreenCaptureHelper.Bgra32Size;

    public event Action? OnChanged;

    public bool Enabled { get => true; set { } }
    public bool EnablePostProcess { get => true; set { } }
    public bool MinimapEnabled { get; set; }

    public Rectangle ScreenRect => screenRect;
    private Rectangle screenRect;

    public Image<Bgra32> ScreenImage { get; init; }

    private readonly SixLabors.ImageSharp.Configuration ContiguousJpegConfiguration
        = new(new JpegConfigurationModule()) { PreferContiguousImageBuffers = true };

    public const int MiniMapSize = 200;
    public Rectangle MiniMapRect { get; private set; }
    public Image<Bgra32> MiniMapImage { get; init; }

    public MinimapSettings MinimapSettings =>
        Data.Length > 2
        ? new(Data[16], Data[17])
        : new(9013, 220016); //debug only

    // D3D11 resources
    private static readonly FeatureLevel[] s_featureLevels =
    [
        FeatureLevel.Level_12_1,
        FeatureLevel.Level_12_0,
        FeatureLevel.Level_11_0,
    ];

    // Cached reflection for borderless capture (IsBorderRequired not in SDK 19041)
    private static readonly PropertyInfo? s_borderRequiredProp = typeof(GraphicsCaptureSession)
        .GetProperty("IsBorderRequired", BindingFlags.Public | BindingFlags.Instance);

    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext deviceContext;

    // WGC resources
    private readonly IDirect3DDevice winrtDevice;
    private GraphicsCaptureItem? captureItem;
    private Direct3D11CaptureFramePool? framePool;
    private GraphicsCaptureSession? captureSession;

    private bool deviceRemoved;

    // Double-buffer: WGC writes async, Update() reads
    private readonly Lock frameLock = new();
    private ID3D11Texture2D? writeStagingTexture;
    private ID3D11Texture2D? readStagingTexture;
    private SizeInt32 stagingTextureSize;
    private SizeInt32 latestFrameSize;
    private bool hasNewFrame;
    private bool processingFrame;

    // IGpuTextureProvider -- Default-usage copy for GPU compute shader
    private ID3D11Texture2D? gpuTextureCopy;
    private SizeInt32 gpuTextureSize;

    ID3D11Device IGpuTextureProvider.Device => device;
    ID3D11DeviceContext IGpuTextureProvider.DeviceContext => deviceContext;

    ID3D11Texture2D? IGpuTextureProvider.GetCapturedTexture()
    {
        using (frameLock.EnterScope())
        {
            return gpuTextureCopy;
        }
    }

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

        using (ID3D11Multithread multithread = device.QueryInterface<ID3D11Multithread>())
        {
            multithread.SetMultithreadProtected(true);
        }

        // Create WinRT device for WGC
        winrtDevice = GraphicsCaptureInterop.CreateDirect3DDeviceFromD3D11(device)
            ?? throw new InvalidOperationException("Failed to create WinRT Direct3D device");

        InitFrames(frames);
        InitializeCapture();

        logger.LogInformation(
            "WGC initialized - {ScreenRect} - ClientOffset: ({OffsetX}, {OffsetY}) - Borderless: {Borderless}",
            screenRect, clientOffset.X, clientOffset.Y, GraphicsCaptureInterop.IsBorderlessSupported);
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

        TryConfigureCaptureSession(captureSession);

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
            using IDXGISurface dxgiSurface = new(dxgiSurfacePtr);
            using ID3D11Texture2D frameTexture = dxgiSurface.QueryInterface<ID3D11Texture2D>();

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

                // Copy client area only to GPU texture for compute shader
                // (WGC captures full window including title bar; match CPU path)
                // Clamp to frame content bounds to avoid out-of-bounds copy
                // during window resize or DPI changes
                int gpuCopyWidth = Math.Min(screenRect.Width, contentSize.Width - clientOffset.X);
                int gpuCopyHeight = Math.Min(screenRect.Height, contentSize.Height - clientOffset.Y);

                if (gpuCopyWidth > 0 && gpuCopyHeight > 0)
                {
                    if (gpuTextureCopy == null ||
                        gpuTextureSize.Width != gpuCopyWidth ||
                        gpuTextureSize.Height != gpuCopyHeight)
                    {
                        Texture2DDescription gpuDesc = frameTexture.Description;
                        gpuDesc.Width = (uint)gpuCopyWidth;
                        gpuDesc.Height = (uint)gpuCopyHeight;
                        gpuDesc.Usage = ResourceUsage.Default;
                        gpuDesc.BindFlags = BindFlags.ShaderResource;
                        gpuDesc.CPUAccessFlags = CpuAccessFlags.None;
                        gpuDesc.MiscFlags = ResourceOptionFlags.None;

                        ID3D11Texture2D? oldGpuTexture = gpuTextureCopy;
                        gpuTextureCopy = device.CreateTexture2D(gpuDesc);
                        gpuTextureSize = new SizeInt32
                        {
                            Width = gpuCopyWidth,
                            Height = gpuCopyHeight
                        };
                        oldGpuTexture?.Dispose();
                    }

                    Box clientBox = new(
                        clientOffset.X, clientOffset.Y, 0,
                        clientOffset.X + gpuCopyWidth,
                        clientOffset.Y + gpuCopyHeight, 1);
                    deviceContext.CopySubresourceRegion(
                        gpuTextureCopy, 0, 0, 0, 0,
                        frameTexture, 0, clientBox);
                }

                // Only swap if Update() isn't actively reading from readStagingTexture.
                // If processingFrame is true, we just overwrote writeStagingTexture in place
                // and the next Update() call will pick up the latest frame after swap.
                if (!processingFrame)
                {
                    (writeStagingTexture, readStagingTexture) = (readStagingTexture, writeStagingTexture);
                }

                latestFrameSize = contentSize;
                hasNewFrame = true;
                successfulFrameCount++;
            }

            if (successfulFrameCount == 1)
            {
                logger.LogInformation("OnFrameArrived: First successful frame captured! Size: {Width}x{Height}, SurfacePtr: 0x{Ptr:X}",
                    latestFrameSize.Width, latestFrameSize.Height, dxgiSurfacePtr);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OnFrameArrived: Error processing captured frame #{FrameCount}", frameCount);
        }
    }

    /// <summary>
    /// Configures capture session: disables cursor capture (available since SDK 19041)
    /// and attempts borderless capture via reflection (build 20348+).
    /// </summary>
    private void TryConfigureCaptureSession(GraphicsCaptureSession session)
    {
        session.IsCursorCaptureEnabled = false;

        if (!GraphicsCaptureInterop.IsBorderlessSupported)
            return;

        try
        {
            s_borderRequiredProp?.SetValue(session, false);
            logger.LogDebug("Borderless capture enabled via reflection");
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not enable borderless capture - yellow border may appear");
        }
    }

    public void Dispose()
    {
        StopCapture();

        winrtDevice?.Dispose();
        gpuTextureCopy?.Dispose();
        writeStagingTexture?.Dispose();
        readStagingTexture?.Dispose();
        deviceContext?.Dispose();
        device?.Dispose();
    }

    private void StopCapture()
    {
        try { captureSession?.Dispose(); }
        catch (Exception ex) { logger.LogDebug(ex, "Error disposing capture session"); }
        captureSession = null;

        try { framePool?.Dispose(); }
        catch (Exception ex) { logger.LogDebug(ex, "Error disposing frame pool"); }
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

        logger.LogDebug("DataFrames {FrameCount} - Addon: {AddonSize}", frames.Length, addonSize);
    }

    [SkipLocalsInit]
    public void Update()
    {
        if (deviceRemoved)
            return;

        // Get latest window rect
        GetRectangle(out Rectangle newRect);

        // Handle window resize
        if (newRect.Width != screenRect.Width || newRect.Height != screenRect.Height)
        {
            screenRect = newRect;
            clientOffset = NativeMethods.GetClientAreaOffset(process.MainWindowHandle);
            RecreateFramePool();
        }

        ID3D11Texture2D? frameToProcess;
        SizeInt32 frameSize;

        using (frameLock.EnterScope())
        {
            if (!hasNewFrame || readStagingTexture == null)
                return;

            processingFrame = true;
            frameToProcess = readStagingTexture;
            frameSize = latestFrameSize;
            hasNewFrame = false;
        }

        try
        {
            MappedSubresource resource = deviceContext.Map(frameToProcess, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                int rowPitch = (int)resource.RowPitch;
                ReadOnlySpan<byte> fullFrame = resource.AsSpan(frameSize.Height * rowPitch);

#if SAVE_RAW_FRAME
                SaveRawFrame(fullFrame, rowPitch, frameSize);
#endif

                if (frames.Length > 2)
                    UpdateAddonImage(fullFrame, rowPitch, frameSize);

                if (Enabled)
                    UpdateScreenImage(fullFrame, rowPitch, frameSize);

                if (MinimapEnabled)
                    UpdateMinimapImage(fullFrame, rowPitch, frameSize);
            }
            finally
            {
                deviceContext.Unmap(frameToProcess, 0);
            }
        }
        catch (SharpGenException) when (CheckDeviceRemoved())
        {
            // Device lost — silently stop capturing
        }
        finally
        {
            using (frameLock.EnterScope())
            {
                processingFrame = false;
            }
        }
    }

#if SAVE_RAW_FRAME
    private bool rawFrameSaved;
    private void SaveRawFrame(ReadOnlySpan<byte> fullFrame, int rowPitch, SizeInt32 frameSize)
    {
        if (rawFrameSaved)
            return;

        try
        {
            using Image<Bgra32> rawImage = new(frameSize.Width, frameSize.Height);
            if (rawImage.DangerousTryGetSinglePixelMemory(out Memory<Bgra32> memory))
            {
                Span<byte> dest = MemoryMarshal.Cast<Bgra32, byte>(memory.Span);
                ScreenCaptureHelper.CopyRegion(fullFrame, rowPitch, 0, 0, dest, frameSize.Width, frameSize.Height);

                rawImage.SaveAsJpeg("raw_frame_wgc.jpg");
                logger.LogInformation("Saved raw frame: {Width}x{Height}", frameSize.Width, frameSize.Height);
            }

            rawFrameSaved = true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save raw frame");
        }
    }
#endif

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

            logger.LogDebug("Frame pool recreated for size: {Width}x{Height}", screenRect.Width, screenRect.Height);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to recreate frame pool");
        }
    }

    [SkipLocalsInit]
    private void UpdateAddonImage(ReadOnlySpan<byte> fullFrame, int rowPitch, SizeInt32 frameSize)
    {
        if (!addonImage.DangerousTryGetSinglePixelMemory(out Memory<Bgra32> memory))
            return;

        // WGC captures full window including title bar/borders, offset to client area
        if (!RegionFitsInFrame(clientOffset.X, clientOffset.Y, addonSize.Width, addonSize.Height, frameSize))
            return;

        Span<byte> dest = MemoryMarshal.Cast<Bgra32, byte>(memory.Span);
        ScreenCaptureHelper.CopyRegion(fullFrame, rowPitch, clientOffset.X, clientOffset.Y, dest, addonSize.Width, addonSize.Height);

#if SAVE_ADDON_IMAGE
        addonImage.SaveAsJpeg("addon_wgc.jpg");
#endif
    }

    [SkipLocalsInit]
    private void UpdateScreenImage(ReadOnlySpan<byte> fullFrame, int rowPitch, SizeInt32 frameSize)
    {
        if (!ScreenImage.DangerousTryGetSinglePixelMemory(out Memory<Bgra32> memory))
            return;

        // Copy client area (offset past title bar/borders)
        if (!RegionFitsInFrame(clientOffset.X, clientOffset.Y, screenRect.Width, screenRect.Height, frameSize))
            return;

        Span<byte> dest = MemoryMarshal.Cast<Bgra32, byte>(memory.Span);
        ScreenCaptureHelper.CopyRegion(fullFrame, rowPitch, clientOffset.X, clientOffset.Y, dest, screenRect.Width, screenRect.Height);

#if SAVE_SCREEN_IMAGE
        ScreenImage.SaveAsJpeg("screen_wgc.jpg");
#endif
    }

    [SkipLocalsInit]
    private void UpdateMinimapImage(ReadOnlySpan<byte> fullFrame, int rowPitch, SizeInt32 frameSize)
    {
        if (!MiniMapImage.DangerousTryGetSinglePixelMemory(out Memory<Bgra32> memory))
            return;

        // Minimap is at top-right of client area
        int minimapX = clientOffset.X + screenRect.Width - MiniMapSize;
        int minimapY = clientOffset.Y;

        if (!RegionFitsInFrame(minimapX, minimapY, MiniMapRect.Width, MiniMapRect.Height, frameSize))
            return;

        Span<byte> dest = MemoryMarshal.Cast<Bgra32, byte>(memory.Span);
        ScreenCaptureHelper.CopyRegion(fullFrame, rowPitch, minimapX, minimapY, dest, MiniMapRect.Width, MiniMapRect.Height);

#if SAVE_MINIMAP_IMAGE
        MiniMapImage.SaveAsJpeg("minimap_wgc.jpg");
#endif
    }

    private static bool RegionFitsInFrame(
        int srcX, int srcY, int width, int height, SizeInt32 frameSize)
        => srcX >= 0 && srcY >= 0
        && srcX + width <= frameSize.Width
        && srcY + height <= frameSize.Height;

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
