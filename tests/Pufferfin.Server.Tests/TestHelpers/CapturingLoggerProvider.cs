using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Pufferfin.Server.Tests.TestHelpers;

/// <summary>
/// Captures every formatted log message and exception, to prove secrets never reach a log.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider, ILoggerFactory
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyCollection<string> Entries => _entries;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);

    public ILogger<T> CreateLogger<T>() => new Logger<T>(this);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly ConcurrentQueue<string> _entries;

        public CapturingLogger(ConcurrentQueue<string> entries)
        {
            _entries = entries;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _entries.Enqueue(formatter(state, exception) + " " + exception);
            if (state is IEnumerable<KeyValuePair<string, object?>> properties)
            {
                foreach (var property in properties)
                {
                    _entries.Enqueue(property.Key + "=" + property.Value);
                }
            }
        }
    }
}
