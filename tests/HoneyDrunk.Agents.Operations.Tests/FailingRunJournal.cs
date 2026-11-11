using HoneyDrunk.Agents.Operations.Runs;

namespace HoneyDrunk.Agents.Operations.Tests;

/// <summary>A journal whose writes fail: by throwing, by a faulted task, or by a task the test faults later.</summary>
internal sealed class FailingRunJournal(FailingRunJournal.Mode mode) : IAgentRunJournal
{
    /// <summary>How a write fails.</summary>
    internal enum Mode
    {
        /// <summary>The call throws.</summary>
        Throws,

        /// <summary>The call returns a faulted task.</summary>
        Faulted,

        /// <summary>The call returns <see cref="Pending"/>'s task, which the test faults later.</summary>
        Later,
    }

    public TaskCompletionSource Pending { get; } = new();

    public Task StartedAsync(string agentId, string runId, DateTimeOffset at, CancellationToken cancellationToken) => this.Fail();

    public Task CompletedAsync(string agentId, string runId, DateTimeOffset at, string status, CancellationToken cancellationToken) => this.Fail();

    public Task<IReadOnlyList<AgentRunRecord>> RecentAsync(string agentId, int limit, string? beforeRunId, bool finishedOnly, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("journal-secret");

    public Task<AgentRunRecord?> GetAsync(string agentId, string runId, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("journal-secret");

    private Task Fail() => mode switch
    {
        Mode.Throws => throw new InvalidOperationException("journal-secret"),
        Mode.Faulted => Task.FromException(new InvalidOperationException("journal-secret")),
        Mode.Later => this.Pending.Task,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };
}
