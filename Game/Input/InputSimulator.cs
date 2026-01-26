using GregsStack.InputSimulatorStandard.Native;

using SixLabors.ImageSharp;

using System;
using System.Threading;

using TextCopy;

using static WinAPI.NativeMethods;

namespace Game;

public sealed class InputSimulator : IInput
{
    private readonly int maxDelay;

    private readonly GregsStack.InputSimulatorStandard.InputSimulator simulator;
    private readonly WowProcess process;

    private readonly CancellationToken token;

    public InputSimulator(WowProcess process, CancellationTokenSource cts, int maxDelay)
    {
        this.process = process;
        this.token = cts.Token;

        this.maxDelay = maxDelay;

        simulator = new();
    }

    private int DelayTime(int milliseconds)
    {
        return milliseconds + Random.Shared.Next(maxDelay);
    }

    public void KeyDown(int key)
    {
        if (GetForegroundWindow() != process.MainWindowHandle)
            SetForegroundWindow(process.MainWindowHandle);

        simulator.Keyboard.KeyDown((VirtualKeyCode)key);
    }

    public void KeyUp(int key)
    {
        if (GetForegroundWindow() != process.MainWindowHandle)
            SetForegroundWindow(process.MainWindowHandle);

        simulator.Keyboard.KeyUp((VirtualKeyCode)key);
    }

    public int PressRandom(int key, int milliseconds)
    {
        return PressRandom(key, milliseconds, token);
    }

    public int PressRandom(int key, int milliseconds, CancellationToken token)
    {
        simulator.Keyboard.KeyDown((VirtualKeyCode)key);

        int delay = DelayTime(milliseconds);
        token.WaitHandle.WaitOne(delay);

        simulator.Keyboard.KeyUp((VirtualKeyCode)key);

        return delay;
    }

    public void PressFixed(int key, int milliseconds, CancellationToken token)
    {
        simulator.Keyboard.KeyDown((VirtualKeyCode)key);
        token.WaitHandle.WaitOne(milliseconds);
        simulator.Keyboard.KeyUp((VirtualKeyCode)key);
    }

    public void LeftClick(Point p)
    {
        SetCursorPos(p);

        simulator.Mouse.LeftButtonDown();
        token.WaitHandle.WaitOne(DelayTime(maxDelay));
        simulator.Mouse.LeftButtonUp();
    }

    public void RightClick(Point p)
    {
        SetCursorPos(p);

        simulator.Mouse.RightButtonDown();
        token.WaitHandle.WaitOne(DelayTime(maxDelay));
        simulator.Mouse.RightButtonUp();
    }

    public void SetCursorPos(Point p)
    {
        GetWindowRect(process.MainWindowHandle, out Rectangle rect);
        p.X = p.X * 65535 / rect.Width;
        p.Y = p.Y * 65535 / rect.Height;
        simulator.Mouse.MoveMouseTo(Convert.ToDouble(p.X), Convert.ToDouble(p.Y));
    }

    public void SendText(string text)
    {
        if (GetForegroundWindow() != process.MainWindowHandle)
            SetForegroundWindow(process.MainWindowHandle);

        simulator.Keyboard.TextEntry(text);

        token.WaitHandle.WaitOne(DelayTime(maxDelay));
    }

    public void SetClipboard(string text)
    {
        ClipboardService.SetText(text);
    }

    public void PasteFromClipboard()
    {
        if (GetForegroundWindow() != process.MainWindowHandle)
            SetForegroundWindow(process.MainWindowHandle);

        simulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.LCONTROL, VirtualKeyCode.VK_V);
    }
}
