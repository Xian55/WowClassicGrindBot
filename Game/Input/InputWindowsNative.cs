using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinAPI;

namespace Game
{
    public class InputWindowsNative : IInput
    {
        private readonly int MIN_DELAY;
        private readonly int MAX_DELAY;

        private readonly Process process;
        private readonly Random random = new Random();

        private readonly IEnumerable<ConsoleKey> consoleKeys = (IEnumerable<ConsoleKey>)Enum.GetValues(typeof(ConsoleKey));

        public InputWindowsNative(Process process, int minDelay, int maxDelay)
        {
            this.process = process;

            MIN_DELAY = minDelay;
            MAX_DELAY = maxDelay;
        }

        private async Task Delay(int milliseconds)
        {
            await Task.Delay(milliseconds + random.Next(1, MAX_DELAY));
        }

        public void KeyDown(int key)
        {
            NativeMethods.PostMessage(process.MainWindowHandle, NativeMethods.WM_KEYDOWN, (int)key, 0);
        }

        public void KeyUp(int key)
        {
            NativeMethods.PostMessage(process.MainWindowHandle, NativeMethods.WM_KEYUP, (int)key, 0);
        }

        public async Task KeyPress(int key, int milliseconds)
        {
            NativeMethods.PostMessage(process.MainWindowHandle, NativeMethods.WM_KEYDOWN, (int)key, 0);
            await Delay(milliseconds);
            NativeMethods.PostMessage(process.MainWindowHandle, NativeMethods.WM_KEYUP, (int)key, 0);
        }

        public void KeyPressSleep(int key, int milliseconds)
        {
            NativeMethods.PostMessage(process.MainWindowHandle, NativeMethods.WM_KEYDOWN, (int)key, 0);
            Thread.Sleep(milliseconds);
            NativeMethods.PostMessage(process.MainWindowHandle, NativeMethods.WM_KEYUP, (int)key, 0);
        }

        public async Task LeftClickMouse(Point p)
        {
            SetCursorPosition(p);

            var pp = new PInvoke.POINT
            {
                x = p.X,
                y = p.Y
            };
            NativeMethods.ScreenToClient(process.MainWindowHandle, ref pp);
            int lparam = NativeMethods.MakeLParam(pp.x, pp.y);

            NativeMethods.PostMessage(process.MainWindowHandle, NativeMethods.WM_LBUTTONDOWN, 0, lparam);

            await Delay(MIN_DELAY);

            NativeMethods.GetCursorPos(out p);
            pp = new PInvoke.POINT
            {
                x = p.X,
                y = p.Y
            };
            NativeMethods.ScreenToClient(process.MainWindowHandle, ref pp);
            lparam = NativeMethods.MakeLParam(pp.x, pp.y);

            NativeMethods.PostMessage(process.MainWindowHandle, NativeMethods.WM_LBUTTONUP, 0, lparam);
        }

        public async Task RightClickMouse(Point p)
        {
            SetCursorPosition(p);

            var pp = new PInvoke.POINT
            {
                x = p.X,
                y = p.Y
            };
            NativeMethods.ScreenToClient(process.MainWindowHandle, ref pp);
            int lparam = NativeMethods.MakeLParam(pp.x, pp.y);

            NativeMethods.PostMessage(process.MainWindowHandle, NativeMethods.WM_RBUTTONDOWN, 0, lparam);

            await Delay(MIN_DELAY);

            NativeMethods.GetCursorPos(out p);
            pp = new PInvoke.POINT
            {
                x = p.X,
                y = p.Y
            };
            NativeMethods.ScreenToClient(process.MainWindowHandle, ref pp);
            lparam = NativeMethods.MakeLParam(pp.x, pp.y);

            NativeMethods.PostMessage(process.MainWindowHandle, NativeMethods.WM_RBUTTONUP, 0, lparam);
        }

        public void SetCursorPosition(Point p)
        {
            NativeMethods.SetCursorPos(p.X, p.Y);
        }

        public async Task SendText(string text)
        {
            // only works with ConsoleKey characters
            var chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                var consoleKey = consoleKeys.FirstOrDefault(k => k.ToString() == chars[i].ToString());
                if(consoleKey != 0)
                { 
                    await KeyPress((int)consoleKey, 15);
                }
            }
        }

        public void PasteFromClipboard()
        {
            // currently not supported
            throw new NotImplementedException();
        }
    }
}
