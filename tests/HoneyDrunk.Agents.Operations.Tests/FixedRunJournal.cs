using HoneyDrunk.Agents.Operations.Runs;

namespace HoneyDrunk.Agents.Operations.Tests;

/// <summary>A journal that returns the records it was given, whatever was asked: a stand-in for a faulty store.</summary>
internal sealed class FixedRunJournal(params AgentRunRecord[] records) : IAgentRunJournal
{
    public Task StartedAsync(string agentId, string runId, DateTimeOffset at, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task CompletedAsync(string agentId, string runId, DateTimeOffset at, string status, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<AgentRunRecord>> RecentAsync(string agentId, int limit, string? beforeRunId, bool finishedOnly, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AgentRunRecord>>(records);

    public Task<AgentRunRecord?> GetAsync(string agentId, string runId, CancellationToken cancellationToken) =>
        Task.FromResult(records.FirstOrDefault(record => record.RunId == runId));
}
