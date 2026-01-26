using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;

using System;
using System.Collections;
using System.Threading;

using WinAPI;

namespace Game;

public sealed partial class WowProcessInput : IMouseInput
{
    private readonly ILogger<WowProcessInput> logger;

    private readonly WowProcess process;
    private readonly InputWindowsNative nativeInput;
    private readonly InputSimulator simulatorInput;

    private readonly BitArray keysDown;

    public ConsoleKey ForwardKey { get; set; }
    public ConsoleKey BackwardKey { get; set; }
    public ConsoleKey TurnLeftKey { get; set; }
    public ConsoleKey TurnRightKey { get; set; }
    public ConsoleKey InteractMouseover { get; set; }
    public int InteractMouseoverPress { get; set; }

    public WowProcessInput(ILogger<WowProcessInput> logger, CancellationTokenSource cts, WowProcess process)
    {
        this.logger = logger;
        this.process = process;

        keysDown = new((int)ConsoleKey.OemClear);

        nativeInput = new(process, cts, InputDuration.FastPress);
        simulatorInput = new(process, cts, InputDuration.FastPress);
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

    public void SendText(string payload)
    {
        simulatorInput.SendText(payload);
    }

    public void SetClipboard(string text)
    {
        simulatorInput.SetClipboard(text);
    }

    public void PasteFromClipboard()
    {
        simulatorInput.PasteFromClipboard();
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
        PressFixed(InteractMouseover, InteractMouseoverPress, token);
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
