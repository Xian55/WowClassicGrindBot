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

    public const uint WM_CHAR = 0x0102;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_LBUTTONDOWN = 0x201;
    public const uint WM_LBUTTONUP = 0x202;
    public const uint WM_RBUTTONDOWN = 0x204;
    public const uint WM_RBUTTONUP = 0x205;

    public const int VK_LBUTTON = 0x01;
    public const int VK_RBUTTON = 0x02;

    public static int MakeLParam(int x, int y) => (y << 16) | (x & 0xFFFF);

    // MapVirtualKey translation types
    public const uint MAPVK_VK_TO_VSC = 0;
    public const uint MAPVK_VSC_TO_VK = 1;
    public const uint MAPVK_VK_TO_CHAR = 2;
    public const uint MAPVK_VSC_TO_VK_EX = 3;
    public const uint MAPVK_VK_TO_VSC_EX = 4;

    [LibraryImport("user32.dll")]
    public static partial uint MapVirtualKeyA(uint uCode, uint uMapType);

    [LibraryImport("user32.dll")]
    public static partial nint GetKeyboardLayout(uint idThread);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    /// <summary>
    /// Translates a character to a virtual-key code and shift state for the specified keyboard layout.
    /// Returns: Low byte = virtual key code, High byte = shift state (1=Shift, 2=Ctrl, 4=Alt)
    /// Returns -1 if the character cannot be translated.
    /// </summary>
    [LibraryImport("user32.dll")]
    public static partial short VkKeyScanExW(char ch, nint dwhkl);

    /// <summary>
    /// Gets the virtual key code and required modifiers for a character using the keyboard layout of the specified window.
    /// </summary>
    /// <param name="character">The character to look up</param>
    /// <param name="windowHandle">The target window handle</param>
    /// <param name="virtualKey">Output: the virtual key code</param>
    /// <param name="needsShift">Output: true if Shift modifier is required</param>
    /// <param name="needsCtrl">Output: true if Ctrl modifier is required</param>
    /// <param name="needsAlt">Output: true if Alt modifier is required</param>
    /// <returns>True if the character was found on this keyboard layout</returns>
    public static bool GetVirtualKeyForCharacter(char character, nint windowHandle,
        out int virtualKey, out bool needsShift, out bool needsCtrl, out bool needsAlt)
    {
        virtualKey = 0;
        needsShift = false;
        needsCtrl = false;
        needsAlt = false;

        // Get the keyboard layout for the target window's thread
        uint threadId = GetWindowThreadProcessId(windowHandle, out _);
        nint keyboardLayout = GetKeyboardLayout(threadId);

        // Translate character to virtual key code
        short result = VkKeyScanExW(character, keyboardLayout);

        if (result == -1)
            return false; // Character not found on this keyboard layout

        // Low byte is the virtual key code
        virtualKey = result & 0xFF;

        // High byte is the shift state
        int shiftState = (result >> 8) & 0xFF;
        needsShift = (shiftState & 1) != 0;
        needsCtrl = (shiftState & 2) != 0;
        needsAlt = (shiftState & 4) != 0;

        return true;
    }

    /// <summary>
    /// Checks if a virtual key code is a layout-dependent OEM key that might need remapping.
    /// </summary>
    public static bool IsLayoutDependentKey(int virtualKey)
    {
        return virtualKey switch
        {
            // OEM keys that vary by keyboard layout
            0xBA => true,  // OEM_1 (;:)
            0xBB => true,  // OEM_PLUS (=+)
            0xBC => true,  // OEM_COMMA (,<)
            0xBD => true,  // OEM_MINUS (-_)
            0xBE => true,  // OEM_PERIOD (.>)
            0xBF => true,  // OEM_2 (/?)
            0xC0 => true,  // OEM_3 (`~)
            0xDB => true,  // OEM_4 ([{)
            0xDC => true,  // OEM_5 (\|)
            0xDD => true,  // OEM_6 (]})
            0xDE => true,  // OEM_7 ('")
            _ => false
        };
    }

    /// <summary>
    /// Maps US keyboard virtual key codes to their character for layout translation.
    /// </summary>
    public static char? GetCharacterForUSKey(int virtualKey)
    {
        return virtualKey switch
        {
            0xBA => ';',   // OEM_1
            0xBB => '=',   // OEM_PLUS
            0xBC => ',',   // OEM_COMMA
            0xBD => '-',   // OEM_MINUS
            0xBE => '.',   // OEM_PERIOD
            0xBF => '/',   // OEM_2
            0xC0 => '`',   // OEM_3
            0xDB => '[',   // OEM_4
            0xDC => '\\',  // OEM_5
            0xDD => ']',   // OEM_6
            0xDE => '\'',  // OEM_7
            _ => null
        };
    }

    /// <summary>
    /// Builds the lParam for WM_KEYDOWN message.
    /// </summary>
    /// <param name="virtualKey">Virtual key code</param>
    /// <param name="extended">True if this is an extended key (arrow keys, navigation keys, etc.)</param>
    /// <param name="repeatCount">Repeat count (usually 1)</param>
    public static int MakeKeyDownLParam(int virtualKey, bool extended = false, int repeatCount = 1)
    {
        uint scanCode = GetScanCode(virtualKey);

        int lParam = repeatCount & 0xFFFF;                    // Bits 0-15: repeat count
        lParam |= (int)(scanCode << 16);                      // Bits 16-23: scan code
        if (extended)
            lParam |= (1 << 24);                              // Bit 24: extended key flag
        // Bit 29 (context): 0 for WM_KEYDOWN
        // Bit 30 (previous state): 0 for new press
        // Bit 31 (transition): 0 for WM_KEYDOWN

        return lParam;
    }

    /// <summary>
    /// Builds the lParam for WM_KEYUP message.
    /// </summary>
    public static int MakeKeyUpLParam(int virtualKey, bool extended = false)
    {
        uint scanCode = GetScanCode(virtualKey);

        int lParam = 1;                                       // Bits 0-15: repeat count = 1
        lParam |= (int)(scanCode << 16);                      // Bits 16-23: scan code
        if (extended)
            lParam |= (1 << 24);                              // Bit 24: extended key flag
        // Bit 29 (context): 0
        lParam |= (1 << 30);                                  // Bit 30 (previous state): 1 for key up
        lParam |= unchecked((int)(1u << 31));                 // Bit 31 (transition): 1 for WM_KEYUP

        return lParam;
    }

    /// <summary>
    /// Gets the scan code for a virtual key, with fallbacks for keys that MapVirtualKey doesn't handle.
    /// </summary>
    private static uint GetScanCode(int virtualKey)
    {
        uint scanCode = MapVirtualKeyA((uint)virtualKey, MAPVK_VK_TO_VSC);

        // If MapVirtualKey returned 0, use known fallbacks for common keys
        if (scanCode == 0)
        {
            scanCode = virtualKey switch
            {
                0xBB => 0x0D,  // VK_OEM_PLUS (=) -> scan code 13
                0xBD => 0x0C,  // VK_OEM_MINUS (-) -> scan code 12
                0xDB => 0x1A,  // VK_OEM_4 ([) -> scan code 26
                0xDD => 0x1B,  // VK_OEM_6 (]) -> scan code 27
                0xDC => 0x2B,  // VK_OEM_5 (\) -> scan code 43
                0xBA => 0x27,  // VK_OEM_1 (;) -> scan code 39
                0xDE => 0x28,  // VK_OEM_7 (') -> scan code 40
                0xBC => 0x33,  // VK_OEM_COMMA (,) -> scan code 51
                0xBE => 0x34,  // VK_OEM_PERIOD (.) -> scan code 52
                0xBF => 0x35,  // VK_OEM_2 (/) -> scan code 53
                0xC0 => 0x29,  // VK_OEM_3 (`) -> scan code 41
                _ => 0
            };
        }

        return scanCode;
    }

    /// <summary>
    /// Checks if a virtual key code is an extended key.
    /// Extended keys include: arrow keys, Insert, Delete, Home, End, Page Up/Down, Numpad keys.
    /// </summary>
    public static bool IsExtendedKey(int virtualKey)
    {
        return virtualKey switch
        {
            // Arrow keys
            0x25 or 0x26 or 0x27 or 0x28 => true, // Left, Up, Right, Down
            // Navigation cluster
            0x2D or 0x2E => true, // Insert, Delete
            0x24 or 0x23 => true, // Home, End
            0x21 or 0x22 => true, // Page Up, Page Down
            // Numpad special
            0x90 => true, // Num Lock
            // Right-side modifier keys (if sent separately)
            0xA1 or 0xA3 or 0xA5 => true, // RShift, RControl, RMenu
            _ => false
        };
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint hWnd, out RECT lpRect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(nint hWnd, ref Point lpPoint);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ScreenToClient(nint hWnd, ref Point lpPoint);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowRect", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRectNative(nint hWnd, out RECT lpRect);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

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

    /// <summary>
    /// Gets the offset from the WGC captured window top-left to the client area top-left.
    /// WGC captures the visible window frame (DWM extended frame bounds), not the full
    /// window rect which includes invisible drop shadows on Windows 10+.
    /// </summary>
    /// <param name="hWnd">The window handle.</param>
    /// <returns>Point containing (borderWidth, titleBarHeight + borderWidth).</returns>
    public static Point GetClientAreaOffset(nint hWnd)
    {
        // Get the visible window bounds (what WGC captures)
        // DWM extended frame bounds excludes the invisible drop shadow
        int hr = DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS,
            out RECT frameRect, Marshal.SizeOf<RECT>());

        // Fall back to GetWindowRect if DWM fails
        if (hr != 0)
            GetWindowRectNative(hWnd, out frameRect);

        // Get client area top-left in screen coordinates
        Point clientTopLeft = new();
        ClientToScreen(hWnd, ref clientTopLeft);

        // Calculate the offset from visible frame top-left to client top-left
        return new Point(
            clientTopLeft.X - frameRect.left,
            clientTopLeft.Y - frameRect.top);
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