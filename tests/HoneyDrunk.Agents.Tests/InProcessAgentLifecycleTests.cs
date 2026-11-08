using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Capabilities.Testing;
using HoneyDrunk.Kernel.Abstractions.Agents;
using HoneyDrunk.Kernel.Abstractions.Identity;
using HoneyDrunk.Operator.Abstractions;
using Xunit;

namespace HoneyDrunk.Agents.Tests;

/// <summary>The in-process lifecycle: phases, pre-run checks, execution, cleanup and failure handling.</summary>
public sealed class InProcessAgentLifecycleTests
{
    private const string Sentinel = "sk-secret-value";

    /// <summary>Ids must be valid and unique.</summary>
    [Fact]
    public void Register_rejects_duplicate_and_bad_ids()
    {
        using var fixture = new LifecycleFixture();
        fixture.Lifecycle.Register(new AgentRegistration(new TestAgent(), AgentGrants.None, []));

        Assert.Throws<InvalidOperationException>(() => fixture.Lifecycle.Register(new AgentRegistration(new TestAgent(), AgentGrants.None, [])));
        Assert.Throws<ArgumentException>(() => fixture.Lifecycle.Register(new AgentRegistration(new BadIdAgent(), AgentGrants.None, [])));
        Assert.Equal(AgentLifecyclePhase.Registered, fixture.Lifecycle.GetPhase(InvokerFixture.AgentId));
    }

    /// <summary>Initialization checks every binding exists.</summary>
    [Fact]
    public async Task Initialize_fails_on_unregistered_binding()
    {
        using var fixture = new LifecycleFixture();
        fixture.Lifecycle.Register(new AgentRegistration(new TestAgent(), AgentGrants.None, [new("missing-tool", "1.0.0")]));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Lifecycle.InitializeAsync(InvokerFixture.AgentId));

