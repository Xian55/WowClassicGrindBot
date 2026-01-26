using Serilog.Core;
using Serilog.Events;

using System;

namespace PathingAPI;

public sealed class PathingAPILoggerSink : ILogEventSink
{
    public event Action<LogEvent> OnLog;

    public void Emit(LogEvent logEvent)
    {
        OnLog?.Invoke(logEvent);
    }
}
