using HoneyDrunk.Agents.Operations.Runs;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HoneyDrunk.Agents.Operations.Tests;

/// <summary>The bounded run journal and the lifecycle observer that feeds it.</summary>
public sealed class RunJournalTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Each runtime outcome maps to one of the seven statuses; anything unknown is a failure.</summary>
    /// <param name="outcome">The runtime's outcome string.</param>
    /// <param name="expected">The status.</param>
    [Theory]
    [InlineData("Completed", "completed")]
    [InlineData("Rejected", "rejected")]
    [InlineData("AwaitingApproval", "awaitingapproval")]
    [InlineData("Halted", "halted")]
    [InlineData("Failed", "failed")]
    [InlineData("cancelled", "canceled")]
    [InlineData("propagated", "propagated")]
    [InlineData("error", "failed")]
    [InlineData("something-new", "failed")]
    [InlineData(null, "failed")]
    public void Outcome_maps_to_status(string? outcome, string expected)
    {
        Assert.Equal(expected, AgentRunStatus.FromOutcome(outcome));
        Assert.Contains(expected, AgentRunStatus.All);
    }

    /// <summary>The status set is exactly the seven v1 statuses.</summary>
    [Fact]
    public void All_holds_the_seven_statuses()
    {
        string[] expected = ["awaitingapproval", "canceled", "completed", "failed", "halted", "propagated", "rejected"];

        Assert.Equal(expected, AgentRunStatus.All.Order(StringComparer.Ordinal));
    }

    /// <summary>An execute transition starts a run; its complete transition finishes it with the mapped status.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Observer_records_start_then_completion()
    {
        var journal = new InMemoryAgentRunJournal();
        var observer = new RunJournalObserver(journal, new ListLogger<RunJournalObserver>());

        observer.OnTransition(new AgentLifecycleTransition("a", Id(1), "execute", Start, null));
        var running = await journal.GetAsync("a", Id(1), CancellationToken.None);
        observer.OnTransition(new AgentLifecycleTransition("a", Id(1), "complete", Start.AddSeconds(2), "Completed"));
        var finished = await journal.GetAsync("a", Id(1), CancellationToken.None);

        Assert.Equal(new AgentRunRecord(Id(1), "a", Start, null, null), running);
        Assert.Equal(new AgentRunRecord(Id(1), "a", Start, Start.AddSeconds(2), "completed"), finished);
    }

    /// <summary>Transitions other than execute and complete, and ones without an execution id, are not journaled.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Observer_ignores_other_transitions()
    {
        var journal = new InMemoryAgentRunJournal();
        var observer = new RunJournalObserver(journal, new ListLogger<RunJournalObserver>());

        observer.OnTransition(new AgentLifecycleTransition("a", null, "register", Start, null));
        observer.OnTransition(new AgentLifecycleTransition("a", null, "initialize", Start, null));
        observer.OnTransition(new AgentLifecycleTransition("a", null, "decommission", Start, null));
        observer.OnTransition(new AgentLifecycleTransition("a", null, "execute", Start, null));
        observer.OnTransition(new AgentLifecycleTransition("a", null, "complete", Start, "Completed"));

        Assert.Empty(await journal.RecentAsync("a", 10, null, finishedOnly: false, CancellationToken.None));
    }

    /// <summary>A run that has not completed is left out of a finished-only read, and present otherwise.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Running_run_is_excluded_when_finished_only()
    {
        var journal = new InMemoryAgentRunJournal();
        await Finish(journal, "a", 1);
        await journal.StartedAsync("a", Id(2), Start.AddMinutes(2), CancellationToken.None);

        var finished = await journal.RecentAsync("a", 10, null, finishedOnly: true, CancellationToken.None);
        var all = await journal.RecentAsync("a", 10, null, finishedOnly: false, CancellationToken.None);

        Assert.Equal([Id(1)], finished.Select(run => run.RunId));
        Assert.Equal([Id(2), Id(1)], all.Select(run => run.RunId));
    }

    /// <summary>Runs come newest first, and only the newest runs up to the cap are kept.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Newest_first_and_capped_per_agent()
    {
        var journal = new InMemoryAgentRunJournal(maxRunsPerAgent: 100);
        for (var number = 1; number <= 105; number++)
        {
            await Finish(journal, "a", number);
        }

        var runs = await journal.RecentAsync("a", 1000, null, finishedOnly: true, CancellationToken.None);

        Assert.Equal(Enumerable.Range(6, 100).Reverse().Select(Id), runs.Select(run => run.RunId));
        Assert.Null(await journal.GetAsync("a", Id(5), CancellationToken.None));
    }

    /// <summary>Runs that started at the same instant are ordered by run id, descending.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Same_instant_runs_order_by_id_descending()
    {
        var journal = new InMemoryAgentRunJournal();
        foreach (var number in new[] { 2, 3, 1 })
        {
            await journal.StartedAsync("a", Id(number), Start, CancellationToken.None);
        }

        var runs = await journal.RecentAsync("a", 10, null, finishedOnly: false, CancellationToken.None);

        Assert.Equal([Id(3), Id(2), Id(1)], runs.Select(run => run.RunId));
    }

    /// <summary>A cursor returns only runs older than the run it names, up to the limit.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Before_cursor_returns_older_runs()
    {
        var journal = new InMemoryAgentRunJournal();
        for (var number = 1; number <= 5; number++)
        {
            await Finish(journal, "a", number);
        }

        var page = await journal.RecentAsync("a", 2, Id(4), finishedOnly: true, CancellationToken.None);

        Assert.Equal([Id(3), Id(2)], page.Select(run => run.RunId));
    }

    /// <summary>A cursor that names no kept run returns nothing.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Unknown_cursor_returns_empty()
    {
        var journal = new InMemoryAgentRunJournal();
        await Finish(journal, "a", 1);

        Assert.Empty(await journal.RecentAsync("a", 10, Id(99), finishedOnly: true, CancellationToken.None));
    }

    /// <summary>One agent's runs never appear under another agent, and an unknown agent has none.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Agents_are_kept_apart()
    {
        var journal = new InMemoryAgentRunJournal();
        await Finish(journal, "a", 1);
        await Finish(journal, "b", 2);

        var runs = await journal.RecentAsync("a", 10, null, finishedOnly: true, CancellationToken.None);

        Assert.Equal([Id(1)], runs.Select(run => run.RunId));
        Assert.Null(await journal.GetAsync("a", Id(2), CancellationToken.None));
        Assert.Empty(await journal.RecentAsync("c", 10, null, finishedOnly: false, CancellationToken.None));
    }

    /// <summary>A single run is read by agent and run id; anything else is null.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Get_returns_one_run_or_null()
    {
        var journal = new InMemoryAgentRunJournal();
        await Finish(journal, "a", 1);

        var found = await journal.GetAsync("a", Id(1), CancellationToken.None);

        Assert.Equal(new AgentRunRecord(Id(1), "a", Start.AddMinutes(1), Start.AddMinutes(1).AddSeconds(1), "completed"), found);
        Assert.Null(await journal.GetAsync("a", Id(2), CancellationToken.None));
        Assert.Null(await journal.GetAsync("b", Id(1), CancellationToken.None));
    }

    /// <summary>A completion for a run the journal never saw start is kept whole, starting when it completed.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Completion_of_unseen_run_is_recorded_whole()
    {
        var journal = new InMemoryAgentRunJournal();

        await journal.CompletedAsync("a", Id(1), Start, "halted", CancellationToken.None);

        Assert.Equal(new AgentRunRecord(Id(1), "a", Start, Start, "halted"), await journal.GetAsync("a", Id(1), CancellationToken.None));
    }

    /// <summary>A repeated start or completion never duplicates a run or rewrites a finished one.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Repeated_writes_keep_the_first()
    {
        var journal = new InMemoryAgentRunJournal();
        await Finish(journal, "a", 1);

        await journal.StartedAsync("a", Id(1), Start.AddHours(1), CancellationToken.None);
        await journal.CompletedAsync("a", Id(1), Start.AddHours(2), "failed", CancellationToken.None);

        var run = Assert.Single(await journal.RecentAsync("a", 10, null, finishedOnly: false, CancellationToken.None));
        Assert.Equal(new AgentRunRecord(Id(1), "a", Start.AddMinutes(1), Start.AddMinutes(1).AddSeconds(1), "completed"), run);
    }

    /// <summary>A limit below one is refused.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Limit_below_one_is_refused()
    {
        var journal = new InMemoryAgentRunJournal();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => journal.RecentAsync("a", 0, null, finishedOnly: true, CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryAgentRunJournal(maxRunsPerAgent: 0));
    }

    /// <summary>
    /// While runs start and complete in parallel, no finished-only read holds a run twice or a run without its
    /// completion time and status. The readers read without pause from before the first of these writes until
    /// after the last, so reads and writes overlap.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Concurrent_completion_never_duplicates_or_tears()
    {
        var journal = new InMemoryAgentRunJournal(maxRunsPerAgent: 500);
        for (var number = 1; number <= 100; number++)
        {
            await Finish(journal, "a", number);
        }

        using var firstRead = new ManualResetEventSlim();
        using var written = new CancellationTokenSource();
        var readers = Enumerable.Range(1, 8).Select(_ => Task.Run(async () =>
        {
            var seen = new List<IReadOnlyList<AgentRunRecord>>();
            do
            {
                seen.Add(await journal.RecentAsync("a", 500, null, finishedOnly: true, CancellationToken.None));
                firstRead.Set();
            }
            while (!written.IsCancellationRequested);
            return seen;
        })).ToArray();
        var writers = Enumerable.Range(101, 100).Select(number => Task.Run(async () =>
        {
            // No write starts before a read has finished, so at least one read sees the journal part-written.
            firstRead.Wait();
            await journal.StartedAsync("a", Id(number), Start.AddMilliseconds(number % 7), CancellationToken.None);
            await Task.Yield();
            await journal.CompletedAsync("a", Id(number), Start.AddSeconds(1), "completed", CancellationToken.None);
        })).ToArray();

        await Task.WhenAll(writers);
        await written.CancelAsync();
        var reads = (await Task.WhenAll(readers))
            .SelectMany(seen => seen)
            .Append(await journal.RecentAsync("a", 500, null, finishedOnly: true, CancellationToken.None))
            .ToArray();

        Assert.Equal(200, reads[^1].Count);
        Assert.Contains(reads, read => read.Count is >= 100 and < 200);
        Assert.All(reads, read =>
        {
            Assert.Equal(read.Count, read.Select(run => run.RunId).Distinct(StringComparer.Ordinal).Count());
            Assert.All(read, run =>
            {
                Assert.NotNull(run.CompletedAt);
                Assert.NotNull(run.Status);
            });
        });
    }

    /// <summary>A journal that fails never throws into the lifecycle; the failure is logged by type name only.</summary>
    /// <param name="mode">How the journal fails.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData((int)FailingRunJournal.Mode.Throws)]
    [InlineData((int)FailingRunJournal.Mode.Faulted)]
    [InlineData((int)FailingRunJournal.Mode.Later)]
    public async Task Observer_swallows_journal_failure(int mode)
    {
        var journal = new FailingRunJournal((FailingRunJournal.Mode)mode);
        var logger = new ListLogger<RunJournalObserver>();
        var observer = new RunJournalObserver(journal, logger);

        observer.OnTransition(new AgentLifecycleTransition("a", Id(1), "execute", Start, null));
        journal.Pending.TrySetException(new InvalidOperationException("journal-secret"));
        await logger.Written.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var (level, message, exception) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("InvalidOperationException", message, StringComparison.Ordinal);
        Assert.DoesNotContain("journal-secret", message, StringComparison.Ordinal);
        Assert.Null(exception);
    }

    private static string Id(int number) => number.ToString("x32", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task Finish(InMemoryAgentRunJournal journal, string agentId, int number)
    {
        var started = Start.AddMinutes(number);
        await journal.StartedAsync(agentId, Id(number), started, CancellationToken.None);
        await journal.CompletedAsync(agentId, Id(number), started.AddSeconds(1), "completed", CancellationToken.None);
    }
}
