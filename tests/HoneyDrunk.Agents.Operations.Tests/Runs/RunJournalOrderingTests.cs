using HoneyDrunk.Agents.Operations.Runs;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HoneyDrunk.Agents.Operations.Tests.Runs;

/// <summary>Asynchronous persistence must preserve lifecycle ordering without stalling other agents.</summary>
public sealed class RunJournalOrderingTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A completion waits for its actual start to persist; another agent with the same run id remains independent.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Completion_waits_for_start_without_blocking_other_agents()
    {
        var journal = new GatedRunJournal();
        var observer = new RunJournalObserver(journal, new ListLogger<RunJournalObserver>());
        observer.OnTransition(new AgentLifecycleTransition("a", "run", "execute", Start, null));
        observer.OnTransition(new AgentLifecycleTransition("a", "run", "complete", Start.AddSeconds(2), "Completed"));
        observer.OnTransition(new AgentLifecycleTransition("b", "run", "execute", Start.AddSeconds(1), null));
        observer.OnTransition(new AgentLifecycleTransition("b", "run", "complete", Start.AddSeconds(3), "Halted"));

        Assert.False(journal.CompletionCalled);
        Assert.Null(await journal.GetAsync("a", "run", CancellationToken.None));
        Assert.Equal(
            new AgentRunRecord("run", "b", Start.AddSeconds(1), Start.AddSeconds(3), "halted"),
            await journal.GetAsync("b", "run", CancellationToken.None));

        journal.ReleaseStart.SetResult();
        await journal.Completion.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(
            new AgentRunRecord("run", "a", Start, Start.AddSeconds(2), "completed"),
            await journal.GetAsync("a", "run", CancellationToken.None));
    }

    /// <summary>A failed or canceled start is observed safely and does not prevent the later completion.</summary>
    /// <param name="cancel">Whether to cancel rather than fault the pending start.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_start_does_not_drop_completion(bool cancel)
    {
        var journal = new GatedRunJournal();
        var logger = new ListLogger<RunJournalObserver>();
        var observer = new RunJournalObserver(journal, logger);
        observer.OnTransition(new AgentLifecycleTransition("a", "run", "execute", Start, null));
        observer.OnTransition(new AgentLifecycleTransition("a", "run", "complete", Start.AddSeconds(2), "Completed"));
        Assert.False(journal.CompletionCalled);

        if (cancel)
        {
            journal.ReleaseStart.SetCanceled();
        }
        else
        {
            journal.ReleaseStart.SetException(new InvalidOperationException("journal-secret"));
        }

        await journal.Completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(
            new AgentRunRecord("run", "a", Start.AddSeconds(2), Start.AddSeconds(2), "completed"),
            await journal.GetAsync("a", "run", CancellationToken.None));
        var (level, message, exception) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains(cancel ? "TaskCanceledException" : "InvalidOperationException", message, StringComparison.Ordinal);
        Assert.DoesNotContain("journal-secret", message, StringComparison.Ordinal);
        Assert.Null(exception);
    }
}
