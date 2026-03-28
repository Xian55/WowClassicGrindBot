using System.Collections.Concurrent;

using Serilog.Core;
using Serilog.Events;

namespace Frontend;

public sealed class ShortSourceContextEnricher : ILogEventEnricher
{
    private static readonly ConcurrentDictionary<string, LogEventProperty> Cache = [];

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (logEvent.Properties.TryGetValue("SourceContext", out LogEventPropertyValue? value) &&
            value is ScalarValue { Value: string sourceContext } &&
            sourceContext.Length > 0)
        {
            logEvent.AddPropertyIfAbsent(
                Cache.GetOrAdd(sourceContext, static (ctx, factory) =>
                {
                    int lastDot = ctx.LastIndexOf('.');
                    string shortName = lastDot >= 0 ? ctx[(lastDot + 1)..] : ctx;
                    return factory.CreateProperty("ShortSource", shortName);
                }, propertyFactory));
        }
    }
}
