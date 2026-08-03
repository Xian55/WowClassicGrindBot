using Microsoft.Extensions.Logging;

using System;

namespace Core;

public interface IMoneyChangeTracker { }

public sealed class NoMoneyChangeTracker : IMoneyChangeTracker { }

/// <summary>
/// Logs purse movements the way the bag log reads: <c>+ 2g 59s 1c</c>.
/// The counterpart of <see cref="BagChangeTracker"/> for coin, which never
/// touches the bags and so did not show up anywhere.
/// </summary>
public sealed partial class MoneyChangeTracker : IDisposable, IMoneyChangeTracker
{
    private readonly ILogger<MoneyChangeTracker> logger;
    private readonly PlayerReader playerReader;

    // The addon reports the whole balance, so the delta has to be derived. The
    // bot can be started at any moment, which makes the first change an opening
    // balance rather than a gain - announcing it would read as free gold.
    private bool primed;
    private int previous;

    public MoneyChangeTracker(ILogger<MoneyChangeTracker> logger,
        PlayerReader playerReader)
    {
        this.logger = logger;
        this.playerReader = playerReader;

        playerReader.Money.Changed += Money_Changed;
    }

    public void Dispose()
    {
        playerReader.Money.Changed -= Money_Changed;
    }

    private void Money_Changed()
    {
        int current = playerReader.Money.Value;

        if (!primed)
        {
            primed = true;
            previous = current;
            return;
        }

        int delta = current - previous;
        previous = current;

        // Guarded because Format allocates - the bookkeeping above must happen
        // either way, so that a level change mid-session does not report a
        // delta spanning everything that moved while logging was off.
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        // Locals, not inline arguments: CA1873 does not track the guard above
        // for source-generated log methods, only local variable access.
        char sign = delta > 0 ? '+' : '-';
        string amount = Coin.Format(Math.Abs(delta));

        LogMoneyChange(logger, sign, amount);
    }

    #region Logging

    [LoggerMessage(
        EventId = 1996,
        Level = LogLevel.Information,
        Message = "{sign} {amount}")]
    static partial void LogMoneyChange(ILogger logger, char sign, string amount);

    #endregion
}
