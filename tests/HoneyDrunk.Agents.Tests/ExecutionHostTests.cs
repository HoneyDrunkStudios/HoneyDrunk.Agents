using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Audit.Abstractions;
using HoneyDrunk.Auth.Abstractions;
using HoneyDrunk.Auth.Authorization;
using HoneyDrunk.Capabilities.Abstractions;
using HoneyDrunk.Capabilities.Testing;
using HoneyDrunk.Kernel.Abstractions.Agents;
using HoneyDrunk.Kernel.Abstractions.Context;
using HoneyDrunk.Kernel.Abstractions.Identity;
using HoneyDrunk.Kernel.Context;
using HoneyDrunk.Kernel.Hosting;
using HoneyDrunk.Kernel.Telemetry;
using HoneyDrunk.Memory;
using HoneyDrunk.Memory.Providers.InMemory;
using HoneyDrunk.Operator.Abstractions;
using HoneyDrunk.Operator.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Xunit;

namespace HoneyDrunk.Agents.Tests;

/// <summary>Exercises the runtime with real Kernel, Capabilities, and Memory components.</summary>
public sealed class ExecutionHostTests
{
    private static readonly TenantId Tenant = new("01JC0000000000000000000001");
    private static readonly TenantId OtherTenant = new("01JC0000000000000000000009");
    private static readonly ProjectId Project = new("01JC0000000000000000000002");
    private static readonly ProjectId OtherProject = new("01JC0000000000000000000003");
    private static readonly AgentExecutionOptions Execution = new(new AgentMemoryBinding(Tenant, Project, []));
    private static readonly AgentRequest Request = new("test", JsonSerializer.SerializeToElement<object>(new { }));
    private static readonly AgentToolBinding Binding = new("echo", "1.0.0");

