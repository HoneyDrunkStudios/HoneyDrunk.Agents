using Microsoft.Extensions.Logging;

namespace HoneyDrunk.Agents.Tests;

/// <summary>Captures log entries for assertions.</summary>
/// <typeparam name="T">The logger category.</typeparam>
internal sealed class ListLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel level, string message, Exception? exception)> entries = [];

    public IReadOnlyList<(LogLevel level, string message, Exception? exception)> Entries
    {
        get
        {
            lock (this.entries)
            {
                return [.. this.entries];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (this.entries)
        {
            this.entries.Add((logLevel, formatter(state, exception), exception));
        }
    }
}
