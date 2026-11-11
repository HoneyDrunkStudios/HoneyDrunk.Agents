using HoneyDrunk.Agents.Operations.Runs;

namespace HoneyDrunk.Agents.Operations.Tests.Runs;

/// <summary>A real in-memory journal whose first start is held before persistence.</summary>
internal sealed class GatedRunJournal : IAgentRunJournal
{
    private readonly InMemoryAgentRunJournal inner = new();

    public TaskCompletionSource ReleaseStart { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool CompletionCalled { get; private set; }

    public async Task StartedAsync(string agentId, string runId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        if (agentId == "a")
        {
            await this.ReleaseStart.Task.ConfigureAwait(false);
        }

        await this.inner.StartedAsync(agentId, runId, at, cancellationToken).ConfigureAwait(false);
    }

    public async Task CompletedAsync(string agentId, string runId, DateTimeOffset at, string status, CancellationToken cancellationToken)
    {
        await this.inner.CompletedAsync(agentId, runId, at, status, cancellationToken).ConfigureAwait(false);
        if (agentId == "a")
        {
            this.CompletionCalled = true;
            this.Completion.TrySetResult();
        }
    }

    public Task<IReadOnlyList<AgentRunRecord>> RecentAsync(string agentId, int limit, string? beforeRunId, bool finishedOnly, CancellationToken cancellationToken) =>
        this.inner.RecentAsync(agentId, limit, beforeRunId, finishedOnly, cancellationToken);

    public Task<AgentRunRecord?> GetAsync(string agentId, string runId, CancellationToken cancellationToken) =>
        this.inner.GetAsync(agentId, runId, cancellationToken);
}
