using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Capabilities.Abstractions;
using HoneyDrunk.Kernel.Abstractions.Agents;
using HoneyDrunk.Kernel.Abstractions.Context;
using HoneyDrunk.Kernel.Abstractions.Telemetry;
using HoneyDrunk.Memory.Abstractions;
using HoneyDrunk.Operator.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace HoneyDrunk.Agents;

/// <summary>
/// The per-host agent registry and runner (ADR-0020 D11). Each execution runs in its own dependency-injection scope:
/// it checks the grid context, the tenant, the agent's breaker and memory access before the agent runs, then disposes
/// everything execution-scoped when it completes. Agent exceptions become <c>agent_error</c>, except configured stop
/// exceptions, which are rethrown unchanged after cleanup and never retried.
/// </summary>
public sealed partial class InProcessAgentLifecycle : IAgentLifecycle
{
    private readonly ConcurrentDictionary<string, Entry> agents = new(StringComparer.Ordinal);
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ToolApprovalLedger ledger;
    private readonly IAgentLifecycleObserver[] observers;
    private readonly AgentRuntimeOptions options;
    private readonly Type[] propagated;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<InProcessAgentLifecycle> logger;

    /// <summary>Initializes a new instance of the <see cref="InProcessAgentLifecycle"/> class.</summary>
    /// <param name="scopeFactory">Creates one scope per execution.</param>
    /// <param name="ledger">The host's open-approval ledger.</param>
    /// <param name="observers">The lifecycle observers.</param>
    /// <param name="options">The runtime options.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentException">A propagated type does not derive from <see cref="Exception"/>.</exception>
    public InProcessAgentLifecycle(
        IServiceScopeFactory scopeFactory,
        ToolApprovalLedger ledger,
        IEnumerable<IAgentLifecycleObserver> observers,
        IOptions<AgentRuntimeOptions> options,
        TimeProvider timeProvider,
        ILogger<InProcessAgentLifecycle> logger)
    {
        this.scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        this.observers = (observers ?? throw new ArgumentNullException(nameof(observers))).ToArray();
        this.options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.propagated = this.options.PropagatedExceptionTypes.ToArray();
        if (this.propagated.Any(type => type is null || !typeof(Exception).IsAssignableFrom(type)))
        {
            throw new ArgumentException("Every propagated type must derive from System.Exception.", nameof(options));
        }
    }

    /// <inheritdoc />
    public void Register(AgentRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var agentId = registration.Agent.Descriptor?.AgentId;
        if (agentId is null || !AgentIdPattern().IsMatch(agentId))
        {
            throw new ArgumentException("The agent id must be 1 to 64 characters of a-z, 0-9, '.', '_' or '-', starting with a letter or digit.", nameof(registration));
        }

        if (!this.agents.TryAdd(agentId, new Entry(registration)))
        {
            throw new InvalidOperationException($"An agent with id '{agentId}' is already registered.");
        }

        this.Notify(agentId, null, "register", null);
    }

