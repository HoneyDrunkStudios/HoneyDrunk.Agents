using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Agents.Testing;
using HoneyDrunk.Kernel.Abstractions.Agents;
using HoneyDrunk.Operator.Abstractions;
using System.Text.Json;

namespace HoneyDrunk.Agents.Operations.LocalHost;

/// <summary>
/// The local host's fixed workload: two scripted agents, five runs that end in five different ways, one pending
/// approval, one pause and some spend under a budget. For a clock frozen at the same instant it is the same every
/// time, apart from the random bits of run ids.
/// </summary>
public static class LocalWorkload
{
    /// <summary>The first agent's id.</summary>
    public const string ReviewerId = "reviewer";

    /// <summary>The second agent's id.</summary>
    public const string SummarizerId = "summarizer";

    /// <summary>The id of the seeded pending approval.</summary>
    public const string ApprovalId = "fixture-approval-1";

    /// <summary>
    /// A marker placed in everything a response must never show: request payloads, agent output and the approval's
    /// context. A response or fixture that contains it has leaked private data.
    /// </summary>
    public const string PrivateMarker = "local-private-marker";

    private static readonly TimeSpan Step = TimeSpan.FromMinutes(1);

    /// <summary>Seeds the workload, advancing the frozen clock between steps so every time is distinct.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="clock">The host's clock, frozen at the instant the workload starts.</param>
    /// <returns>A task that completes when the workload is seeded.</returns>
    public static async Task SeedAsync(IServiceProvider services, LocalClock clock)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(clock);
        var lifecycle = services.GetRequiredService<IAgentLifecycle>();
        var gate = services.GetRequiredService<IApprovalGate>();
        var costGuard = services.GetRequiredService<ICostGuard>();
        var pauses = services.GetRequiredService<IAgentPauses>();

        foreach (var agent in new[] { new FixtureAgent(ReviewerId, "Fixture Reviewer", clock), new FixtureAgent(SummarizerId, "Fixture Summarizer", clock) })
        {
            lifecycle.Register(new AgentRegistration(agent, AgentGrants.None, []));
            await lifecycle.InitializeAsync(agent.Descriptor.AgentId).ConfigureAwait(false);
        }

        await RunAsync(lifecycle, ReviewerId, FixtureAgent.Complete).ConfigureAwait(false);
        clock.Advance(Step);
        await RunAsync(lifecycle, ReviewerId, FixtureAgent.Reject).ConfigureAwait(false);
        clock.Advance(Step);

        var now = clock.GetUtcNow();
        var context = new Dictionary<string, string>(StringComparer.Ordinal) { ["arguments"] = PrivateMarker };
        await gate.RequestAsync(new ApprovalRequest(ApprovalId, "agent:" + ReviewerId, "tool.publish-review", context, "tool:publish-review@1", now.AddDays(30), "fixture-correlation-1")).ConfigureAwait(false);
        await RunAsync(lifecycle, ReviewerId, FixtureAgent.AwaitApproval).ConfigureAwait(false);
        clock.Advance(Step);

        now = clock.GetUtcNow();
        await costGuard.RecordAsync(new CostEvent("fixture-cost-1", ReviewerId, AgentTestHarness.Tenant.ToString(), CostWindows.Month(now), 4.25m, "usd", "fixture-model", now, "fixture-correlation-2")).ConfigureAwait(false);
        clock.Advance(Step);

        await RunAsync(lifecycle, SummarizerId, FixtureAgent.Fail).ConfigureAwait(false);
        clock.Advance(Step);
        await pauses.PauseAsync("agent:" + SummarizerId, "fixture-operator", "Paused to review a failed run.").ConfigureAwait(false);
        clock.Advance(Step);

        // The pause stops this run at the breaker, before the agent is called.
        await RunAsync(lifecycle, SummarizerId, FixtureAgent.Complete).ConfigureAwait(false);
        clock.Advance(Step);
    }

    private static async Task RunAsync(IAgentLifecycle lifecycle, string agentId, string kind)
    {
        using var payload = JsonDocument.Parse($$"""{"prompt":"{{PrivateMarker}}"}""");
        var options = new AgentExecutionOptions(new AgentMemoryBinding(AgentTestHarness.Tenant, AgentTestHarness.HomeProject, []));
        await lifecycle.ExecuteAsync(agentId, new AgentRequest(kind, payload.RootElement.Clone()), options).ConfigureAwait(false);
    }

    /// <summary>An agent whose request kind decides how its run ends. Each run takes a fixed time on the host's clock.</summary>
    private sealed class FixtureAgent(string agentId, string name, LocalClock clock) : IAgent
    {
        public const string Complete = "complete";
        public const string Reject = "reject";
        public const string AwaitApproval = "await-approval";
        public const string Fail = "fail";

        public IAgentDescriptor Descriptor { get; } = new AgentDescriptor(agentId, name, "fixture", "1.0.0");

        public Task<AgentResult> ExecuteAsync(IAgentRunContext context, AgentRequest request, CancellationToken cancellationToken)
        {
            clock.Advance(TimeSpan.FromSeconds(12));
            using var output = JsonDocument.Parse($$"""{"answer":"{{PrivateMarker}}"}""");
            return request.Kind switch
            {
                Complete => Task.FromResult(new AgentResult(AgentOutcome.Completed, output.RootElement.Clone(), null, null)),
                Reject => Task.FromResult(new AgentResult(AgentOutcome.Rejected, null, "fixture_rejected", null)),
                AwaitApproval => Task.FromResult(new AgentResult(AgentOutcome.AwaitingApproval, null, "approval_required", ApprovalId)),
                _ => throw new InvalidOperationException("The fixture run failed: " + PrivateMarker),
            };
        }
    }
}