        Assert.Contains("missing-tool@1.0.0", error.Message, StringComparison.Ordinal);
        Assert.Equal(AgentLifecyclePhase.Registered, fixture.Lifecycle.GetPhase(InvokerFixture.AgentId));
    }

    /// <summary>An agent must be initialized before it runs.</summary>
    [Fact]
    public async Task Execute_before_initialize_throws_naming_phase()
    {
        using var fixture = new LifecycleFixture();
        fixture.Lifecycle.Register(new AgentRegistration(new TestAgent(), AgentGrants.None, []));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions()));

        Assert.Contains(nameof(AgentLifecyclePhase.Registered), error.Message, StringComparison.Ordinal);
    }

    /// <summary>A decommissioned agent refuses executions.</summary>
    [Fact]
    public async Task Execute_after_decommission_throws()
    {
        using var fixture = new LifecycleFixture();
        await fixture.ReadyAsync();
        await fixture.Lifecycle.DecommissionAsync(InvokerFixture.AgentId);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions()));

        Assert.Contains(nameof(AgentLifecyclePhase.Decommissioned), error.Message, StringComparison.Ordinal);
    }

    /// <summary>No run without an initialized grid context.</summary>
    [Fact]
    public async Task Uninitialized_grid_context_fails_without_running_agent()
    {
        using var fixture = new LifecycleFixture(gridInitialized: false);
        var agent = await fixture.ReadyAsync();

        var result = await fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions());

        Assert.Equal(AgentOutcome.Failed, result.Outcome);
        Assert.Equal("grid_context_missing", result.ReasonCode);
        Assert.Equal(0, agent.Runs);
    }

    /// <summary>The memory tenant must be the grid context's tenant.</summary>
    [Fact]
    public async Task Tenant_mismatch_rejects()
    {
        using var fixture = new LifecycleFixture();
        var agent = await fixture.ReadyAsync();
        var options = new AgentExecutionOptions(new AgentMemoryBinding(TenantId.NewId(), MemoryFixture.ProjectA, []));

        var result = await fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), options);

        Assert.Equal(AgentOutcome.Rejected, result.Outcome);
        Assert.Equal("tenant_mismatch", result.ReasonCode);
        Assert.Equal(0, agent.Runs);
    }

    /// <summary>An open breaker halts before the agent runs.</summary>
    [Fact]
    public async Task Open_breaker_halts_before_agent_runs()
    {
        using var fixture = new LifecycleFixture();
        var agent = await fixture.ReadyAsync();
        await fixture.Breaker.TripAsync("agent:" + InvokerFixture.AgentId, "test");

        var result = await fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions());

        Assert.Equal(AgentOutcome.Halted, result.Outcome);
        Assert.Equal("breaker_open", result.ReasonCode);
        Assert.Equal(0, agent.Runs);
    }

    /// <summary>A denied memory escalation rejects before the agent runs.</summary>
    [Fact]
    public async Task Denied_memory_escalation_rejects_before_agent_runs()
    {
        using var fixture = new LifecycleFixture();
        var agent = await fixture.ReadyAsync();

        var result = await fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions(null, MemoryFixture.ProjectB));

        Assert.Equal(AgentOutcome.Rejected, result.Outcome);
        Assert.Equal("memory_escalation_denied", result.ReasonCode);
        Assert.Equal(0, agent.Runs);
    }

    /// <summary>A narrowing drops what was never granted.</summary>
    [Fact]
    public async Task Narrowing_never_widens_effective_grants()
    {
        using var fixture = new LifecycleFixture();
        var agent = await fixture.ReadyAsync();

        await fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions(new AgentGrants([CapabilityFixtures.Scope, "admin.all"], ["root"])));

        var context = Assert.Single(agent.Contexts);
        Assert.Equal([CapabilityFixtures.Scope], context.Grants.Scopes);
        Assert.Empty(context.Grants.Roles);
    }

    /// <summary>The context's tools and memory are unusable after the execution.</summary>
    [Fact]
    public async Task Context_is_disposed_after_completion()
    {
        using var fixture = new LifecycleFixture();
        var agent = await fixture.ReadyAsync();

        var result = await fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions());

        Assert.Equal(AgentOutcome.Completed, result.Outcome);
        var context = Assert.Single(agent.Contexts);
        Assert.Throws<ObjectDisposedException>(() => context.Tools);
        Assert.Throws<ObjectDisposedException>(() => context.Memory);
        var operation = Assert.Single(fixture.ContextFactory.Operations);
        Assert.True(operation.IsSuccess);
    }

    /// <summary>An agent can use its tools and memory while it runs.</summary>
    [Fact]
    public async Task Agent_uses_tools_and_memory_while_running()
    {
        using var fixture = new LifecycleFixture();
        await fixture.ReadyAsync(new TestAgent(async (context, token) =>
        {
            await context.Memory.RememberAsync(new AgentMemoryNote("note", "hello", []), token);
            var recalled = await context.Memory.RecallAsync(new AgentRecall(), token);
            var tool = await context.Tools.InvokeAsync(InvokerFixture.Call(), token);
            return new AgentResult(AgentOutcome.Completed, InvokerFixture.Json($$"""{"recalled":{{recalled.Count}},"tool":"{{tool.Status}}"}"""), null, null);
        }));

        var result = await fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions());

        Assert.Equal(AgentOutcome.Completed, result.Outcome);
        Assert.Equal(1, result.Output!.Value.GetProperty("recalled").GetInt32());
        Assert.Equal("Succeeded", result.Output.Value.GetProperty("tool").GetString());
        Assert.Single(fixture.Handler.Invocations);
    }

    /// <summary>An agent exception becomes a code; its message goes nowhere.</summary>
    [Fact]
    public async Task Agent_exception_becomes_agent_error_without_message()
    {
        using var fixture = new LifecycleFixture();
        await fixture.ReadyAsync(new TestAgent((_, _) => throw new InvalidOperationException(Sentinel)));

        var result = await fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions());

        Assert.Equal(AgentOutcome.Failed, result.Outcome);
        Assert.Equal("agent_error", result.ReasonCode);
        Assert.DoesNotContain(Sentinel, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Log.Entries, entry => entry.message.Contains(Sentinel, StringComparison.Ordinal));
        var operation = Assert.Single(fixture.ContextFactory.Operations);
        Assert.False(operation.IsSuccess);
        Assert.DoesNotContain(Sentinel, operation.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>A listed stop exception reaches the caller unchanged after cleanup.</summary>
    [Fact]
    public async Task Listed_exception_propagates_unchanged()
    {
        using var fixture = new LifecycleFixture(options => options.PropagatedExceptionTypes.Add(typeof(StopException)));
        var stop = new StopException(Sentinel);
        var agent = await fixture.ReadyAsync(new TestAgent((_, _) => throw stop));

        var thrown = await Assert.ThrowsAsync<StopException>(() => fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions()));

        Assert.Same(stop, thrown);
        Assert.Equal(1, agent.Runs);
        Assert.Throws<ObjectDisposedException>(() => agent.Contexts[0].Tools);
        Assert.Contains(fixture.Observer.Transitions, transition => transition.Transition == "complete" && transition.Outcome == "propagated");
        var operation = Assert.Single(fixture.ContextFactory.Operations);
        Assert.False(operation.IsSuccess);
        Assert.Equal(nameof(StopException), operation.ErrorMessage);
    }

    /// <summary>A type derived from a listed one also propagates.</summary>
    [Fact]
    public async Task Derived_listed_exception_propagates()
    {
        using var fixture = new LifecycleFixture(options => options.PropagatedExceptionTypes.Add(typeof(StopException)));
        await fixture.ReadyAsync(new TestAgent((_, _) => throw new HardStopException()));

        await Assert.ThrowsAsync<HardStopException>(() => fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions()));
    }

    /// <summary>Exceptions not listed are still reported as agent errors.</summary>
    [Fact]
    public async Task Unlisted_exception_still_becomes_agent_error()
    {
        using var fixture = new LifecycleFixture(options => options.PropagatedExceptionTypes.Add(typeof(StopException)));
        await fixture.ReadyAsync(new TestAgent((_, _) => throw new InvalidOperationException()));

        var result = await fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions());

        Assert.Equal("agent_error", result.ReasonCode);
    }

    /// <summary>Only exception types may be listed.</summary>
    [Fact]
    public void Non_exception_type_in_list_throws()
    {
        Assert.Throws<ArgumentException>(() => new LifecycleFixture(options => options.PropagatedExceptionTypes.Add(typeof(string))));
    }

    /// <summary>An execution that runs past its timeout fails with a timeout code, even if the agent ignores cancellation.</summary>
    [Fact]
    public async Task Timeout_fails_with_timeout_code()
    {
        using var fixture = new LifecycleFixture();
        var started = new TaskCompletionSource();
        var never = new TaskCompletionSource<AgentResult>();
        await fixture.ReadyAsync(new TestAgent((_, _) =>
        {
            started.SetResult();
            return never.Task;
        }));

        var execution = fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions());
        await started.Task;
        fixture.Memory.Time.Advance(fixture.Options.ExecutionTimeout + TimeSpan.FromSeconds(1));
        var result = await execution;

        Assert.Equal(AgentOutcome.Failed, result.Outcome);
        Assert.Equal("timeout", result.ReasonCode);
    }

    /// <summary>Caller cancellation propagates.</summary>
    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        using var fixture = new LifecycleFixture();
        using var cancellation = new CancellationTokenSource();
        await fixture.ReadyAsync(new TestAgent(async (_, token) =>
        {
            await cancellation.CancelAsync();
            token.ThrowIfCancellationRequested();
            return new AgentResult(AgentOutcome.Completed, null, null, null);
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions(), cancellation.Token));
    }

    /// <summary>Observers see each transition in order.</summary>
    [Fact]
    public async Task Observers_see_transitions_in_order()
    {
        using var fixture = new LifecycleFixture();
        await fixture.ReadyAsync();

        await fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions());
        await fixture.Lifecycle.DecommissionAsync(InvokerFixture.AgentId);

        Assert.Equal(["register", "initialize", "execute", "complete", "decommission"], fixture.Observer.Transitions.Select(transition => transition.Transition));
        var complete = fixture.Observer.Transitions[3];
        Assert.Equal(nameof(AgentOutcome.Completed), complete.Outcome);
        Assert.Equal(fixture.Observer.Transitions[2].ExecutionId, complete.ExecutionId);
    }

    /// <summary>An observer failure never breaks an execution.</summary>
    [Fact]
    public async Task Failing_observer_does_not_break_execution()
    {
        using var fixture = new LifecycleFixture();
        await fixture.ReadyAsync();
        fixture.Observer.Throw = true;

        var result = await fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions());

        Assert.Equal(AgentOutcome.Completed, result.Outcome);
    }

    /// <summary>The policy sees the narrowed agent through tool calls.</summary>
    [Fact]
    public async Task Tool_calls_use_the_execution_identity()
    {
        using var fixture = new LifecycleFixture();
        fixture.Policy.Outcome = PolicyOutcome.Allow;
        await fixture.ReadyAsync(new TestAgent(async (context, token) =>
        {
            await context.Tools.InvokeAsync(InvokerFixture.Call(), token);
            return new AgentResult(AgentOutcome.Completed, null, null, null);
        }));

        await fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions());

        var asked = Assert.Single(fixture.Policy.Asked);
        Assert.Equal(fixture.Observer.Transitions.Single(transition => transition.Transition == "execute").ExecutionId, asked.Attributes["executionId"]);
        Assert.Equal("corr-1", asked.Attributes["correlationId"]);
    }

    /// <summary>A breaker that fails before the run refuses with a code instead of escaping.</summary>
    [Fact]
    public async Task Breaker_failure_before_run_refuses_with_gate_error()
    {
        using var fixture = new LifecycleFixture();
        var agent = await fixture.ReadyAsync();
        fixture.Breaker.Throw = new InvalidOperationException(Sentinel);

        var result = await fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions());

        Assert.Equal(AgentOutcome.Failed, result.Outcome);
        Assert.Equal("gate_error", result.ReasonCode);
        Assert.Equal(0, agent.Runs);
    }

    /// <summary>The registered id is used even if the agent's descriptor changes later.</summary>
    [Fact]
    public async Task Execution_uses_the_registered_id()
    {
        using var fixture = new LifecycleFixture();
        var agent = new ShiftingAgent();
        fixture.Lifecycle.Register(new AgentRegistration(agent, AgentGrants.None, []));
        await fixture.Lifecycle.InitializeAsync(InvokerFixture.AgentId);
        await fixture.Breaker.TripAsync("agent:" + InvokerFixture.AgentId, "test");
        agent.Shift();

        var result = await fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions());

        Assert.Equal(AgentOutcome.Halted, result.Outcome);
    }

    /// <summary>Out-of-range options are refused when the lifecycle is built.</summary>
    /// <param name="which">The option to break.</param>
    [Theory]
    [InlineData("calls")]
    [InlineData("timeout")]
    [InlineData("ttl")]
    public void Invalid_options_are_refused(string which)
    {
        Assert.Throws<ArgumentException>(() => new LifecycleFixture(options =>
        {
            switch (which)
            {
                case "calls":
                    options.MaxToolCallsPerExecution = 0;
                    break;
                case "timeout":
                    options.ExecutionTimeout = TimeSpan.FromSeconds(-1);
                    break;
                default:
                    options.ToolApprovalTtl = TimeSpan.Zero;
                    break;
            }
        }));
    }

    /// <summary>Decommissioning twice notifies once.</summary>
    [Fact]
    public async Task Decommission_twice_notifies_once()
    {
        using var fixture = new LifecycleFixture();
        await fixture.ReadyAsync();

        await fixture.Lifecycle.DecommissionAsync(InvokerFixture.AgentId);
        await fixture.Lifecycle.DecommissionAsync(InvokerFixture.AgentId);

        Assert.Single(fixture.Observer.Transitions, transition => transition.Transition == "decommission");
    }

    /// <summary>A null result fails the operation.</summary>
    [Fact]
    public async Task Null_result_fails_the_operation()
    {
        using var fixture = new LifecycleFixture();
        await fixture.ReadyAsync(new TestAgent((_, _) => Task.FromResult<AgentResult>(null!)));

        var result = await fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions());

        Assert.Equal("agent_error", result.ReasonCode);
        Assert.False(Assert.Single(fixture.ContextFactory.Operations).IsSuccess);
    }

    /// <summary>The execution deadline also covers the pre-run checks.</summary>
    [Fact]
    public async Task Deadline_covers_pre_run_checks()
    {
        using var fixture = new LifecycleFixture();
        var agent = await fixture.ReadyAsync();
        fixture.Breaker.WaitUntilCancelled = true;

        var execution = fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions());
        await fixture.Breaker.Entered.Task;
        fixture.Memory.Time.Advance(fixture.Options.ExecutionTimeout + TimeSpan.FromSeconds(1));
        var result = await execution;

        Assert.Equal(AgentOutcome.Failed, result.Outcome);
        Assert.Equal("timeout", result.ReasonCode);
        Assert.Equal(0, agent.Runs);
    }

    /// <summary>A returned failure fails the Kernel operation too; only a completed run completes it.</summary>
    /// <param name="outcome">The outcome the agent returns.</param>
    /// <param name="succeeded">Whether the operation should complete successfully.</param>
    [Theory]
    [InlineData(AgentOutcome.Completed, true)]
    [InlineData(AgentOutcome.Failed, false)]
    [InlineData(AgentOutcome.Halted, false)]
    [InlineData(AgentOutcome.Rejected, false)]
    public async Task Operation_status_agrees_with_the_returned_outcome(AgentOutcome outcome, bool succeeded)
    {
        using var fixture = new LifecycleFixture();
        await fixture.ReadyAsync(new TestAgent((_, _) => Task.FromResult(new AgentResult(outcome, null, "agent_code", null))));

        await fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions());

        Assert.Equal(succeeded, Assert.Single(fixture.ContextFactory.Operations).IsSuccess);
    }

    /// <summary>A pre-run check that ignores cancellation still ends at the deadline.</summary>
    [Fact]
    public async Task Deadline_bounds_a_check_that_ignores_cancellation()
    {
        using var fixture = new LifecycleFixture();
        var agent = await fixture.ReadyAsync();
        fixture.Breaker.Hang = true;

        var execution = fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions());
        await fixture.Breaker.Entered.Task;
        fixture.Memory.Time.Advance(fixture.Options.ExecutionTimeout + TimeSpan.FromSeconds(1));
        var result = await execution;

        Assert.Equal("timeout", result.ReasonCode);
        Assert.Equal(0, agent.Runs);
    }

    /// <summary>An agent runs one execution at a time; an overlapping one is refused.</summary>
    [Fact]
    public async Task Overlapping_execution_is_refused()
    {
        using var fixture = new LifecycleFixture();
        var release = new TaskCompletionSource<AgentResult>();
        var started = new TaskCompletionSource();
        await fixture.ReadyAsync(new TestAgent((_, _) =>
        {
            started.TrySetResult();
            return release.Task;
        }));

        var first = fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions());
        await started.Task;
        var second = await fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions());
        release.SetResult(new AgentResult(AgentOutcome.Completed, null, null, null));
        var firstResult = await first;

        Assert.Equal(AgentOutcome.Rejected, second.Outcome);
        Assert.Equal("agent_busy", second.ReasonCode);
        Assert.Equal(AgentOutcome.Completed, firstResult.Outcome);
    }

    /// <summary>An agent abandoned at its deadline keeps its slot until its task really ends.</summary>
    [Fact]
    public async Task Abandoned_agent_holds_its_slot_until_it_ends()
    {
        using var fixture = new LifecycleFixture();
        var stuck = new TaskCompletionSource<AgentResult>();
        var calls = 0;
        await fixture.ReadyAsync(new TestAgent((_, _) => ++calls == 1
            ? stuck.Task
            : Task.FromResult(new AgentResult(AgentOutcome.Completed, null, null, null))));

        var first = fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions());
        fixture.Memory.Time.Advance(fixture.Options.ExecutionTimeout + TimeSpan.FromSeconds(1));
        Assert.Equal("timeout", (await first).ReasonCode);
        var whileStuck = await fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions());
        stuck.SetResult(new AgentResult(AgentOutcome.Completed, null, null, null));
        var afterwards = await fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions());

        Assert.Equal("agent_busy", whileStuck.ReasonCode);
        Assert.Equal(AgentOutcome.Completed, afterwards.Outcome);
    }

    /// <summary>An agent still running after its caller cancelled keeps its slot until its task ends.</summary>
    [Fact]
    public async Task Cancelled_but_running_agent_holds_its_slot()
    {
        using var fixture = new LifecycleFixture();
        using var cancellation = new CancellationTokenSource();
        var stuck = new TaskCompletionSource<AgentResult>();
        var started = new TaskCompletionSource();
        var calls = 0;
        await fixture.ReadyAsync(new TestAgent((_, _) =>
        {
            if (++calls > 1)
            {
                return Task.FromResult(new AgentResult(AgentOutcome.Completed, null, null, null));
            }

            started.SetResult();
            return stuck.Task;
        }));

        var first = fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions(), cancellation.Token);
        await started.Task;
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var whileStuck = await fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions());
        stuck.SetResult(new AgentResult(AgentOutcome.Completed, null, null, null));
        var afterwards = await fixture.Lifecycle.ExecuteAsync(InvokerFixture.AgentId, LifecycleFixture.Request(), LifecycleFixture.ExecutionOptions());

        Assert.Equal("agent_busy", whileStuck.ReasonCode);
        Assert.Equal(AgentOutcome.Completed, afterwards.Outcome);
    }

    private class StopException(string message = "stop") : Exception(message);

    private sealed class ShiftingAgent : IAgent
    {
        private IAgentDescriptor descriptor = new AgentDescriptor(InvokerFixture.AgentId, "Shifting", "test", "1.0.0");

        public IAgentDescriptor Descriptor => this.descriptor;

        public void Shift() => this.descriptor = new AgentDescriptor("someone-else", "Shifting", "test", "1.0.0");

        public Task<AgentResult> ExecuteAsync(IAgentRunContext context, AgentRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new AgentResult(AgentOutcome.Completed, null, null, null));
    }

    private sealed class HardStopException() : StopException("hard stop");

    private sealed class BadIdAgent : IAgent
    {
        public IAgentDescriptor Descriptor { get; } = new BadDescriptor();

        public Task<AgentResult> ExecuteAsync(IAgentRunContext context, AgentRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new AgentResult(AgentOutcome.Completed, null, null, null));
    }

    private sealed class BadDescriptor : IAgentDescriptor
    {
        public string AgentId => "Bad Id";

        public string Name => "Bad";

        public string AgentType => "bad";

        public string Version => "1.0.0";

        public IReadOnlyList<IAgentCapability> Capabilities => [];

        public AgentContextScope ContextScope => AgentContextScope.Standard;

        public IReadOnlyDictionary<string, string> Metadata => new Dictionary<string, string>();

        public bool HasCapability(string capabilityName) => false;
    }
}
