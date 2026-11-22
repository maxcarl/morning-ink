using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MorningInk.Infrastructure.Tests;

/// <summary>Keeps every log message written through it, to check what never may be logged.</summary>
public sealed class CapturingLogger : ILoggerProvider, ILogger
{
    public ConcurrentQueue<string> Messages { get; } = new();

    public ILogger CreateLogger(string categoryName) => this;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Messages.Enqueue(formatter(state, exception));

    public void Dispose()
    {
    }
}
