using Microsoft.Extensions.Logging;

namespace HoneyDrunk.Agents.Operations.Runs;

/// <summary>
/// Feeds the run journal from lifecycle transitions: <c>execute</c> starts a run and <c>complete</c> finishes it.
/// Other transitions are ignored. A journal failure is logged by exception type name and never reaches the lifecycle.
/// </summary>
/// <param name="journal">The run journal.</param>
/// <param name="logger">The logger.</param>
public sealed partial class RunJournalObserver(IAgentRunJournal journal, ILogger<RunJournalObserver> logger) : IAgentLifecycleObserver
{
    private readonly Lock gate = new();
    private readonly Dictionary<(string agentId, string runId), Task> pending = [];
    private readonly IAgentRunJournal journal = journal ?? throw new ArgumentNullException(nameof(journal));
    private readonly ILogger<RunJournalObserver> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public void OnTransition(AgentLifecycleTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        if (transition.ExecutionId is not { } runId || transition.Transition is not ("execute" or "complete"))
        {
            return;
        }

        var key = (agentId: transition.AgentId, runId);
        lock (this.gate)
        {
            var previous = this.pending.GetValueOrDefault(key, Task.CompletedTask);
            var write = this.WriteAfterAsync(previous, transition, runId);
            this.pending[key] = write;
            _ = write.ContinueWith(
                completed =>
                {
                    lock (this.gate)
                    {
                        if (this.pending.TryGetValue(key, out var latest) && ReferenceEquals(latest, completed))
                        {
                            this.pending.Remove(key);
                        }
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent {AgentId} run {RunId}: journaling {Transition} failed with {ExceptionType}")]
    private static partial void JournalWriteFailed(ILogger logger, string agentId, string runId, string transition, string exceptionType);

    // Synchronous journals still finish inline. Asynchronous writes are ordered per agent/run without
    // blocking the lifecycle or unrelated runs, and only unfinished chains stay in the dictionary.
    private async Task WriteAfterAsync(Task previous, AgentLifecycleTransition transition, string runId)
    {
        try
        {
            await previous.ConfigureAwait(false);

            // The lifecycle's caller may already be canceled; its run is written regardless.
            var write = transition.Transition == "execute"
                ? this.journal.StartedAsync(transition.AgentId, runId, transition.At, CancellationToken.None)
                : this.journal.CompletedAsync(transition.AgentId, runId, transition.At, AgentRunStatus.FromOutcome(transition.Outcome), CancellationToken.None);
            await write.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            JournalWriteFailed(this.logger, transition.AgentId, runId, transition.Transition, exception.GetType().Name);
        }
    }
}
