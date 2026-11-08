using HoneyDrunk.Agents.Abstractions;
using System.Collections.Concurrent;

namespace HoneyDrunk.Agents.Testing;

/// <summary>A tool invoker that returns scripted results per tool and records every call. Unscripted tools are not found.</summary>
public sealed class InMemoryToolInvoker : IToolInvoker
{
    private readonly ConcurrentDictionary<string, ToolCallResult> results = new(StringComparer.Ordinal);
    private readonly List<ToolCall> calls = [];

    /// <summary>Gets the calls received, in order.</summary>
    public IReadOnlyList<ToolCall> Calls
    {
        get
        {
            lock (this.calls)
            {
                return [.. this.calls];
            }
        }
    }

    /// <summary>Sets the result for a tool.</summary>
    /// <param name="name">The tool name.</param>
    /// <param name="version">The tool version.</param>
    /// <param name="result">The result to return.</param>
    public void Script(string name, string version, ToolCallResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        this.results[name + "@" + version] = result;
    }

    /// <inheritdoc />
    public Task<ToolCallResult> InvokeAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);
        lock (this.calls)
        {
            this.calls.Add(call);
        }

        return Task.FromResult(this.results.TryGetValue(call.Name + "@" + call.Version, out var result)
            ? result
            : new ToolCallResult(ToolCallStatus.NotFound, null, "not_found", null));
    }
}
