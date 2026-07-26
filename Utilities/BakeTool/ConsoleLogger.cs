using Microsoft.Extensions.Logging;

using System;

namespace BakeTool;

/// <summary>
/// Minimal <see cref="ILogger{T}"/> writing to stdout. The bake logs warnings
/// that matter to a headless run - missing area grids, failed archive opens,
/// per-tile bake failures - so they must be visible, but pulling a console
/// logging provider into Central Package Management for one utility is not worth
/// it.
/// </summary>
internal sealed class ConsoleLogger<T> : ILogger<T>
{
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        string level = logLevel switch
        {
            LogLevel.Critical => "CRIT",
            LogLevel.Error => "FAIL",
            LogLevel.Warning => "WARN",
            LogLevel.Information => "INFO",
            _ => logLevel.ToString().ToUpperInvariant(),
        };

        Console.WriteLine($"  {level} {formatter(state, exception)}");

        if (exception is not null)
        {
            Console.WriteLine($"       {exception.GetType().Name}: {exception.Message}");
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose() { }
    }
}