    /// <summary>Dispatches only the exact bound tool with effective grants, scopes memory, and revokes saved access.</summary>
    /// <returns>The test operation.</returns>
    [Fact]
    public async Task Execute_WithRealGridServices_EnforcesExecutionBoundaries()
    {
        var fixture = new Fixture();
        await using var provider = fixture.Build();
        var grid = InitializedGrid(Tenant);
        using var ambient = ScopedGridContextAccessor.SetContext(grid);
        var lifecycle = provider.GetRequiredService<IAgentLifecycle>();
        IAgentMemory? escaped = null;
        var agent = new TestAgent(async (context, _, token) =>
        {
            Assert.Equal("test-correlation", context.GridContext.CorrelationId);
            Assert.Equal(["read"], context.Grants.Scopes);
            escaped = context.Memory;
            var note = await escaped.RememberAsync(new AgentMemoryNote("test", "private memo", ["sample"]), token);
            Assert.Equal(note.Id, Assert.Single(await escaped.RecallAsync(new AgentRecall(), token)).Id);
            Assert.True(note.FromHomeScope);

            var refused = await context.Tools.InvokeAsync(new ToolCall("echo", "2.0.0", Request.Payload), token);
            Assert.Equal(ToolCallStatus.NotBound, refused.Status);
            var invoked = await context.Tools.InvokeAsync(new ToolCall(Binding.Name, Binding.Version, Request.Payload), token);
            Assert.Equal(ToolCallStatus.Succeeded, invoked.Status);
            return new AgentResult(AgentOutcome.Completed, null, null, null);
        });

        lifecycle.Register(new AgentRegistration(agent, new AgentGrants(["read", "write"], []), [Binding]));
        await lifecycle.InitializeAsync(agent.Descriptor.AgentId);
        var result = await lifecycle.ExecuteAsync(agent.Descriptor.AgentId, Request, Execution with
        {
            NarrowGrantsTo = new AgentGrants(["read", "extra"], []),
        });

        Assert.Equal(AgentOutcome.Completed, result.Outcome);
        var invocation = Assert.Single(fixture.Handler.Invocations);
        Assert.Equal("agent:worker", invocation.Caller.SubjectId);
        Assert.Equal(["read"], invocation.Caller.Scopes);
        var saved = Assert.IsAssignableFrom<IAgentMemory>(escaped);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => saved.RecallAsync(new AgentRecall()));
        await lifecycle.DecommissionAsync(agent.Descriptor.AgentId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => lifecycle.ExecuteAsync(agent.Descriptor.AgentId, Request, Execution));
    }

    /// <summary>Refuses missing context, another tenant, unauthorized memory, and an open breaker before dispatch.</summary>
    /// <returns>The test operation.</returns>
    [Fact]
    public async Task Execute_WhenGuardrailsRefuse_DoesNotRunAgent()
    {
        var fixture = new Fixture();
        await using var provider = fixture.Build();
        var lifecycle = provider.GetRequiredService<IAgentLifecycle>();
        var agent = new TestAgent((_, _, _) => throw new InvalidOperationException("The workload must not be invoked."));
        lifecycle.Register(new AgentRegistration(agent, AgentGrants.None, []));
        await lifecycle.InitializeAsync(agent.Descriptor.AgentId);

        var missing = await lifecycle.ExecuteAsync(agent.Descriptor.AgentId, Request, Execution);
        Assert.Equal("grid_context_missing", missing.ReasonCode);

        var grid = InitializedGrid(Tenant);
        using var ambient = ScopedGridContextAccessor.SetContext(grid);
        var wrongTenant = await lifecycle.ExecuteAsync(agent.Descriptor.AgentId, Request, new AgentExecutionOptions(new AgentMemoryBinding(OtherTenant, Project, [])));
        Assert.Equal("tenant_mismatch", wrongTenant.ReasonCode);

        var wider = new AgentExecutionOptions(new AgentMemoryBinding(Tenant, Project, [OtherProject]));
        var denied = await lifecycle.ExecuteAsync(agent.Descriptor.AgentId, Request, wider);
        Assert.Equal("memory_escalation_denied", denied.ReasonCode);

        await fixture.Breaker.TripAsync("agent:worker", "test");
        var halted = await lifecycle.ExecuteAsync(agent.Descriptor.AgentId, Request, Execution);
        Assert.Equal("breaker_open", halted.ReasonCode);
        Assert.Empty(fixture.Handler.Invocations);
    }

    /// <summary>One pending approval is reused for equivalent calls, but never across tenants.</summary>
    /// <returns>The test operation.</returns>
    [Fact]
    public async Task Execute_WithPendingApproval_IsolatesApprovalByTenant()
    {
        var fixture = new Fixture(new RequireApproval());
        fixture.Gate.DefaultOutcome = ApprovalOutcome.Pending;
        await using var provider = fixture.Build();
        var lifecycle = provider.GetRequiredService<IAgentLifecycle>();
        var agent = RegisterToolAgent(lifecycle);
        await lifecycle.InitializeAsync(agent.Descriptor.AgentId);

        var first = await RunToolAsync(lifecycle, Tenant, """{"b":2,"a":1}""");
        var repeated = await RunToolAsync(lifecycle, Tenant, """{"a":1,"b":2}""");
        var other = await RunToolAsync(lifecycle, OtherTenant, """{"a":1,"b":2}""");

        Assert.Equal(AgentOutcome.AwaitingApproval, first.Outcome);
        Assert.NotNull(first.ApprovalId);
        Assert.Equal(first.ApprovalId, repeated.ApprovalId);
        Assert.NotEqual(first.ApprovalId, other.ApprovalId);
        Assert.Equal(2, fixture.Gate.ReceivedRequests.Count);
        Assert.Empty(fixture.Handler.Invocations);
    }

    /// <summary>Separately approved runs have distinct approval identifiers and each dispatch once.</summary>
    /// <returns>The test operation.</returns>
    [Fact]
    public async Task Execute_WithNewApprovals_DispatchesEachOnce()
    {
        var fixture = new Fixture(new RequireApproval());
        fixture.Gate.DefaultOutcome = ApprovalOutcome.Approved;
        await using var provider = fixture.Build();
        var lifecycle = provider.GetRequiredService<IAgentLifecycle>();
        var agent = RegisterToolAgent(lifecycle);
        await lifecycle.InitializeAsync(agent.Descriptor.AgentId);

        var first = await RunToolAsync(lifecycle, Tenant, "{}");
        var second = await RunToolAsync(lifecycle, Tenant, "{}");

        Assert.Equal(AgentOutcome.Completed, first.Outcome);
        Assert.Equal(AgentOutcome.Completed, second.Outcome);
        Assert.Equal(2, fixture.Handler.Invocations.Count);
        Assert.Equal(2, fixture.Gate.ReceivedRequests.Select(request => request.ApprovalId).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>Translates a workload exception without exposing its message.</summary>
    /// <returns>The test operation.</returns>
    [Fact]
    public async Task Execute_WhenWorkloadThrows_ReturnsSafeFailure()
    {
        await using var provider = new Fixture().Build();
        var grid = InitializedGrid(Tenant);
        using var ambient = ScopedGridContextAccessor.SetContext(grid);
        var lifecycle = provider.GetRequiredService<IAgentLifecycle>();
        var agent = new TestAgent((_, _, _) => throw new InvalidOperationException("private-sentinel"));
        lifecycle.Register(new AgentRegistration(agent, AgentGrants.None, []));
        await lifecycle.InitializeAsync(agent.Descriptor.AgentId);

        var result = await lifecycle.ExecuteAsync(agent.Descriptor.AgentId, Request, Execution);

        Assert.Equal(AgentOutcome.Failed, result.Outcome);
        Assert.Equal("agent_error", result.ReasonCode);
        Assert.Null(result.Output);
    }

    private static TestAgent RegisterToolAgent(IAgentLifecycle lifecycle)
    {
        var agent = new TestAgent(async (context, request, token) =>
        {
            var call = await context.Tools.InvokeAsync(new ToolCall(Binding.Name, Binding.Version, request.Payload), token);
            return new AgentResult(
                call.Status == ToolCallStatus.Succeeded ? AgentOutcome.Completed : AgentOutcome.AwaitingApproval,
                null,
                call.ErrorCode,
                call.ApprovalId);
        });
        lifecycle.Register(new AgentRegistration(agent, AgentGrants.None, [Binding]));
        return agent;
    }

    private static async Task<AgentResult> RunToolAsync(IAgentLifecycle lifecycle, TenantId tenant, string arguments)
    {
        var grid = InitializedGrid(tenant);
        using var ambient = ScopedGridContextAccessor.SetContext(grid);
        using var document = JsonDocument.Parse(arguments);
        return await lifecycle.ExecuteAsync("worker", new AgentRequest("tool", document.RootElement), new AgentExecutionOptions(new AgentMemoryBinding(tenant, Project, [])));
    }

    private static GridContext InitializedGrid(TenantId tenant)
    {
        var grid = new GridContext("agents-tests", "honeydrunk-studios", "testing");
        grid.Initialize("test-correlation", tenantId: tenant);
        return grid;
    }

    private sealed class TestAgent(Func<IAgentRunContext, AgentRequest, CancellationToken, Task<AgentResult>> action) : IAgent
    {
        public IAgentDescriptor Descriptor { get; } = new AgentDescriptor("worker", "Worker", "automation", "1.0.0");

        public Task<AgentResult> ExecuteAsync(IAgentRunContext context, AgentRequest request, CancellationToken cancellationToken) =>
            action(context, request, cancellationToken);
    }

    private sealed class RequireApproval : IDecisionPolicy
    {
        public Task<PolicyDecision> EvaluateAsync(ActionContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PolicyDecision(PolicyOutcome.RequireApproval, null, []));
    }

    private sealed class TestAudit : IAuditLog
    {
        public Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Fixture(IDecisionPolicy? decision = null)
    {
        public InMemoryCircuitBreaker Breaker { get; } = new();

        public InMemoryApprovalGate Gate { get; } = new();

        public RecordingCapabilityHandler Handler { get; } = new(CapabilityResult.Success(null));

        public ServiceProvider Build()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHoneyDrunkNode(options =>
            {
                options.NodeId = new NodeId("agents-tests");
                options.SectorId = new SectorId("ai");
                options.EnvironmentId = new EnvironmentId("testing");
                options.StudioId = "honeydrunk-studios";
            }).AddTelemetry();
            services.AddSingleton<IGridContextAccessor>(provider => provider.GetRequiredService<ScopedGridContextAccessor>());
            services.AddSingleton<ICircuitBreaker>(this.Breaker);
            services.AddSingleton<IApprovalGate>(this.Gate);
            services.AddSingleton(decision ?? new PermissiveDecisionPolicy());
            services.AddSingleton<IAuditLog>(new TestAudit());
            services.AddScoped<IAuthorizationPolicy, DefaultAuthorizationPolicy>();
            services.AddHoneyDrunkMemory<InMemoryMemoryStore>();
            var capabilities = new CapabilityTestHarness();
            capabilities.Register(
                new CapabilityDescriptor(Binding.Name, Binding.Version, "Echo an input.", "agents-tests", """{"type":"object"}""", null, new CapabilityPermissions(["read"], [])),
                this.Handler);
            services.AddSingleton(capabilities.Registry);
            services.AddSingleton(capabilities.Invoker);
            services.AddHoneyDrunkAgents(options =>
                options.CreateOperationContext = (grid, name) => new OperationContext(grid, name, Guid.NewGuid().ToString("N")));
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }
    }
}
