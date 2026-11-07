using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Capabilities.Abstractions;
using HoneyDrunk.Capabilities.Testing;
using HoneyDrunk.Operator.Abstractions;
using Xunit;

namespace HoneyDrunk.Agents.Tests;

/// <summary>Tool calls go through bindings, Operator's gates and the Capabilities invoker, in that order.</summary>
public sealed class CapabilityToolInvokerTests
{
    private const string Sentinel = "sk-secret-value";

    private static readonly string[] AllowedTagKeys =
    [
        "agent.id", "agent.execution_id", "agent.transition", "agent.outcome", "tool.name", "tool.version", "tool.status",
        "memory.operation", "memory.result_count", "gate.kind", "gate.outcome",
    ];

    /// <summary>A tool outside the bindings is refused before anything else is asked.</summary>
    [Fact]
    public async Task Unbound_tool_returns_not_bound_and_calls_nothing()
    {
        var fixture = new InvokerFixture();
        using var invoker = fixture.Create();

        var result = await invoker.InvokeAsync(InvokerFixture.Call(name: "other-tool"));

        Assert.Equal(ToolCallStatus.NotBound, result.Status);
        Assert.Equal("not_bound", result.ErrorCode);
        Assert.Empty(fixture.Policy.Asked);
        Assert.Empty(fixture.Capabilities.Guard.Requests);
        Assert.Empty(fixture.Handler.Invocations);
    }

    /// <summary>A bound name at another version is not bound.</summary>
    [Fact]
    public async Task Bound_name_at_other_version_is_not_bound()
    {
        var fixture = new InvokerFixture();
        using var invoker = fixture.Create();

        var result = await invoker.InvokeAsync(InvokerFixture.Call(version: "2.0.0"));

        Assert.Equal(ToolCallStatus.NotBound, result.Status);
        Assert.Empty(fixture.Policy.Asked);
    }

    /// <summary>A bound tool that is not registered is not found.</summary>
    [Fact]
    public async Task Unregistered_tool_returns_not_found()
    {
        var fixture = new InvokerFixture { Bindings = [new("missing-tool", "1.0.0")] };
        using var invoker = fixture.Create();

        var result = await invoker.InvokeAsync(InvokerFixture.Call(name: "missing-tool"));

        Assert.Equal(ToolCallStatus.NotFound, result.Status);
        Assert.Equal("not_found", result.ErrorCode);
        Assert.Empty(fixture.Policy.Asked);
    }

    /// <summary>An open breaker stops the call before policy or dispatch.</summary>
    [Fact]
    public async Task Open_breaker_halts_before_dispatch()
    {
        var fixture = new InvokerFixture();
        await fixture.Breaker.TripAsync("agent:" + InvokerFixture.AgentId, "test");
        using var invoker = fixture.Create();

        var result = await invoker.InvokeAsync(InvokerFixture.Call());

        Assert.Equal(ToolCallStatus.Halted, result.Status);
        Assert.Equal("breaker_open", result.ErrorCode);
        Assert.Empty(fixture.Policy.Asked);
        Assert.Empty(fixture.Handler.Invocations);
    }

    /// <summary>A policy deny never dispatches.</summary>
    [Fact]
    public async Task Policy_deny_never_dispatches()
    {
        var fixture = new InvokerFixture();
        fixture.Policy.Outcome = PolicyOutcome.Deny;
        using var invoker = fixture.Create();

        var result = await invoker.InvokeAsync(InvokerFixture.Call());

        Assert.Equal(ToolCallStatus.Denied, result.Status);
        Assert.Equal("operator_denied", result.ErrorCode);
        Assert.Empty(fixture.Handler.Invocations);
    }

    /// <summary>The policy is asked with the pinned action context.</summary>
    [Fact]
    public async Task Policy_is_asked_with_the_pinned_context()
    {
        var fixture = new InvokerFixture();
        using var invoker = fixture.Create();

        await invoker.InvokeAsync(InvokerFixture.Call());

        var asked = Assert.Single(fixture.Policy.Asked);
        Assert.Equal(InvokerFixture.AgentId, asked.ActorId);
        Assert.Equal("tool.test-tool", asked.Action);
        Assert.Equal("capability/test-tool/1.0.0", asked.Resource);
        Assert.Equal(["correlationId", "executionId"], asked.Attributes.Keys.Order(StringComparer.Ordinal));
    }

