using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Capabilities.Abstractions;
using HoneyDrunk.Capabilities.Testing;
using HoneyDrunk.Kernel.Abstractions.Agents;
using HoneyDrunk.Kernel.Abstractions.Context;
using HoneyDrunk.Kernel.Abstractions.Telemetry;
using HoneyDrunk.Memory.Abstractions;
using HoneyDrunk.Operator.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace HoneyDrunk.Agents.Tests;

/// <summary>A lifecycle over real Capabilities and Memory runtimes with scripted Operator gates.</summary>
internal sealed class LifecycleFixture : IDisposable
{
    private readonly ServiceProvider provider;

    public LifecycleFixture(Action<AgentRuntimeOptions>? configure = null, bool gridInitialized = true)
    {
        this.Capabilities.Register(CapabilityFixtures.Descriptor(), this.Handler);
        this.Grid = new TestGridContextAccessor(new TestGridContext(MemoryFixture.Tenant, gridInitialized));
        var services = new ServiceCollection();
        services.AddSingleton<IGridContextAccessor>(this.Grid);
        services.AddSingleton<IAgentExecutionContextFactory>(this.ContextFactory);
        services.AddSingleton<ITelemetryActivityFactory>(this.Telemetry);
        services.AddSingleton(this.Capabilities.Registry);
        services.AddSingleton(this.Capabilities.Invoker);
        services.AddSingleton<IMemoryScopeResolver>(this.Memory.Resolver);
        services.AddSingleton<IMemoryStore>(this.Memory.Store);
        services.AddSingleton<ICircuitBreaker>(this.Breaker);
        services.AddSingleton<IDecisionPolicy>(this.Policy);
        services.AddSingleton<IApprovalGate>(this.Gate);
        this.provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var options = new AgentRuntimeOptions();
        configure?.Invoke(options);
        this.Options = options;
        this.Lifecycle = new InProcessAgentLifecycle(
            this.provider.GetRequiredService<IServiceScopeFactory>(),
            new ToolApprovalLedger(this.Memory.Time),
            [this.Observer],
            Microsoft.Extensions.Options.Options.Create(options),
            this.Memory.Time,
            this.Log);
    }

    public MemoryFixture Memory { get; } = new();

    public CapabilityTestHarness Capabilities { get; } = new();

    public RecordingCapabilityHandler Handler { get; } = new(CapabilityResult.Success(InvokerFixture.Json("""{"ok":true}""")));

    public TestBreaker Breaker { get; } = new();

    public ScriptedDecisionPolicy Policy { get; } = new();

    public ScriptedApprovalGate Gate { get; } = new();

    public TestGridContextAccessor Grid { get; }

    public TestAgentExecutionContextFactory ContextFactory { get; } = new();

    public RecordingTelemetryActivityFactory Telemetry => this.Memory.Telemetry;

    public RecordingObserver Observer { get; } = new();

    public ListLogger<InProcessAgentLifecycle> Log { get; } = new();

    public AgentRuntimeOptions Options { get; }

    public InProcessAgentLifecycle Lifecycle { get; }

    public static AgentRequest Request() => new("test", JsonDocument.Parse("{}").RootElement);

    public static AgentExecutionOptions ExecutionOptions(AgentGrants? narrow = null, params Kernel.Abstractions.Identity.ProjectId[] alsoRead) =>
        new(MemoryFixture.Binding(MemoryFixture.ProjectA, alsoRead), narrow);

    public async Task<TestAgent> ReadyAsync(TestAgent? agent = null, AgentGrants? grants = null)
    {
        agent ??= new TestAgent();
        this.Lifecycle.Register(new AgentRegistration(agent, grants ?? new AgentGrants([CapabilityFixtures.Scope], ["operator"]), [new(CapabilityFixtures.Name, CapabilityFixtures.Version)]));
        await this.Lifecycle.InitializeAsync(agent.Descriptor.AgentId);
        return agent;
    }

    public void Dispose() => this.provider.Dispose();
}
