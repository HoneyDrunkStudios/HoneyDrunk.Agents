using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Capabilities.Testing;
using HoneyDrunk.Kernel.Abstractions.Agents;
using HoneyDrunk.Kernel.Abstractions.Context;
using HoneyDrunk.Kernel.Abstractions.Identity;
using HoneyDrunk.Kernel.Abstractions.Telemetry;
using HoneyDrunk.Memory;
using HoneyDrunk.Memory.Abstractions;
using HoneyDrunk.Memory.Providers.InMemory;
using HoneyDrunk.Operator.Abstractions;
using HoneyDrunk.Operator.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace HoneyDrunk.Agents.Testing;

/// <summary>
/// The real agent lifecycle composed over test pieces: the Capabilities test harness, the Memory runtime on the
/// in-memory provider with <see cref="GrantingAuthorizationPolicy"/>, and in-memory Operator gates. Every piece is
/// exposed for assertions.
/// </summary>
public sealed class AgentTestHarness : IDisposable
{
    private readonly ServiceProvider provider;

    /// <summary>Initializes a new instance of the <see cref="AgentTestHarness"/> class.</summary>
    /// <param name="configure">Optionally configures the runtime.</param>
    /// <param name="timeProvider">The clock; a <see cref="FakeTimeProvider"/> at a fixed instant by default.</param>
    public AgentTestHarness(Action<AgentRuntimeOptions>? configure = null, TimeProvider? timeProvider = null)
    {
        this.Time = timeProvider ?? new FakeTimeProvider(TestAgentExecutionContext.FixedStart);
        this.Gate = new InMemoryApprovalGate(this.Time);
        this.MemoryProvider = new InMemoryMemoryStore(this.Time);
        var telemetry = new NullTelemetryActivityFactory();
        this.MemoryResolver = new MemoryScopeResolver(
            new GrantingAuthorizationPolicy(),
            this.Gate,
            Microsoft.Extensions.Options.Options.Create(new MemoryAuthorizationOptions()),
            telemetry,
            this.Time,
            NullLogger<MemoryScopeResolver>.Instance);
        this.MemoryStore = new ScopeCheckedMemoryStore(this.MemoryProvider, nameof(InMemoryMemoryStore), telemetry, this.Time, NullLogger<ScopeCheckedMemoryStore>.Instance);
        this.GridAccessor = new TestGridContextAccessor(new TestGridContext(Tenant));

        var services = new ServiceCollection();
        services.AddSingleton<IGridContextAccessor>(this.GridAccessor);
        services.AddSingleton<IAgentExecutionContextFactory>(this.ContextFactory);
        services.AddSingleton<ITelemetryActivityFactory>(telemetry);
        services.AddSingleton(this.Capabilities.Registry);
        services.AddSingleton(this.Capabilities.Invoker);
        services.AddSingleton<IMemoryScopeResolver>(this.MemoryResolver);
        services.AddSingleton<IMemoryStore>(this.MemoryStore);
        services.AddSingleton<ICircuitBreaker>(this.Breaker);
        services.AddSingleton<IDecisionPolicy>(this.Policy);
        services.AddSingleton<IApprovalGate>(this.Gate);
        this.provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var options = new AgentRuntimeOptions();
        configure?.Invoke(options);
        this.Lifecycle = new InProcessAgentLifecycle(
            this.provider.GetRequiredService<IServiceScopeFactory>(),
            new ToolApprovalLedger(this.Time),
            [this.Observer],
            Microsoft.Extensions.Options.Options.Create(options),
            this.Time,
            NullLogger<InProcessAgentLifecycle>.Instance);
    }

    /// <summary>Gets the tenant every harness execution runs in.</summary>
    public static TenantId Tenant { get; } = new("01JC0000000000000000000001");

    /// <summary>Gets the default home project.</summary>
    public static ProjectId HomeProject { get; } = new("01JC0000000000000000000002");

    /// <summary>Gets a second project for escalation tests.</summary>
    public static ProjectId OtherProject { get; } = new("01JC0000000000000000000003");

    /// <summary>Gets the clock.</summary>
    public TimeProvider Time { get; }

    /// <summary>Gets the Capabilities test harness; register tools here.</summary>
    public CapabilityTestHarness Capabilities { get; } = new();

    /// <summary>Gets the in-memory memory provider.</summary>
    public InMemoryMemoryStore MemoryProvider { get; }

    /// <summary>Gets the memory scope resolver.</summary>
    public MemoryScopeResolver MemoryResolver { get; }

    /// <summary>Gets the scope-checked memory store.</summary>
    public ScopeCheckedMemoryStore MemoryStore { get; }

    /// <summary>Gets the circuit breaker.</summary>
    public InMemoryCircuitBreaker Breaker { get; } = new();

    /// <summary>Gets the decision policy; Allow by default.</summary>
    public ScriptedDecisionPolicy Policy { get; } = new();

    /// <summary>Gets the approval gate; Approved by default.</summary>
    public InMemoryApprovalGate Gate { get; }

    /// <summary>Gets the grid context accessor.</summary>
    public TestGridContextAccessor GridAccessor { get; }

    /// <summary>Gets the Kernel agent context factory.</summary>
    public TestAgentExecutionContextFactory ContextFactory { get; } = new();

    /// <summary>Gets the lifecycle observer.</summary>
    public RecordingLifecycleObserver Observer { get; } = new();

    /// <summary>Gets the lifecycle under test.</summary>
    public IAgentLifecycle Lifecycle { get; }

    /// <summary>Builds execution options in the harness tenant.</summary>
    /// <param name="narrowGrantsTo">An optional narrowing.</param>
    /// <param name="alsoRead">Other projects to read.</param>
    /// <returns>The options, with <see cref="HomeProject"/> as home.</returns>
    public AgentExecutionOptions ExecutionOptions(AgentGrants? narrowGrantsTo = null, params ProjectId[] alsoRead) =>
        new(new AgentMemoryBinding(Tenant, HomeProject, alsoRead), narrowGrantsTo);

    /// <inheritdoc />
    public void Dispose() => this.provider.Dispose();
}
