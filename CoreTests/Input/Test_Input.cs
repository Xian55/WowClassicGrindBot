using Game;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;

using System;
using System.Threading;

namespace CoreTests;

public sealed class Test_Input
{
    private const int delay = 500;

    private readonly CancellationToken token;

    private readonly ILogger logger;
    private readonly WowProcess process;
    private readonly IWowScreen screen;
    private readonly WowProcessInput input;

    public Test_Input(ILogger logger, CancellationTokenSource cts,
        WowProcess process, IWowScreen screen, ILoggerFactory loggerFactory)
    {
        this.logger = logger;
        this.token = cts.Token;
        this.process = process;
        this.screen = screen;

        input = new(loggerFactory.CreateLogger<WowProcessInput>(), cts, process);
    }

    public void Mouse_Movement()
    {
        input.SetForegroundWindow();

        input.SetCursorPos(new(25, 25));
        token.WaitHandle.WaitOne(delay);

        input.SetCursorPos(new(50, 50));
        token.WaitHandle.WaitOne(delay);

        logger.LogInformation($"{nameof(Mouse_Movement)} Finished");
    }

    public void Mouse_Clicks()
    {
        input.SetForegroundWindow();

        Point p = new(120, 120);
        input.LeftClick(p);

        token.WaitHandle.WaitOne(delay);

        input.RightClick(p);

        token.WaitHandle.WaitOne(delay);

        input.RightClick(p);

        screen.GetRectangle(out Rectangle rect);
        p = new Point(rect.Width / 2, rect.Height / 2);

        token.WaitHandle.WaitOne(delay);

        input.RightClick(p);

        token.WaitHandle.WaitOne(delay);

        input.RightClick(p);

        logger.LogInformation($"{nameof(Mouse_Clicks)} Finished");
    }

    public void Clipboard()
    {
        input.SetClipboard("/help");

        // Open chat inputbox
        input.PressRandom(ConsoleKey.Enter, delay);

        input.PasteFromClipboard();
        token.WaitHandle.WaitOne(delay);

        // Close chat inputbox
        input.PressRandom(ConsoleKey.Enter, delay);

        logger.LogInformation($"{nameof(Clipboard)} Finished");
    }
}
