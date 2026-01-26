using SixLabors.ImageSharp;

using System;
using System.Threading;

using TextCopy;

using static WinAPI.NativeMethods;

namespace Game;

public sealed class InputWindowsNative : IInput
{
    private readonly int maxDelay;

    private readonly WowProcess process;
    private readonly CancellationToken token;

    public InputWindowsNative(WowProcess process, CancellationTokenSource cts, int maxDelay)
    {
        this.process = process;
        token = cts.Token;

        this.maxDelay = maxDelay;
    }

    private int DelayTime(int milliseconds)
    {
        return milliseconds + Random.Shared.Next(maxDelay);
    }

    public void KeyDown(int key)
    {
        PostMessage(process.MainWindowHandle, WM_KEYDOWN, key, 0);
    }

    public void KeyUp(int key)
    {
        PostMessage(process.MainWindowHandle, WM_KEYUP, key, 0);
    }

    public int PressRandom(int key, int milliseconds)
    {
        return PressRandom(key, milliseconds, token);
    }

    public int PressRandom(int key, int milliseconds, CancellationToken token)
    {
        PostMessage(process.MainWindowHandle, WM_KEYDOWN, key, 0);

        int delay = DelayTime(milliseconds);
        token.WaitHandle.WaitOne(delay);

        PostMessage(process.MainWindowHandle, WM_KEYUP, key, 0);

        return delay;
    }

    public void PressFixed(int key, int milliseconds, CancellationToken token)
    {
        PostMessage(process.MainWindowHandle, WM_KEYDOWN, key, 0);
        token.WaitHandle.WaitOne(milliseconds);
        PostMessage(process.MainWindowHandle, WM_KEYUP, key, 0);
    }

    public void LeftClick(Point p)
    {
        SetCursorPos(p);

        ScreenToClient(process.MainWindowHandle, ref p);
        int lparam = MakeLParam(p.X, p.Y);

        PostMessage(process.MainWindowHandle, WM_LBUTTONDOWN, 0, lparam);

        token.WaitHandle.WaitOne(DelayTime(maxDelay));

        GetCursorPos(out p);
        ScreenToClient(process.MainWindowHandle, ref p);
        lparam = MakeLParam(p.X, p.Y);

        PostMessage(process.MainWindowHandle, WM_LBUTTONUP, 0, lparam);
    }

    public void RightClick(Point p)
    {
        SetCursorPos(p);

        ScreenToClient(process.MainWindowHandle, ref p);
        int lparam = MakeLParam(p.X, p.Y);

        PostMessage(process.MainWindowHandle, WM_RBUTTONDOWN, 0, lparam);

        token.WaitHandle.WaitOne(DelayTime(maxDelay));

        GetCursorPos(out p);
        ScreenToClient(process.MainWindowHandle, ref p);
        lparam = MakeLParam(p.X, p.Y);

        PostMessage(process.MainWindowHandle, WM_RBUTTONUP, 0, lparam);
    }

    public void SetCursorPos(Point p)
    {
        WinAPI.NativeMethods.SetCursorPos(p.X, p.Y);
    }

    public void SendText(string text)
    {
        // currently not supported
        throw new NotImplementedException();
    }

    public void SetClipboard(string text)
    {
        ClipboardService.SetText(text);
    }

    public void PasteFromClipboard()
    {
        // currently not supported
        throw new NotImplementedException();
    }
}
