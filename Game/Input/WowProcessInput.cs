using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using WinAPI;

namespace Game
{
    public class WowProcessInput : IMouseInput
    {
        private const int MIN_DELAY = 25;
        private const int MAX_DELAY = 55;

        private readonly ILogger logger;
        private readonly WowProcess wowProcess;
        private readonly IInput nativeInput;
        private readonly IInput simulatorInput;

        private readonly ConcurrentDictionary<ConsoleKey, bool> keyDict = new ConcurrentDictionary<ConsoleKey, bool>();

        private bool debug = true;

        public WowProcessInput(ILogger logger, WowProcess wowProcess)
        {
            this.logger = logger;
            this.wowProcess = wowProcess;

            this.nativeInput = new InputWindowsNative(wowProcess.WarcraftProcess, MIN_DELAY, MAX_DELAY);
            this.simulatorInput = new InputSimulator(wowProcess.WarcraftProcess, MIN_DELAY, MAX_DELAY);
        }

        private void KeyDown(ConsoleKey key, string description = "")
        {
            if (keyDict.ContainsKey(key))
            {
                //if (keyDict[key] == true) { return; }
            }
            else
            {
                if(!keyDict.TryAdd(key, true))
                {
                    return;
                }
            }

            if (!string.IsNullOrEmpty(description))
                Log($"KeyDown {key} " + description);

            nativeInput.KeyDown((int)key);
            keyDict[key] = true;
        }

        private void KeyUp(ConsoleKey key, bool forceClick)
        {
            if (keyDict.ContainsKey(key))
            {
                if (!forceClick)
                {
                    if (keyDict[key] == false) { return; }
                }
            }
            else
            {
                if(!keyDict.TryAdd(key, false))
                {
                    return;
                }
            }

            Log($"KeyUp {key}");
            nativeInput.KeyUp((int)key);

            keyDict[key] = false;
        }

        public bool IsKeyDown(ConsoleKey key)
        {
            if(keyDict.TryGetValue(key, out var down))
            {
                return down;
            }
            return false;
        }

        public async Task SendText(string payload)
        {
            await simulatorInput.SendText(payload);
        }

        public void PasteFromClipboard()
        {
            simulatorInput.PasteFromClipboard();
        }

        public void SetForegroundWindow()
        {
            NativeMethods.SetForegroundWindow(wowProcess.WarcraftProcess.MainWindowHandle);
        }


        public async Task KeyPress(ConsoleKey key, int milliseconds, string description = "")
        {
            if (!string.IsNullOrEmpty(description))
                Log($"{description}[{key}] pressing for {milliseconds}ms");

            await nativeInput.KeyPress((int)key, milliseconds);
        }

        public void KeyPressSleep(ConsoleKey key, int milliseconds, string description = "")
        {
            if (milliseconds < 1)
                return;

            if (!string.IsNullOrEmpty(description))
                Log($"{description}[{key}] pressing for {milliseconds}ms");

            nativeInput.KeyPressSleep((int)key, milliseconds);
        }

        public void SetKeyState(ConsoleKey key, bool pressDown, bool forceClick, string description = "")
        {
            if (!string.IsNullOrEmpty(description))
                Log($"SetKeyState: {description}");

            if (pressDown) { KeyDown(key, description); } else { KeyUp(key, forceClick); }
        }

        public void SetCursorPosition(Point position)
        {
            nativeInput.SetCursorPosition(position);
        }

        public async Task RightClickMouse(Point position)
        {
            await nativeInput.RightClickMouse(position);
        }

        public async Task LeftClickMouse(Point position)
        {
            await nativeInput.LeftClickMouse(position);
        }

        private void Log(string text)
        {
            if (debug)
                logger.LogInformation($"Input: {text}");
        }
    }
}
