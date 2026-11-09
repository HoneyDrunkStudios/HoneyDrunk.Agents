using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace HoneyDrunk.Agents.Operations.Tests;

/// <summary>
/// Keeps every line the host logs, with its category and any exception in full, so a test can assert what never
/// reaches the logs.
/// </summary>
internal sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<string> lines = new();

    public IReadOnlyList<string> Lines => [.. this.lines];

    public ILogger CreateLogger(string categoryName) => new Capture(categoryName, this.lines);

    public void Dispose()
    {
        // Nothing to release; the lines stay readable after the host stops.
    }

    private sealed class Capture(string category, ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue($"{category} [{logLevel}] {formatter(state, exception)} {exception}");
    }
}
