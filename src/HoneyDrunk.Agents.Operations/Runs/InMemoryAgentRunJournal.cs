using System.Collections.Concurrent;

namespace HoneyDrunk.Agents.Operations.Runs;

/// <summary>
/// The default run journal: it keeps each agent's newest runs in this process's memory and loses them when the
/// process ends. Every call completes synchronously, so runs are recorded in the order the lifecycle reports them.
/// </summary>
public sealed class InMemoryAgentRunJournal : IAgentRunJournal
{
    private readonly ConcurrentDictionary<string, AgentRuns> agents = new(StringComparer.Ordinal);
    private readonly int maxRunsPerAgent;

    /// <summary>Initializes a new instance of the <see cref="InMemoryAgentRunJournal"/> class.</summary>
    /// <param name="maxRunsPerAgent">How many runs to keep per agent; the oldest are dropped first.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxRunsPerAgent"/> is below 1.</exception>
    public InMemoryAgentRunJournal(int maxRunsPerAgent = 100)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRunsPerAgent, 1);
        this.maxRunsPerAgent = maxRunsPerAgent;
    }

    /// <inheritdoc />
    public Task StartedAsync(string agentId, string runId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        ArgumentNullException.ThrowIfNull(runId);
        cancellationToken.ThrowIfCancellationRequested();
        this.RunsOf(agentId).Start(new AgentRunRecord(runId, agentId, at, null, null));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CompletedAsync(string agentId, string runId, DateTimeOffset at, string status, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        ArgumentNullException.ThrowIfNull(runId);
        ArgumentNullException.ThrowIfNull(status);
        cancellationToken.ThrowIfCancellationRequested();
        this.RunsOf(agentId).Complete(agentId, runId, at, status);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AgentRunRecord>> RecentAsync(string agentId, int limit, string? beforeRunId, bool finishedOnly, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<AgentRunRecord> runs = this.agents.TryGetValue(agentId, out var kept)
            ? kept.Recent(limit, beforeRunId, finishedOnly)
            : [];
        return Task.FromResult(runs);
    }

    /// <inheritdoc />
    public Task<AgentRunRecord?> GetAsync(string agentId, string runId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        ArgumentNullException.ThrowIfNull(runId);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(this.agents.TryGetValue(agentId, out var kept) ? kept.Find(runId) : null);
    }

    private AgentRuns RunsOf(string agentId) => this.agents.GetOrAdd(agentId, static (_, cap) => new AgentRuns(cap), this.maxRunsPerAgent);

    /// <summary>One agent's runs, oldest first, behind one lock. Records are immutable and replaced whole.</summary>
    private sealed class AgentRuns(int cap)
    {
        private readonly Lock gate = new();
        private readonly List<AgentRunRecord> runs = [];

        public void Start(AgentRunRecord run)
        {
            lock (this.gate)
            {
                if (this.IndexOf(run.RunId) < 0)
                {
                    this.Insert(run);
                }
            }
        }

        public void Complete(string agentId, string runId, DateTimeOffset at, string status)
        {
            lock (this.gate)
            {
                var index = this.IndexOf(runId);
                if (index < 0)
                {
                    this.Insert(new AgentRunRecord(runId, agentId, at, at, status));
                }
                else if (this.runs[index].CompletedAt is null)
                {
                    this.runs[index] = this.runs[index] with { CompletedAt = at, Status = status };
                }
            }
        }

        public AgentRunRecord? Find(string runId)
        {
            lock (this.gate)
            {
                var index = this.IndexOf(runId);
                return index < 0 ? null : this.runs[index];
            }
        }

        public AgentRunRecord[] Recent(int limit, string? beforeRunId, bool finishedOnly)
        {
            lock (this.gate)
            {
                var from = beforeRunId is null ? this.runs.Count : this.IndexOf(beforeRunId);
                var page = new List<AgentRunRecord>(Math.Min(limit, Math.Max(from, 0)));
                for (var index = from - 1; index >= 0 && page.Count < limit; index--)
                {
                    if (!finishedOnly || this.runs[index].CompletedAt is not null)
                    {
                        page.Add(this.runs[index]);
                    }
                }

                return [.. page];
            }
        }

        private static int Compare(AgentRunRecord left, AgentRunRecord right)
        {
            var byStart = left.StartedAt.CompareTo(right.StartedAt);
            return byStart != 0 ? byStart : string.CompareOrdinal(left.RunId, right.RunId);
        }

        private int IndexOf(string runId)
        {
            for (var index = this.runs.Count - 1; index >= 0; index--)
            {
                if (string.Equals(this.runs[index].RunId, runId, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        private void Insert(AgentRunRecord run)
        {
            // Runs nearly always arrive in order, so the search starts from the newest end.
            var index = this.runs.Count;
            while (index > 0 && Compare(this.runs[index - 1], run) > 0)
            {
                index--;
            }

            this.runs.Insert(index, run);
            if (this.runs.Count > cap)
            {
                this.runs.RemoveAt(0);
            }
        }
    }
}
