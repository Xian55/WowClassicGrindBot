using Game;

using Microsoft.Extensions.Logging;

using System;
using System.Threading;

namespace Core;

public sealed class ExecGameCommand
{
    private readonly ILogger<ExecGameCommand> logger;
    private readonly WowProcessInput input;
    private readonly CancellationToken token;

    public ExecGameCommand(ILogger<ExecGameCommand> logger,
        CancellationTokenSource cts, WowProcessInput input)
    {
        this.logger = logger;
        token = cts.Token;
        this.input = input;
    }

    public void Run(string content)
    {
        input.SetForegroundWindow();
        logger.LogInformation(content);

        int duration = string.IsNullOrEmpty(content)
            ? InputDuration.VeryFastPress
            : Random.Shared.Next(100, 250);

        input.SetClipboard(content);
        token.WaitHandle.WaitOne(duration);

        // Open chat inputbox
        input.PressRandom(ConsoleKey.Enter, token: token);

        input.PasteFromClipboard();
        token.WaitHandle.WaitOne(duration);

        // Close chat inputbox
        input.PressRandom(ConsoleKey.Enter, token: token);
        token.WaitHandle.WaitOne(duration);
    }
}
