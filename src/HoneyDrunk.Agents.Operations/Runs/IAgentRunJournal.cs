namespace HoneyDrunk.Agents.Operations.Runs;

/// <summary>
/// A bounded record of agent runs, fed by lifecycle transitions and read by the operations endpoints. An
/// implementation keeps ids, times and status names only, and must be safe to call from many threads.
/// </summary>
public interface IAgentRunJournal
{
    /// <summary>Records that a run started. A run id already kept is left unchanged.</summary>
    /// <param name="agentId">The agent id.</param>
    /// <param name="runId">The execution id.</param>
    /// <param name="at">When the run started.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the start is recorded.</returns>
    Task StartedAsync(string agentId, string runId, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>
    /// Records that a run completed. A run the journal never saw start is recorded whole, starting at
    /// <paramref name="at"/>; a run already completed is left unchanged.
    /// </summary>
    /// <param name="agentId">The agent id.</param>
    /// <param name="runId">The execution id.</param>
    /// <param name="at">When the run completed.</param>
    /// <param name="status">One of <see cref="AgentRunStatus.All"/>.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the completion is recorded.</returns>
    Task CompletedAsync(string agentId, string runId, DateTimeOffset at, string status, CancellationToken cancellationToken);

    /// <summary>Reads an agent's runs, newest first: by start time, then by run id, both descending.</summary>
    /// <param name="agentId">The agent id.</param>
    /// <param name="limit">The most runs to return; at least 1.</param>
    /// <param name="beforeRunId">
    /// When set, only runs older than this run are returned. A run id the journal does not keep returns nothing.
    /// </param>
    /// <param name="finishedOnly">Whether to leave out runs that have not completed.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The runs; empty for an agent with none. No run appears twice.</returns>
    Task<IReadOnlyList<AgentRunRecord>> RecentAsync(string agentId, int limit, string? beforeRunId, bool finishedOnly, CancellationToken cancellationToken);

    /// <summary>Reads one run.</summary>
    /// <param name="agentId">The agent id.</param>
    /// <param name="runId">The execution id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The run, or null when the journal does not keep it under that agent.</returns>
    Task<AgentRunRecord?> GetAsync(string agentId, string runId, CancellationToken cancellationToken);
}