    /// <inheritdoc />
    public async Task InitializeAsync(string agentId, CancellationToken cancellationToken = default)
    {
        var entry = this.Get(agentId);
        RequirePhase(agentId, entry, AgentLifecyclePhase.Registered, "initialize");
        var scope = this.scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var registry = scope.ServiceProvider.GetRequiredService<ICapabilityRegistry>();
            foreach (var binding in entry.Registration.Tools)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsRegistered(registry, binding))
                {
                    throw new InvalidOperationException($"Agent '{agentId}' is bound to {binding.Name}@{binding.Version}, which is not registered.");
                }
            }
        }

        if (!entry.TryMove(AgentLifecyclePhase.Registered, AgentLifecyclePhase.Initialized))
        {
            throw new InvalidOperationException($"Agent '{agentId}' is {entry.Phase}; it cannot be initialized.");
        }

        this.Notify(agentId, null, "initialize", null);
    }

    /// <inheritdoc />
    public async Task<AgentResult> ExecuteAsync(string agentId, AgentRequest request, AgentExecutionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);
        var entry = this.Get(agentId);
        RequirePhase(agentId, entry, AgentLifecyclePhase.Initialized, "execute");
        var executionId = Guid.CreateVersion7(this.timeProvider.GetUtcNow()).ToString("N");
        var scope = this.scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var services = scope.ServiceProvider;
            using var activity = services.GetRequiredService<ITelemetryActivityFactory>().Start(
                AgentTelemetry.ExecuteActivity,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [AgentTelemetry.AgentIdTag] = agentId,
                    [AgentTelemetry.ExecutionIdTag] = executionId,
                });
            this.Notify(agentId, executionId, "execute", null);
            var outcome = "error";
            try
            {
                var result = await this.ExecuteInScopeAsync(entry.Registration, executionId, request, options, services, cancellationToken).ConfigureAwait(false);
                outcome = result.Outcome.ToString();
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                outcome = "cancelled";
                throw;
            }
            catch (Exception exception) when (this.IsPropagated(exception))
            {
                outcome = "propagated";
                throw;
            }
            finally
            {
                activity?.SetTag(AgentTelemetry.OutcomeTag, outcome);
                this.Notify(agentId, executionId, "complete", outcome);
            }
        }
    }

    /// <inheritdoc />
    public Task DecommissionAsync(string agentId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = this.Get(agentId);
        entry.MoveTo(AgentLifecyclePhase.Decommissioned);
        this.Notify(agentId, null, "decommission", null);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public AgentLifecyclePhase GetPhase(string agentId) => this.Get(agentId).Phase;

    private static void RequirePhase(string agentId, Entry entry, AgentLifecyclePhase required, string action)
    {
        var phase = entry.Phase;
        if (phase != required)
        {
            throw new InvalidOperationException($"Agent '{agentId}' is {phase}; {action} needs {required}.");
        }
    }

    private static bool IsRegistered(ICapabilityRegistry registry, AgentToolBinding binding)
    {
        try
        {
            return registry.Resolve(new CapabilityKey(binding.Name, binding.Version)) is not null;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static IGridContext? ReadGridContext(IServiceProvider services)
    {
        try
        {
            return services.GetRequiredService<IGridContextAccessor>().GridContext;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static AgentResult Refuse(AgentOutcome outcome, string code) => new(outcome, null, code, null);

    [GeneratedRegex(@"\A[a-z0-9][a-z0-9._-]{0,63}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AgentIdPattern();

    private async Task<AgentResult> ExecuteInScopeAsync(
        AgentRegistration registration,
        string executionId,
        AgentRequest request,
        AgentExecutionOptions executionOptions,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var agentId = registration.Agent.Descriptor.AgentId;
        var grid = ReadGridContext(services);
        if (grid is null || !grid.IsInitialized)
        {
            return Refuse(AgentOutcome.Failed, "grid_context_missing");
        }

        if (executionOptions.Memory.Tenant != grid.TenantId)
        {
            return Refuse(AgentOutcome.Rejected, "tenant_mismatch");
        }

        var identity = new ExecutionIdentity(agentId, executionId, grid.CorrelationId, registration.Grants.Narrow(executionOptions.NarrowGrantsTo), registration.Tools);
        var breaker = services.GetRequiredService<ICircuitBreaker>();
        if (!await breaker.IsAllowedAsync(identity.BreakerName, cancellationToken).ConfigureAwait(false))
        {
            return Refuse(AgentOutcome.Halted, "breaker_open");
        }

        IMemoryScope home;
        IMemoryScope read;
        try
        {
            (home, read) = await AgentMemoryScopes.OpenAsync(services.GetRequiredService<IMemoryScopeResolver>(), identity, executionOptions.Memory, cancellationToken).ConfigureAwait(false);
        }
        catch (MemoryEscalationDeniedException)
        {
            return Refuse(AgentOutcome.Rejected, "memory_escalation_denied");
        }

        var telemetry = services.GetRequiredService<ITelemetryActivityFactory>();
        using var memory = new MemoryAgentMemory(services.GetRequiredService<IMemoryStore>(), home, read, identity, telemetry, this.logger);
        using var tools = new CapabilityToolInvoker(
            identity,
            services.GetRequiredService<ICapabilityRegistry>(),
            services.GetRequiredService<ICapabilityInvoker>(),
            new OperatorGates(breaker, services.GetRequiredService<IDecisionPolicy>(), services.GetRequiredService<IApprovalGate>(), this.ledger),
            this.options,
            telemetry,
            this.logger,
            this.timeProvider);
        var kernel = services.GetRequiredService<IAgentExecutionContextFactory>().Create(registration.Agent.Descriptor, grid);
        using var operation = kernel.OperationContext;
        using var context = new AgentExecutionContext(kernel, identity, tools, memory);
        return await this.RunAsync(registration.Agent, context, request, operation, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AgentResult> RunAsync(IAgent agent, AgentExecutionContext context, AgentRequest request, IOperationContext operation, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(this.options.ExecutionTimeout, this.timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            // WaitAsync stops waiting at the deadline even when an agent ignores its token; the context is then disposed.
            var result = await agent.ExecuteAsync(context, request, linked.Token).WaitAsync(linked.Token).ConfigureAwait(false);
            operation.Complete();
            return result ?? Refuse(AgentOutcome.Failed, "agent_error");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            operation.Fail("cancelled");
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            AgentLog.AgentTimedOut(this.logger, context.Agent.AgentId, context.ExecutionId);
            operation.Fail("timeout");
            return Refuse(AgentOutcome.Failed, "timeout");
        }
        catch (Exception exception) when (this.IsPropagated(exception))
        {
            AgentLog.AgentStopped(this.logger, context.Agent.AgentId, context.ExecutionId, exception.GetType().Name);
            operation.Fail(exception.GetType().Name);
            throw;
        }
        catch (Exception exception) when (!this.IsPropagated(exception))
        {
            // The message may carry model output or secrets; only the type name is recorded.
            AgentLog.AgentFailed(this.logger, context.Agent.AgentId, context.ExecutionId, exception.GetType().Name);
            operation.Fail(exception.GetType().Name);
            return Refuse(AgentOutcome.Failed, "agent_error");
        }
    }

    private bool IsPropagated(Exception exception) => this.propagated.Any(type => type.IsInstanceOfType(exception));

    private Entry Get(string agentId)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        return this.agents.TryGetValue(agentId, out var entry)
            ? entry
            : throw new InvalidOperationException($"No agent '{agentId}' is registered.");
    }

    private void Notify(string agentId, string? executionId, string transition, string? outcome)
    {
        var record = new AgentLifecycleTransition(agentId, executionId, transition, this.timeProvider.GetUtcNow(), outcome);
        AgentLog.Transition(this.logger, agentId, transition, executionId, outcome);
        foreach (var observer in this.observers)
        {
            try
            {
                observer.OnTransition(record);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                AgentLog.ObserverFailed(this.logger, agentId, transition, exception.GetType().Name);
            }
        }
    }

    private sealed class Entry(AgentRegistration registration)
    {
        private int phase = (int)AgentLifecyclePhase.Registered;

        public AgentRegistration Registration { get; } = registration;

        public AgentLifecyclePhase Phase => (AgentLifecyclePhase)Volatile.Read(ref this.phase);

        public bool TryMove(AgentLifecyclePhase from, AgentLifecyclePhase to) =>
            Interlocked.CompareExchange(ref this.phase, (int)to, (int)from) == (int)from;

        public void MoveTo(AgentLifecyclePhase to) => Interlocked.Exchange(ref this.phase, (int)to);
    }
}
