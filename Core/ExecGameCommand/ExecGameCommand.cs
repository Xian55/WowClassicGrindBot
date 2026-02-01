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

    public void Run(string content) => Run(content, content);

    /// <summary>
    /// Executes a game command with a custom log message.
    /// Use this overload when the command contains sensitive data (e.g., recipient names).
    /// </summary>
    /// <param name="content">The command to execute.</param>
    /// <param name="logMessage">The message to log. Pass null to suppress logging entirely.</param>
    public void Run(string content, string? logMessage)
    {
        if (string.IsNullOrEmpty(content))
        {
            return;
        }

        input.SetForegroundWindow();

        if (logMessage != null)
        {
            logger.LogInformation(logMessage);
        }

        int duration = Random.Shared.Next(100, 250);

        // Open chat inputbox
        input.PressRandom(ConsoleKey.Enter, token: token);
        token.WaitHandle.WaitOne(duration);

        // Send text directly via WM_CHAR messages
        input.SendText(content);
        token.WaitHandle.WaitOne(duration);

        // Close chat inputbox and execute command
        input.PressRandom(ConsoleKey.Enter, token: token);
        token.WaitHandle.WaitOne(duration);
    }
}
