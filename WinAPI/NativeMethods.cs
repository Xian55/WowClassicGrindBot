using SixLabors.ImageSharp;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: DisableRuntimeMarshalling]

namespace WinAPI;

public static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    public struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public nint hCursor;
        public Point ptScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct RECT
    {
        public readonly int left, top, right, bottom;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorInfo(ref CURSORINFO pci);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DrawIconEx(nint hdc, int xLeft, int yTop, nint hIcon, int cxWidth, int cyHeight, int istepIfAniCur, nint hbrFlickerFreeDraw, int diFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DrawIcon(nint hDC, int x, int y, nint hIcon);

    public const int CURSOR_SHOWING = 0x0001;
    public const int DI_NORMAL = 0x0003;

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageA")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint hWnd, uint Msg, int wParam, int lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out Point p);

    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_LBUTTONDOWN = 0x201;
    public const uint WM_LBUTTONUP = 0x202;
    public const uint WM_RBUTTONDOWN = 0x204;
    public const uint WM_RBUTTONUP = 0x205;

    public const int VK_LBUTTON = 0x01;
    public const int VK_RBUTTON = 0x02;

    public static int MakeLParam(int x, int y) => (y << 16) | (x & 0xFFFF);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint hWnd, out RECT lpRect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(nint hWnd, ref Point lpPoint);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ScreenToClient(nint hWnd, ref Point lpPoint);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int nIndexn);

    private const int SM_CXCURSOR = 13;
    private const int SM_CYCURSOR = 14;

    [LibraryImport("gdi32.dll")]
    private static partial int GetDeviceCaps(nint hDC, int nIndex);

    private const int LOGPIXELSX = 88;

    public static bool IsWindowedMode(Point point)
    {
        return point.X != 0 || point.Y != 0;
    }

    public static void GetPosition(nint hWnd, ref Point point)
    {
        ClientToScreen(hWnd, ref point);
    }

    public static void GetWindowRect(nint hWnd, out Rectangle rect)
    {
        GetClientRect(hWnd, out RECT nRect);
        rect = Rectangle.FromLTRB(nRect.left, nRect.top, nRect.right, nRect.bottom);

        Point topLeft = new();
        ClientToScreen(hWnd, ref topLeft);
        if (IsWindowedMode(topLeft))
        {
            rect.X = topLeft.X;
            rect.Y = topLeft.Y;
        }
    }

    public static int GetDpi()
    {
        using System.Drawing.Graphics g = System.Drawing.Graphics.FromHwnd(nint.Zero);
        return GetDeviceCaps(g.GetHdc(), LOGPIXELSX);
    }

    public static Size GetCursorSize()
    {
        int dpi = GetDpi();
        SizeF size = new(GetSystemMetrics(SM_CXCURSOR), GetSystemMetrics(SM_CYCURSOR));
        size *= DPI2PPI(dpi);
        return (Size)size;
    }

    public static float DPI2PPI(int dpi)
    {
        return dpi / 96f;
    }

    public const int MONITOR_DEFAULT_TO_NULL = 0;
    public const int MONITOR_DEFAULT_TO_PRIMARY = 1;
    public const int MONITOR_DEFAULT_TO_NEAREST = 2;

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromWindow(nint hWnd, uint dwFlags);
}