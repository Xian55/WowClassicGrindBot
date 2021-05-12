using PInvoke;
using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace WinAPI
{
    public static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct CURSORINFO
        {
            public Int32 cbSize;
            public Int32 flags;
            public IntPtr hCursor;
            public POINTAPI ptScreenPos;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINTAPI
        {
            public int x;
            public int y;
        }

        [DllImport("user32.dll")]
        public static extern bool GetCursorInfo(out CURSORINFO pci);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyHeight, int istepIfAniCur, IntPtr hbrFlickerFreeDraw, int diFlags);
        
        [DllImport("user32.dll")]
        public static extern bool DrawIcon(IntPtr hDC, int x, int y, IntPtr hIcon);

        public const Int32 CURSOR_SHOWING = 0x0001;
        public const Int32 DI_NORMAL = 0x0003;

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, UInt32 Msg, int wParam, int lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetCursorPos(int x, int y);

        public const UInt32 WM_KEYDOWN = 0x0100;
        public const UInt32 WM_KEYUP = 0x0101;
        public const UInt32 WM_LBUTTONDOWN = 0x201;
        public const UInt32 WM_LBUTTONUP = 0x202;
        public const UInt32 WM_RBUTTONDOWN = 0x204;
        public const UInt32 WM_RBUTTONUP = 0x205;
        public const int VK_RMB = 0x02;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, ref RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hWnd, ref RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpRect);

        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int nIndexn);

        private static int SM_CXCURSOR = 13;
        private static int SM_CYCURSOR = 14;

        [DllImport("gdi32.dll")]
        static extern int GetDeviceCaps(IntPtr hDC, int nIndex);

        private static int LOGPIXELSX = 88;

        private static bool IsWindowedMode(POINT point)
        {
            return point.x != 0 || point.y != 0;
        }

        private static void GetNativeClientRect(IntPtr hWnd, out Rectangle rect)
        {
            RECT nRect = new RECT();
            if (GetClientRect(hWnd, ref nRect))
            {
                rect = new Rectangle
                {
                    X = nRect.left,
                    Y = nRect.top,
                    Width = (nRect.right - nRect.left),
                    Height = (nRect.bottom - nRect.top)
                };
            }
            else
            {
                rect = Rectangle.Empty;
            }
        }

        public static void GetPosition(IntPtr hWnd, out Point point)
        {
            var p = new POINT();
            ClientToScreen(hWnd, ref p);
            point = new Point(p.x, p.y);
        }

        public static void GetWindowRect(IntPtr hWnd, out Rectangle rect)
        {
            GetNativeClientRect(hWnd, out rect);

            var topLeft = new POINT();
            ClientToScreen(hWnd, ref topLeft);
            if (IsWindowedMode(topLeft))
            {
                rect.X = topLeft.x;
                rect.Y = topLeft.y;
            }
        }

        private static int GetDpi()
        {
            Graphics g = Graphics.FromHwnd(IntPtr.Zero);
            IntPtr desktop = g.GetHdc();
            var dpi = GetDeviceCaps(desktop, LOGPIXELSX);
            return dpi;
        }

        public static Size GetCursorSize()
        {
            int dpi = GetDpi();
            var size = new SizeF(GetSystemMetrics(SM_CXCURSOR), GetSystemMetrics(SM_CYCURSOR));
            size *= DPI2PPI(dpi);
            return size.ToSize();
        }

        private static float DPI2PPI(int dpi)
        {
            return dpi / 96f;
        }
    }
}