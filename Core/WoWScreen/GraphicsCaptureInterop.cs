using System;
using System.Runtime.InteropServices;

using Vortice.Direct3D11;

using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;

using WinRT;

namespace Core;

/// <summary>
/// Provides interop helpers for Windows Graphics Capture API.
/// </summary>
public static class GraphicsCaptureInterop
{
    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComVisible(true)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(
            [In] IntPtr window,
            [In] ref Guid iid);

        IntPtr CreateForMonitor(
            [In] IntPtr monitor,
            [In] ref Guid iid);
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComVisible(true)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        // HRESULT GetInterface(REFIID iid, void** p)
        [PreserveSig]
        int GetInterface([In] ref Guid iid, out IntPtr p);
    }

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice",
        SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true,
        CallingConvention = CallingConvention.StdCall)]
    private static extern uint CreateDirect3D11DeviceFromDXGIDevice(
        IntPtr dxgiDevice, out IntPtr graphicsDevice);

    private static readonly Guid GraphicsCaptureItemGuid =
        new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    private static readonly Guid IDXGIDeviceGuid =
        new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    /// <summary>
    /// Creates a GraphicsCaptureItem for the specified window handle.
    /// </summary>
    /// <param name="hwnd">The window handle to capture.</param>
    /// <returns>A GraphicsCaptureItem for the window, or null if creation fails.</returns>
    public static GraphicsCaptureItem? CreateCaptureItemForWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return null;

        try
        {
            object factory = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
            var interop = (IGraphicsCaptureItemInterop)factory;

            Guid guid = GraphicsCaptureItemGuid;
            IntPtr itemPointer = interop.CreateForWindow(hwnd, ref guid);

            if (itemPointer == IntPtr.Zero)
                return null;

            return MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Creates a WinRT IDirect3DDevice from a Vortice D3D11 device.
    /// </summary>
    /// <param name="d3dDevice">The Vortice D3D11 device.</param>
    /// <returns>A WinRT Direct3D device for use with Graphics Capture, or null if creation fails.</returns>
    public static IDirect3DDevice? CreateDirect3DDeviceFromD3D11(ID3D11Device d3dDevice)
    {
        try
        {
            // Get the DXGI device from the D3D11 device
            using Vortice.DXGI.IDXGIDevice dxgiDevice = d3dDevice.QueryInterface<Vortice.DXGI.IDXGIDevice>();

            uint hr = CreateDirect3D11DeviceFromDXGIDevice(
                dxgiDevice.NativePointer,
                out IntPtr graphicsDevice);

            if (hr != 0 || graphicsDevice == IntPtr.Zero)
                return null;

            return MarshalInterface<IDirect3DDevice>.FromAbi(graphicsDevice);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the underlying DXGI surface from a WinRT Direct3D surface.
    /// </summary>
    /// <param name="surface">The WinRT Direct3D surface.</param>
    /// <returns>A pointer to the DXGI surface, or IntPtr.Zero if failed.</returns>
    public static IntPtr GetDXGISurface(IDirect3DSurface surface)
    {
        return GetDXGISurface(surface, out _);
    }

    /// <summary>
    /// Gets the underlying DXGI surface from a WinRT Direct3D surface.
    /// </summary>
    /// <param name="surface">The WinRT Direct3D surface.</param>
    /// <param name="hresult">The HRESULT from the COM call.</param>
    /// <returns>A pointer to the DXGI surface, or IntPtr.Zero if failed.</returns>
    public static IntPtr GetDXGISurface(IDirect3DSurface surface, out int hresult)
    {
        hresult = 0;
        try
        {
            object access = surface.As<IDirect3DDxgiInterfaceAccess>();
            var dxgiAccess = (IDirect3DDxgiInterfaceAccess)access;

            Guid dxgiSurfaceGuid = typeof(Vortice.DXGI.IDXGISurface).GUID;
            hresult = dxgiAccess.GetInterface(ref dxgiSurfaceGuid, out IntPtr surfacePtr);

            // S_OK = 0, anything else is failure
            return hresult >= 0 ? surfacePtr : IntPtr.Zero;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetDXGISurface exception: {ex.Message}");
            hresult = ex.HResult;
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Checks if Windows Graphics Capture is supported on this system.
    /// Requires Windows 10 version 1903 (build 18362) or later for basic support,
    /// and version 2004 (build 19041) for borderless capture (no yellow border).
    /// </summary>
    public static bool IsSupported => GraphicsCaptureSession.IsSupported();

    /// <summary>
    /// Checks if borderless capture is supported (Windows 10 20H2 build 20348+).
    /// When supported, the yellow capture border can be disabled.
    /// </summary>
    public static bool IsBorderlessSupported =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348);
}
