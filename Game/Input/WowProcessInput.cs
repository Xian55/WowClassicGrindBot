using Microsoft.Extensions.Logging;

using SharedLib;

using SixLabors.ImageSharp;

using System;
using System.Collections;
using System.Threading;

using WinAPI;

namespace Game;

public sealed partial class WowProcessInput : IMouseInput
{
    // Virtual key codes for modifier keys
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;  // Alt key

    private readonly ILogger<WowProcessInput> logger;

    private readonly WowProcess process;
    private readonly InputWindowsNative nativeInput;

    private readonly BitArray keysDown;

    public ConsoleKey ForwardKey { get; set; }
    public ConsoleKey BackwardKey { get; set; }
    public ConsoleKey TurnLeftKey { get; set; }
    public ConsoleKey TurnRightKey { get; set; }
    public ConsoleKey InteractMouseover { get; set; }
    public ModifierKey InteractMouseoverModifier { get; set; }
    public int InteractMouseoverPress { get; set; }

    public WowProcessInput(ILogger<WowProcessInput> logger, CancellationTokenSource cts, WowProcess process)
    {
        this.logger = logger;
        this.process = process;

        keysDown = new((int)ConsoleKey.OemClear);

        nativeInput = new(process, cts, InputDuration.FastPress);
    }

    public void Reset()
    {
        lock (keysDown)
        {
            keysDown.SetAll(false);
        }
    }

    public void KeyDown(ConsoleKey key, bool forced)
    {
        if (IsKeyDown(key))
        {
            if (!forced)
                return;
        }

        //if (IsMovementKey(key))
        //    LogMoveKeyDown(logger, key);
        //else
        //    LogKeyDown(logger, key);

        keysDown[(int)key] = true;
        nativeInput.KeyDown((int)key);
    }

    public void KeyUp(ConsoleKey key, bool forced)
    {
        if (!IsKeyDown(key))
        {
            if (!forced)
                return;
        }

        //if (IsMovementKey(key))
        //    LogMoveKeyUp(logger, key);
        //else
        //    LogKeyUp(logger, key);

        nativeInput.KeyUp((int)key);
        keysDown[(int)key] = false;
    }

    public bool IsKeyDown(ConsoleKey key)
    {
        return keysDown[(int)key];
    }

    public void SendText(string text)
    {
        nativeInput.SendText(text);
    }

    public void SetForegroundWindow()
    {
        NativeMethods.SetForegroundWindow(process.MainWindowHandle);
    }

    public int PressRandom(ConsoleKey key, int milliseconds = InputDuration.DefaultPress, CancellationToken token = default)
    {
        keysDown[(int)key] = true;
        int elapsedMs = nativeInput.PressRandom((int)key, milliseconds, token);
        keysDown[(int)key] = false;

        LogKeyPressRandom(logger, key, elapsedMs);

        return elapsedMs;
    }

    public int PressRandomWithModifier(ConsoleKey key, ModifierKey modifier, int milliseconds = InputDuration.DefaultPress, CancellationToken token = default)
    {
        // If no modifier, use the simple path
        if (modifier == ModifierKey.None)
        {
            return PressRandom(key, milliseconds, token);
        }

        // Note: PostMessage sends WM_KEYDOWN to the window's message queue.
        // WoW processes messages in FIFO order, so modifier keys are "pressed"
        // before the main key. No delay needed between messages.
        // If WoW uses GetKeyState() instead of tracking WM_KEYDOWN messages,
        // modifiers may not work - would need SendInput (foreground only).

        // Press modifier(s) down
        if ((modifier & ModifierKey.Shift) != 0)
            nativeInput.KeyDown(VK_SHIFT);
        if ((modifier & ModifierKey.Ctrl) != 0)
            nativeInput.KeyDown(VK_CONTROL);
        if ((modifier & ModifierKey.Alt) != 0)
            nativeInput.KeyDown(VK_MENU);

        // Press actual key
        keysDown[(int)key] = true;
        int elapsedMs = nativeInput.PressRandom((int)key, milliseconds, token);
        keysDown[(int)key] = false;

        // Release modifiers (reverse order)
        if ((modifier & ModifierKey.Alt) != 0)
            nativeInput.KeyUp(VK_MENU);
        if ((modifier & ModifierKey.Ctrl) != 0)
            nativeInput.KeyUp(VK_CONTROL);
        if ((modifier & ModifierKey.Shift) != 0)
            nativeInput.KeyUp(VK_SHIFT);

        LogKeyPressRandomWithModifier(logger, key, modifier, elapsedMs);

        return elapsedMs;
    }

