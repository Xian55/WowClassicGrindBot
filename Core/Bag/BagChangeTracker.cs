using Microsoft.Extensions.Logging;

using System;

namespace Core;

public interface IBagChangeTracker { }

public class NoBagChangeTracker : IBagChangeTracker { }

public sealed partial class BagChangeTracker : IDisposable, IBagChangeTracker
{
    private readonly ILogger<BagChangeTracker> logger;
    private readonly BagReader reader;

    public BagChangeTracker(ILogger<BagChangeTracker> logger, BagReader reader)
    {
        this.logger = logger;
        this.reader = reader;

        reader.BagItemChange += Reader_DataChanged;
    }

    public void Dispose()
    {
        reader.BagItemChange -= Reader_DataChanged;
    }

    private void Reader_DataChanged(BagItem bagItem, BagItemChange change)
    {
        switch (change)
        {
            case BagItemChange.New:
                LogItemNew(logger,
                    bagItem.Count, bagItem.Item.Name);
                break;
            case BagItemChange.Remove:
                LogItemRemove(logger,
                    bagItem.Count, bagItem.Item.Name);
                break;
            case BagItemChange.Update:
                LogItemUpdate(logger,
                    bagItem.LastCount, bagItem.Count, bagItem.Item.Name);
                break;
        }
    }


    #region Logging

    [LoggerMessage(
        EventId = 1997,
        Level = LogLevel.Information,
        Message = "{oldCount,2} -> {newCount,2} {name}")]
    static partial void LogItemUpdate(ILogger logger,
        int oldCount, int newCount, string name);

    [LoggerMessage(
        EventId = 1998,
        Level = LogLevel.Information,
        Message = "-{count,2} {name}")]
    static partial void LogItemRemove(ILogger logger, int count, string name);

    [LoggerMessage(
        EventId = 1999,
        Level = LogLevel.Information,
        Message = "+{count,2} {name}")]
    static partial void LogItemNew(ILogger logger, int count, string name);

    #endregion
}