    /// <summary>A pending approval waits without dispatching.</summary>
    [Fact]
    public async Task Require_approval_with_pending_gate_awaits()
    {
        var fixture = new InvokerFixture();
        fixture.Policy.Outcome = PolicyOutcome.RequireApproval;
        using var invoker = fixture.Create();

        var result = await invoker.InvokeAsync(InvokerFixture.Call());

        Assert.Equal(ToolCallStatus.AwaitingApproval, result.Status);
        Assert.NotNull(result.ApprovalId);
        Assert.Empty(fixture.Handler.Invocations);
        var request = Assert.Single(fixture.Gate.Received);
        Assert.Equal(result.ApprovalId, request.ApprovalId);
        Assert.Equal("agent:" + InvokerFixture.AgentId, request.Subject);
        Assert.Equal("tool.test-tool", request.Action);
        Assert.Equal("capability/test-tool/1.0.0", request.RequestedScope);
        Assert.Equal(fixture.Time.GetUtcNow() + fixture.Options.ToolApprovalTtl, request.Expiry);
        Assert.Equal("corr-1", request.RequesterCorrelationId);
    }

    /// <summary>Denied and expired replies never dispatch.</summary>
    /// <param name="outcome">The gate's reply.</param>
    /// <param name="status">The expected status.</param>
    [Theory]
    [InlineData(ApprovalOutcome.Denied, ToolCallStatus.Denied)]
    [InlineData(ApprovalOutcome.Expired, ToolCallStatus.AwaitingApproval)]
    public async Task Denied_and_expired_approvals_never_dispatch(ApprovalOutcome outcome, ToolCallStatus status)
    {
        var fixture = new InvokerFixture();
        fixture.Policy.Outcome = PolicyOutcome.RequireApproval;
        fixture.Gate.DefaultOutcome = outcome;
        using var invoker = fixture.Create();

        var result = await invoker.InvokeAsync(InvokerFixture.Call());

        Assert.Equal(status, result.Status);
        Assert.Empty(fixture.Handler.Invocations);
        Assert.Single(fixture.Gate.Received);
    }

    /// <summary>An approval authorizes one dispatch; the next identical call asks again under a new id.</summary>
    [Fact]
    public async Task Approved_call_dispatches_once_then_needs_new_approval()
    {
        var fixture = new InvokerFixture();
        fixture.Policy.Outcome = PolicyOutcome.RequireApproval;
        using var first = fixture.Create("exec-1");
        var waiting = await first.InvokeAsync(InvokerFixture.Call());
        fixture.Gate.Decide(waiting.ApprovalId!, ApprovalOutcome.Approved);

        using var second = fixture.Create("exec-2");
        var dispatched = await second.InvokeAsync(InvokerFixture.Call());
        using var third = fixture.Create("exec-3");
        var again = await third.InvokeAsync(InvokerFixture.Call());

        Assert.Equal(ToolCallStatus.Succeeded, dispatched.Status);
        Assert.Single(fixture.Handler.Invocations);
        Assert.Equal(ToolCallStatus.AwaitingApproval, again.Status);
        Assert.NotEqual(waiting.ApprovalId, again.ApprovalId);
        Assert.Equal(2, fixture.Gate.Received.Count);
    }

    /// <summary>Two separately approved identical calls both dispatch.</summary>
    [Fact]
    public async Task Separately_approved_identical_calls_both_dispatch()
    {
        var fixture = new InvokerFixture();
        fixture.Policy.Outcome = PolicyOutcome.RequireApproval;
        fixture.Gate.DefaultOutcome = ApprovalOutcome.Approved;
        using var first = fixture.Create("exec-1");
        using var second = fixture.Create("exec-2");

        var one = await first.InvokeAsync(InvokerFixture.Call());
        var two = await second.InvokeAsync(InvokerFixture.Call());

        Assert.Equal(ToolCallStatus.Succeeded, one.Status);
        Assert.Equal(ToolCallStatus.Succeeded, two.Status);
        Assert.Equal(2, fixture.Handler.Invocations.Count);
        var requests = fixture.Gate.Received;
        Assert.Equal(2, requests.Count);
        Assert.NotEqual(requests[0].ApprovalId, requests[1].ApprovalId);
        Assert.Equal(requests[0].Context["requestKey"], requests[1].Context["requestKey"]);
    }

    /// <summary>A pending approval keeps one id across proposals.</summary>
    [Fact]
    public async Task Pending_approval_keeps_its_id_across_proposals()
    {
        var fixture = new InvokerFixture();
        fixture.Policy.Outcome = PolicyOutcome.RequireApproval;
        using var first = fixture.Create("exec-1");
        using var second = fixture.Create("exec-2");

        var one = await first.InvokeAsync(InvokerFixture.Call());
        var two = await second.InvokeAsync(InvokerFixture.Call());

        Assert.Single(fixture.Gate.Received);
        Assert.Equal(one.ApprovalId, two.ApprovalId);
        Assert.Equal(ToolCallStatus.AwaitingApproval, two.Status);
    }