    public void PressFixed(ConsoleKey key, int milliseconds, CancellationToken token = default)
    {
        if (milliseconds < 1)
            return;

        if (IsMovementKey(key))
            LogMoveKeyPress(logger, key, milliseconds);
        else
            LogKeyPressFixed(logger, key, milliseconds);

        keysDown[(int)key] = true;
        nativeInput.PressFixed((int)key, milliseconds, token);
        keysDown[(int)key] = false;
    }

    public void SetKeyState(ConsoleKey key, bool pressDown, bool forced)
    {
        if (pressDown)
            KeyDown(key, forced);
        else
            KeyUp(key, forced);
    }

    public void SetCursorPos(Point p)
    {
        nativeInput.SetCursorPos(p);
    }

    public void RightClick(Point p)
    {
        nativeInput.RightClick(p);
    }

    public void LeftClick(Point p)
    {
        nativeInput.LeftClick(p);
    }

    public void InteractMouseOver(CancellationToken token)
    {
        if (InteractMouseoverModifier != ModifierKey.None)
        {
            PressRandomWithModifier(InteractMouseover, InteractMouseoverModifier, InteractMouseoverPress, token);
        }
        else
        {
            PressFixed(InteractMouseover, InteractMouseoverPress, token);
        }
    }

    /// <summary>
    /// Presses SHIFT-PAGEDOWN to trigger CUSTOM_FLUSH (/dcflush) in the addon.
    /// </summary>
    public void PressFlushKey()
    {
        PressRandomWithModifier(ConsoleKey.PageDown, ModifierKey.Shift, 50);
    }

    private bool IsMovementKey(ConsoleKey key) =>
        key == ForwardKey ||
        key == BackwardKey ||
        key == TurnLeftKey ||
        key == TurnRightKey;

    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Debug,
        Message = @"[{key}] KeyDown")]
    static partial void LogKeyDown(ILogger logger, ConsoleKey key);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Debug,
        Message = @"[{key}] KeyUp")]
    static partial void LogKeyUp(ILogger logger, ConsoleKey key);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Information,
        Message = @"[{key}] press fix {milliseconds}ms")]
    static partial void LogKeyPressFixed(ILogger logger, ConsoleKey key, int milliseconds);

    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Information,
        Message = @"[{key}] press random {milliseconds}ms")]
    static partial void LogKeyPressRandom(ILogger logger, ConsoleKey key, int milliseconds);

    [LoggerMessage(
        EventId = 3007,
        Level = LogLevel.Information,
        Message = @"[{modifier}-{key}] press random {milliseconds}ms")]
    static partial void LogKeyPressRandomWithModifier(ILogger logger, ConsoleKey key, ModifierKey modifier, int milliseconds);

    #region Movement Trance

    [LoggerMessage(
        EventId = 3004,
        Level = LogLevel.Trace,
        Message = @"[{key}] move KeyDown")]
    static partial void LogMoveKeyDown(ILogger logger, ConsoleKey key);

    [LoggerMessage(
        EventId = 3005,
        Level = LogLevel.Trace,
        Message = @"[{key}] move KeyUp")]
    static partial void LogMoveKeyUp(ILogger logger, ConsoleKey key);

    [LoggerMessage(
        EventId = 3006,
        Level = LogLevel.Trace,
        Message = @"[{key}] move Pressed {milliseconds}ms")]
    static partial void LogMoveKeyPress(ILogger logger, ConsoleKey key, int milliseconds);

    #endregion
}
