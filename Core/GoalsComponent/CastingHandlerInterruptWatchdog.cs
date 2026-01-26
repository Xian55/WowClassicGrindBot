using Core.GOAP;

using Microsoft.Extensions.Logging;

using SharedLib;

using System;
using System.Threading;

#pragma warning disable 162

namespace Core.Goals;

public sealed class CastingHandlerInterruptWatchdog : IDisposable
{
    private const bool Log = false;

    private readonly ILogger<CastingHandlerInterruptWatchdog> logger;
    private readonly Wait wait;
    private readonly CancellationToken token;

    private readonly Thread thread;
    private readonly ManualResetEventSlim resetEvent;

    private bool initialValue;
    private Func<bool> interrupt = () => false;

    private CancellationTokenSource interruptCts;

    public CastingHandlerInterruptWatchdog(
        ILogger<CastingHandlerInterruptWatchdog> logger, Wait wait,
        CancellationTokenSource<GoapAgent> cts)
    {
        this.logger = logger;
        this.wait = wait;
        this.token = cts.Token;

        resetEvent = new(false);

        interruptCts = new();

        thread = new(Watchdog);
        thread.Start();
    }

    public void Dispose()
    {
        resetEvent.Set();
    }

    private void Watchdog()
    {
        while (!token.IsCancellationRequested)
        {
            while (!token.IsCancellationRequested && initialValue == interrupt.Invoke())
            {
                wait.Update(token);
                try
                {
                    resetEvent.Wait(token);
                }
                catch (OperationCanceledException) { }
            }

            interruptCts.Cancel();

            if (Log)
            {
                logger.LogWarning("Interrupted! Waiting...");
            }

            resetEvent.Reset();
            try
            {
                resetEvent.Wait(token);
            }
            catch (OperationCanceledException) { }
        }

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Thread stopped!");
    }

    public CancellationToken Set(Func<bool> interrupt)
    {
        resetEvent.Reset();

        initialValue = interrupt();
        this.interrupt = interrupt;

        if (!interruptCts.TryReset())
        {
            interruptCts.Dispose();
            interruptCts = new();

            if (Log)
                logger.LogDebug("New cts");
        }
        else if (Log)
            logger.LogDebug("Reuse cts");

        resetEvent.Set();

        return interruptCts.Token;
    }
}
