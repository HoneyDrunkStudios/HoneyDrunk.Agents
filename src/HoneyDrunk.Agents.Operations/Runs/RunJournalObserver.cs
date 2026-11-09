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

        Task write;
        try
        {
            // The lifecycle's caller may already be canceled; the record of its run is written regardless.
            write = transition.Transition == "execute"
                ? this.journal.StartedAsync(transition.AgentId, runId, transition.At, CancellationToken.None)
                : this.journal.CompletedAsync(transition.AgentId, runId, transition.At, AgentRunStatus.FromOutcome(transition.Outcome), CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            JournalWriteFailed(this.logger, transition.AgentId, runId, transition.Transition, exception.GetType().Name);
            return;
        }

        this.Observe(write, transition.AgentId, runId, transition.Transition);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent {AgentId} run {RunId}: journaling {Transition} failed with {ExceptionType}")]
    private static partial void JournalWriteFailed(ILogger logger, string agentId, string runId, string transition, string exceptionType);

    private static string FaultName(Task write) => write.Exception?.InnerException?.GetType().Name ?? "unknown";

    // The lifecycle calls synchronously, so a write that is still running is observed, never awaited.
    private void Observe(Task? write, string agentId, string runId, string transition)
    {
        if (write is null || write.IsCompletedSuccessfully)
        {
            return;
        }

        if (write.IsFaulted)
        {
            JournalWriteFailed(this.logger, agentId, runId, transition, FaultName(write));
            return;
        }

        _ = write.ContinueWith(
            task => JournalWriteFailed(this.logger, agentId, runId, transition, FaultName(task)),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
