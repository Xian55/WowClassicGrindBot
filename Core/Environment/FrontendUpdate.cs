using Microsoft.Extensions.Logging;

using System.Threading;

namespace Core;

public sealed class FrontendUpdate
{
    private readonly ILogger<FrontendUpdate> logger;
    private readonly IAddonReader addonReader;
    private readonly CancellationToken token;

    private readonly Thread thread;
    private const int tickMs = 250;

    public FrontendUpdate(ILogger<FrontendUpdate> logger,
        IAddonReader addonReader, CancellationTokenSource cts)
    {
        this.logger = logger;
        this.addonReader = addonReader;
        this.token = cts.Token;

        thread = new(Update);
        thread.Start();
    }

    private void Update()
    {
        while (!token.IsCancellationRequested)
        {
            addonReader.UpdateUI();
            token.WaitHandle.WaitOne(tickMs);
        }

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Thread stopped!");
    }
}
