using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Pufferfin.Catalog.Tests.TestSupport;

/// <summary>
/// Records every formatted message and exception, for any category.
/// </summary>
public sealed class CapturingLogger : ILoggerFactory, ILogger
{
    public ConcurrentQueue<string> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Entries.Enqueue(formatter(state, exception) + " " + exception);
    }

    public ILogger CreateLogger(string categoryName) => this;

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }
}