    /// <summary>Concurrent proposals consume one approval at most once.</summary>
    [Fact]
    public async Task Concurrent_calls_consume_one_approval_once()
    {
        var fixture = new InvokerFixture();
        fixture.Policy.Outcome = PolicyOutcome.RequireApproval;
        using (var first = fixture.Create("exec-0"))
        {
            var waiting = await first.InvokeAsync(InvokerFixture.Call());
            fixture.Gate.Decide(waiting.ApprovalId!, ApprovalOutcome.Approved);
        }

        var invokers = Enumerable.Range(1, 8).Select(index => fixture.Create("exec-" + index)).ToArray();
        try
        {
            var results = await Task.WhenAll(invokers.Select(invoker => Task.Run(() => invoker.InvokeAsync(InvokerFixture.Call()))));

            Assert.Single(results, result => result.Status == ToolCallStatus.Succeeded);
            Assert.Single(fixture.Handler.Invocations);
        }
        finally
        {
            foreach (var invoker in invokers)
            {
                invoker.Dispose();
            }
        }
    }

    /// <summary>A denied call is not asked again until its approval expires.</summary>
    [Fact]
    public async Task Denied_call_is_not_asked_again_until_expiry()
    {
        var fixture = new InvokerFixture();
        fixture.Policy.Outcome = PolicyOutcome.RequireApproval;
        fixture.Gate.DefaultOutcome = ApprovalOutcome.Denied;
        using var invoker = fixture.Create();

        var first = await invoker.InvokeAsync(InvokerFixture.Call());
        var second = await invoker.InvokeAsync(InvokerFixture.Call());
        fixture.Time.Advance(fixture.Options.ToolApprovalTtl + TimeSpan.FromSeconds(1));
        var third = await invoker.InvokeAsync(InvokerFixture.Call());

        Assert.Equal("approval_denied", first.ErrorCode);
        Assert.Equal("approval_denied", second.ErrorCode);
        Assert.Equal("approval_denied", third.ErrorCode);
        Assert.Equal(2, fixture.Gate.Received.Count);
    }

    /// <summary>A new ledger never mints an id an earlier ledger used.</summary>
    [Fact]
    public void New_ledger_never_reuses_an_approval_id()
    {
        var fixture = new InvokerFixture();
        var expiry = fixture.Time.GetUtcNow().AddHours(1);

        var first = new ToolApprovalLedger(fixture.Time).Open("tool-key", expiry);
        var second = new ToolApprovalLedger(fixture.Time).Open("tool-key", expiry);

        Assert.NotEqual(first, second);
        Assert.StartsWith("tool-key-", first, StringComparison.Ordinal);
    }

    /// <summary>The request key is stable and ignores property order.</summary>
    [Fact]
    public void Request_key_is_stable_and_argument_order_insensitive()
    {
        var key = new CapabilityKey("test-tool", "1.0.0");

        var one = ToolRequestKey.For("agent-1", key, InvokerFixture.Json("""{"a":1,"b":{"c":2,"d":[3,{"f":1,"e":2}]}}"""));
        var two = ToolRequestKey.For("agent-1", key, InvokerFixture.Json("""{ "b": {"d":[3,{"e":2,"f":1}],"c":2}, "a":1 }"""));
        var other = ToolRequestKey.For("agent-1", key, InvokerFixture.Json("""{"a":2,"b":{"c":2,"d":[3,{"f":1,"e":2}]}}"""));
        var otherAgent = ToolRequestKey.For("agent-2", key, InvokerFixture.Json("""{"a":1,"b":{"c":2,"d":[3,{"f":1,"e":2}]}}"""));

        Assert.Equal(one, two);
        Assert.NotEqual(one, other);
        Assert.NotEqual(one, otherAgent);
        Assert.Matches("^tool-[0-9a-f]{32}$", one);
    }

    /// <summary>An Operator failure fails closed without dispatch.</summary>
    /// <param name="where">Which gate throws.</param>
    [Theory]
    [InlineData("policy")]
    [InlineData("gate")]
    public async Task Operator_exception_fails_closed(string where)
    {
        var fixture = new InvokerFixture();
        fixture.Policy.Outcome = PolicyOutcome.RequireApproval;
        if (where == "policy")
        {
            fixture.Policy.Throw = new InvalidOperationException(Sentinel);
        }
        else
        {
            fixture.Gate.Throw = new InvalidOperationException(Sentinel);
        }

        using var invoker = fixture.Create();

        var result = await invoker.InvokeAsync(InvokerFixture.Call());

        Assert.Equal(ToolCallStatus.Failed, result.Status);
        Assert.Equal("gate_error", result.ErrorCode);
        Assert.Empty(fixture.Handler.Invocations);
        Assert.DoesNotContain(fixture.Log.Entries, entry => entry.message.Contains(Sentinel, StringComparison.Ordinal));
    }

