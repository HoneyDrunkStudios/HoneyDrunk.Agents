using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Agents.Testing;
using HoneyDrunk.Auth.Authorization;
using HoneyDrunk.Capabilities;
using HoneyDrunk.Capabilities.Abstractions;
using HoneyDrunk.Capabilities.Testing;
using HoneyDrunk.Kernel.Abstractions.Agents;
using HoneyDrunk.Kernel.Abstractions.Context;
using HoneyDrunk.Kernel.Abstractions.Identity;
using HoneyDrunk.Kernel.Abstractions.Telemetry;
using HoneyDrunk.Kernel.AgentsInterop;
using HoneyDrunk.Kernel.Context;
using HoneyDrunk.Kernel.Hosting;
using HoneyDrunk.Kernel.Telemetry;
using HoneyDrunk.Memory;
using HoneyDrunk.Memory.Abstractions;
using HoneyDrunk.Memory.Providers.InMemory;
using HoneyDrunk.Operator.Abstractions;
using HoneyDrunk.Operator.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace HoneyDrunk.Agents.Canary;

/// <summary>
/// The agent runtime over the real Kernel grid context and agent-context projection, the real Capabilities runtime
/// with its Auth guard, the real Memory runtime, and Auth's real <see cref="DefaultAuthorizationPolicy"/>.
/// </summary>
internal sealed class CanaryGrid : IDisposable
{
    public const string AgentId = "canary-agent";

    public const string Scope = "test.invoke";

    public static readonly TenantId Tenant = new("01JC0000000000000000000001");

    public static readonly ProjectId Home = new("01JC0000000000000000000002");

    public static readonly ProjectId Other = new("01JC0000000000000000000003");

    private readonly ServiceProvider provider;

    public CanaryGrid(bool kernelHost = false)
    {
        var grid = new GridContext("agents-canary", "test-studio", "test");
        grid.Initialize("corr-canary", tenantId: Tenant);
        this.GridContext = grid;
        IGridContextAccessor accessor = kernelHost ? new ScopedGridContextAccessor() : new TestGridContextAccessor(grid);
        var telemetry = new NullTelemetryActivityFactory();
        var policy = new DefaultAuthorizationPolicy(telemetry, this.Audit, accessor, NullLogger<DefaultAuthorizationPolicy>.Instance);
        var time = TimeProvider.System;

        var registry = new InProcessCapabilityRegistry(NullLogger<InProcessCapabilityRegistry>.Instance);
        registry.Register(CapabilityFixtures.Descriptor(requiredScopes: [Scope]), this.Handler);
        var guard = new AuthPolicyCapabilityGuard(policy, Options.Create(new CapabilityGuardOptions()), NullLogger<AuthPolicyCapabilityGuard>.Instance);
        var invoker = new CapabilityInvoker(registry, guard, telemetry, NullLogger<CapabilityInvoker>.Instance);

        var resolver = new MemoryScopeResolver(policy, this.Gate, Options.Create(new MemoryAuthorizationOptions()), telemetry, time, NullLogger<MemoryScopeResolver>.Instance);
        var store = new ScopeCheckedMemoryStore(new InMemoryMemoryStore(time), nameof(InMemoryMemoryStore), telemetry, time, NullLogger<ScopeCheckedMemoryStore>.Instance);

        var services = new ServiceCollection();
        if (kernelHost)
        {
            // A background host: Kernel's own registrations, with the AsyncLocal accessor in place of the HTTP one.
            services.AddHoneyDrunkNode(options =>
            {
                options.NodeId = new NodeId("agents-canary");
                options.SectorId = new SectorId("ai");
                options.EnvironmentId = new EnvironmentId("development");
                options.StudioId = "test-studio";
            }).AddTelemetry();
            services.AddSingleton(accessor);
        }
        else
        {
            services.AddSingleton(accessor);
            services.AddSingleton<IAgentExecutionContextFactory>(new ProjectingContextFactory());
            services.AddSingleton<ITelemetryActivityFactory>(telemetry);
        }

        services.AddSingleton<ICapabilityRegistry>(registry);
        services.AddSingleton<ICapabilityInvoker>(invoker);
        services.AddSingleton<IMemoryScopeResolver>(resolver);
        services.AddSingleton<IMemoryStore>(store);
        services.AddSingleton<ICircuitBreaker>(new InMemoryCircuitBreaker());
        services.AddSingleton<IDecisionPolicy>(this.Policy);
        services.AddSingleton<IApprovalGate>(this.Gate);
        services.AddLogging();
        services.AddHoneyDrunkAgents();
        this.provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        this.Lifecycle = this.provider.GetRequiredService<IAgentLifecycle>();
    }

    public IGridContext GridContext { get; }

    public RecordingAuditLog Audit { get; } = new();

    public RecordingCapabilityHandler Handler { get; } = new(CapabilityResult.Success(null));

    public ScriptedDecisionPolicy Policy { get; } = new();

    public InMemoryApprovalGate Gate { get; } = new();

    public IAgentLifecycle Lifecycle { get; }

    public IServiceProvider Services => this.provider;

    public static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static ToolCall ToolCall() => new(CapabilityFixtures.Name, CapabilityFixtures.Version, Json("{}"));

    public async Task<AgentResult> RunAsync(ScriptedAgent agent, AgentGrants grants, AgentExecutionOptions? options = null)
    {
        this.Lifecycle.Register(new AgentRegistration(agent, grants, [new(CapabilityFixtures.Name, CapabilityFixtures.Version)]));
        await this.Lifecycle.InitializeAsync(agent.Descriptor.AgentId);
        return await this.Lifecycle.ExecuteAsync(
            agent.Descriptor.AgentId,
            new AgentRequest("run", Json("{}")),
            options ?? new AgentExecutionOptions(new AgentMemoryBinding(Tenant, Home, [])));
    }

    public void Dispose() => this.provider.Dispose();

    /// <summary>Builds Kernel agent contexts through Kernel's own projection.</summary>
    private sealed class ProjectingContextFactory : IAgentExecutionContextFactory
    {
        private readonly List<IOperationContext> created = [];

        public HoneyDrunk.Kernel.Abstractions.Agents.IAgentExecutionContext Create(IAgentDescriptor agent, IGridContext gridContext, IOperationContext? operationContext = null, IReadOnlyDictionary<string, object?>? executionMetadata = null)
        {
            if (operationContext is null)
            {
                var operation = new TestOperationContext(gridContext, "Agent:" + agent.AgentId);
                this.created.Add(operation);
                operationContext = operation;
            }

            return AgentContextProjection.ProjectToAgentContext(gridContext, operationContext, agent, executionMetadata);
        }
    }
}
