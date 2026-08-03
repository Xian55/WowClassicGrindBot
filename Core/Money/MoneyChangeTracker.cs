using Microsoft.Extensions.Logging;

using System;
using System.Text;

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
    private const int COPPER_PER_GOLD = 10000;
    private const int COPPER_PER_SILVER = 100;

    // The purse is capped by int copper, so the longest possible string is
    // "214748g 36s 47c" - 15 chars. Rounded up to leave the builder room rather
    // than have it grow on the one value that sits right on the boundary.
    private const int MAX_FORMATTED_LENGTH = 32;

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
        string amount = Format(Math.Abs(delta));

        LogMoneyChange(logger, sign, amount);
    }

    /// <summary>
    /// Only the units that carry a value, so a clean gold drop reads "2g"
    /// rather than "2g 0s 0c".
    /// </summary>
    private static string Format(int copper)
    {
        StringBuilder sb = new(MAX_FORMATTED_LENGTH);

        Append(sb, copper / COPPER_PER_GOLD, 'g');
        Append(sb, copper / COPPER_PER_SILVER % COPPER_PER_SILVER, 's');
        Append(sb, copper % COPPER_PER_SILVER, 'c');

        return sb.ToString();

        static void Append(StringBuilder sb, int value, char unit)
        {
            if (value == 0)
            {
                return;
            }

            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(value).Append(unit);
        }
    }

    #region Logging

    [LoggerMessage(
        EventId = 1996,
        Level = LogLevel.Information,
        Message = "{sign} {amount}")]
    static partial void LogMoneyChange(ILogger logger, char sign, string amount);

    #endregion
}