    /// <summary>The capability guard runs once, inside the Capabilities invoker.</summary>
    [Fact]
    public async Task Capabilities_guard_runs_exactly_once()
    {
        var fixture = new InvokerFixture();
        using var invoker = fixture.Create();

        var result = await invoker.InvokeAsync(InvokerFixture.Call());

        Assert.Equal(ToolCallStatus.Succeeded, result.Status);
        Assert.Single(fixture.Capabilities.Guard.Requests);
    }

    /// <summary>The caller is the agent subject with the effective grants.</summary>
    [Fact]
    public async Task Caller_carries_effective_grants_and_agent_subject()
    {
        var fixture = new InvokerFixture();
        using var invoker = fixture.Create();

        await invoker.InvokeAsync(InvokerFixture.Call());

        var caller = Assert.Single(fixture.Capabilities.Guard.Requests).caller;
        Assert.Equal("agent:agent-1", caller.SubjectId);
        Assert.Equal(fixture.Grants.Scopes, caller.Scopes);
        Assert.Equal(fixture.Grants.Roles, caller.Roles);
    }

    /// <summary>A capability deny carries the first deny code.</summary>
    [Fact]
    public async Task Capability_deny_maps_to_denied_with_its_code()
    {
        var fixture = new InvokerFixture { Grants = AgentGrants.None };
        var harness = new CapabilityTestHarness(ScriptedCapabilityGuard.RequireScopes());
        harness.Register(CapabilityFixtures.Descriptor(), fixture.Handler);
        using var invoker = new CapabilityToolInvoker(
            new ExecutionIdentity(InvokerFixture.AgentId, "exec-1", "corr-1", fixture.Grants, fixture.Bindings),
            harness.Registry,
            harness.Invoker,
            new OperatorGates(fixture.Breaker, fixture.Policy, fixture.Gate, fixture.Ledger),
            fixture.Options,
            fixture.Telemetry,
            fixture.Log,
            fixture.Time);

        var result = await invoker.InvokeAsync(InvokerFixture.Call());

        Assert.Equal(ToolCallStatus.Denied, result.Status);
        Assert.False(string.IsNullOrEmpty(result.ErrorCode));
        Assert.Empty(fixture.Handler.Invocations);
    }

    /// <summary>The execution's tool-call limit is enforced.</summary>
    [Fact]
    public async Task Call_limit_returns_limit_reached()
    {
        var fixture = new InvokerFixture();
        fixture.Options.MaxToolCallsPerExecution = 2;
        using var invoker = fixture.Create();

        await invoker.InvokeAsync(InvokerFixture.Call());
        await invoker.InvokeAsync(InvokerFixture.Call());
        var third = await invoker.InvokeAsync(InvokerFixture.Call());

        Assert.Equal(ToolCallStatus.LimitReached, third.Status);
        Assert.Equal("tool_call_limit", third.ErrorCode);
        Assert.Equal(2, fixture.Handler.Invocations.Count);
    }

    /// <summary>Arguments and output never reach telemetry or logs; only allowlisted tag keys are used.</summary>
    [Fact]
    public async Task Arguments_and_output_never_in_telemetry_or_logs()
    {
        var fixture = new InvokerFixture(CapabilityResult.Success(InvokerFixture.Json("""{"secret":"sk-secret-value"}""")));
        using var invoker = fixture.Create();

        var result = await invoker.InvokeAsync(InvokerFixture.Call("""{"secret":"sk-secret-value"}"""));

        Assert.Equal(ToolCallStatus.Succeeded, result.Status);
        Assert.NotEmpty(fixture.Telemetry.Tags);
        Assert.All(fixture.Telemetry.Tags, tag => Assert.Contains(tag.Key, AllowedTagKeys));
        Assert.DoesNotContain(fixture.Telemetry.Tags, tag => (tag.Value?.ToString() ?? string.Empty).Contains(Sentinel, StringComparison.Ordinal));
        Assert.DoesNotContain(fixture.Log.Entries, entry => entry.message.Contains(Sentinel, StringComparison.Ordinal));
    }

    /// <summary>Using the invoker after its execution ends throws.</summary>
    [Fact]
    public async Task Use_after_dispose_throws()
    {
        var fixture = new InvokerFixture();
        var invoker = fixture.Create();
        invoker.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => invoker.InvokeAsync(InvokerFixture.Call()));
    }
}
