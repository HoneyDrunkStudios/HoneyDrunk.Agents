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
/// everything execution-scoped when it completes. The grid context is read once from the ambient accessor and the
/// execution's operation context is built over it, so scoped Kernel state in the new scope is never relied on.
/// Agent exceptions become <c>agent_error</c>, except configured stop exceptions, which are rethrown unchanged after
/// cleanup and never retried.
/// </summary>
public sealed partial class InProcessAgentLifecycle : IAgentLifecycle
{
    private readonly ConcurrentDictionary<string, Entry> agents = new(StringComparer.Ordinal);
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ToolApprovalLedger ledger;
    private readonly IAgentLifecycleObserver[] observers;
    private readonly AgentRuntimeOptions runtimeOptions;
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
    /// <exception cref="ArgumentException">An option is out of range, or a propagated type does not derive from <see cref="Exception"/>.</exception>
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
        this.runtimeOptions = options?.Value ?? throw new ArgumentNullException(nameof(options));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.propagated = this.runtimeOptions.PropagatedExceptionTypes.ToArray();
        Validate(this.runtimeOptions, this.propagated);
    }

    /// <inheritdoc />
    public void Register(AgentRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        // The id and descriptor are captured once, so a descriptor that changes later cannot move the agent to
        // another breaker or memory scope.
        var descriptor = registration.Agent.Descriptor;
        var agentId = descriptor?.AgentId;
        if (descriptor is null || agentId is null || !AgentIdPattern().IsMatch(agentId))
        {
            throw new ArgumentException("The agent id must be 1 to 64 characters of a-z, 0-9, '.', '_' or '-', starting with a letter or digit.", nameof(registration));
        }

        if (!this.agents.TryAdd(agentId, new Entry(agentId, descriptor, registration)))
        {
            throw new InvalidOperationException($"An agent with id '{agentId}' is already registered.");
        }

        this.Notify(agentId, null, "register", null);
    }

    /// <inheritdoc />
    public async Task InitializeAsync(string agentId, CancellationToken cancellationToken = default)
    {
        var entry = this.Get(agentId);
        RequirePhase(entry, AgentLifecyclePhase.Registered, "initialize");
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
        RequirePhase(entry, AgentLifecyclePhase.Initialized, "execute");

        // One execution at a time per agent, as IAgent promises; an agent abandoned at its deadline keeps the slot
        // until its task really ends.
        var admission = entry.TryEnter();
        if (admission is null)
        {
            AgentLog.AgentBusy(this.logger, entry.AgentId);
            return Refuse(AgentOutcome.Rejected, "agent_busy");
        }

        try
        {
            return await this.ExecuteAdmittedAsync(admission, request, options, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            admission.Exit();
        }
    }

    /// <inheritdoc />
    public Task DecommissionAsync(string agentId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = this.Get(agentId);

        // Executions already past the phase check finish; new ones are refused. Repeating is a no-op.
        if (entry.TryMove(AgentLifecyclePhase.Initialized, AgentLifecyclePhase.Decommissioned)
            || entry.TryMove(AgentLifecyclePhase.Registered, AgentLifecyclePhase.Decommissioned))
        {
            this.Notify(entry.AgentId, null, "decommission", null);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public AgentLifecyclePhase GetPhase(string agentId) => this.Get(agentId).Phase;

    private async Task<AgentResult> ExecuteAdmittedAsync(Admission admission, AgentRequest request, AgentExecutionOptions options, CancellationToken cancellationToken)
    {
        var entry = admission.Entry;
        var executionId = Guid.CreateVersion7(this.timeProvider.GetUtcNow()).ToString("N");
        var scope = this.scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var services = scope.ServiceProvider;
            var grid = ReadGridContext(services);
            using var activity = grid is { IsInitialized: true }
                ? services.GetRequiredService<ITelemetryActivityFactory>().StartExplicit(
                    AgentTelemetry.ExecuteActivity,
                    grid,
                    null,
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [AgentTelemetry.AgentIdTag] = entry.AgentId,
                        [AgentTelemetry.ExecutionIdTag] = executionId,
                    })
                : null;
            this.Notify(entry.AgentId, executionId, "execute", null);
            var outcome = "error";
            try
            {
                var result = await this.ExecuteInScopeAsync(admission, executionId, grid, request, options, services, cancellationToken).ConfigureAwait(false);
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
                this.Notify(entry.AgentId, executionId, "complete", outcome);
            }
        }
    }

    private static void Validate(AgentRuntimeOptions options, Type[] propagated)
    {
        if (options.MaxToolCallsPerExecution < 1)
        {
            throw new ArgumentException("MaxToolCallsPerExecution must be at least 1.", nameof(options));
        }

        if (options.ExecutionTimeout <= TimeSpan.Zero || options.ExecutionTimeout > TimeSpan.FromMilliseconds(int.MaxValue))
        {
            throw new ArgumentException("ExecutionTimeout must be positive and at most about 24 days.", nameof(options));
        }

        if (options.ToolApprovalTtl <= TimeSpan.Zero)
        {
            throw new ArgumentException("ToolApprovalTtl must be positive.", nameof(options));
        }

        if (propagated.Any(type => type is null || !typeof(Exception).IsAssignableFrom(type)))
        {
            throw new ArgumentException("Every propagated type must derive from System.Exception.", nameof(options));
        }
    }

    private static void RequirePhase(Entry entry, AgentLifecyclePhase required, string action)
    {
        var phase = entry.Phase;
        if (phase != required)
        {
            throw new InvalidOperationException($"Agent '{entry.AgentId}' is {phase}; {action} needs {required}.");
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
            // Kernel's accessors throw when no context is set for this request or flow.
            return null;
        }
    }

    private static AgentResult Refuse(AgentOutcome outcome, string code) => new(outcome, null, code, null);

    [GeneratedRegex(@"\A[a-z0-9][a-z0-9._-]{0,63}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AgentIdPattern();

    private async Task<AgentResult> ExecuteInScopeAsync(
        Admission admission,
        string executionId,
        IGridContext? grid,
        AgentRequest request,
        AgentExecutionOptions executionOptions,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var entry = admission.Entry;
        if (grid is null || !grid.IsInitialized)
        {
            return Refuse(AgentOutcome.Failed, "grid_context_missing");
        }

        if (executionOptions.Memory.Tenant != grid.TenantId)
        {
            return Refuse(AgentOutcome.Rejected, "tenant_mismatch");
        }

        var registration = entry.Registration;
        var identity = new ExecutionIdentity(entry.AgentId, executionId, grid.CorrelationId, registration.Grants.Narrow(executionOptions.NarrowGrantsTo), registration.Tools, grid.TenantId);

        // The deadline covers the pre-run checks too, so a slow breaker or memory authorization cannot hold a run.
        using var timeout = new CancellationTokenSource(this.runtimeOptions.ExecutionTimeout, this.timeProvider);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var breaker = services.GetRequiredService<ICircuitBreaker>();
        try
        {
            if (!await breaker.IsAllowedAsync(identity.BreakerName, deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false))
            {
                return Refuse(AgentOutcome.Halted, "breaker_open");
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            AgentLog.AgentTimedOut(this.logger, entry.AgentId, executionId);
            return Refuse(AgentOutcome.Failed, "timeout");
        }
        catch (Exception exception) when (this.IsRefusable(exception, cancellationToken))
        {
            AgentLog.AgentFailed(this.logger, entry.AgentId, executionId, exception.GetType().Name);
            return Refuse(AgentOutcome.Failed, "gate_error");
        }

        IMemoryScope home;
        IMemoryScope read;
        try
        {
            (home, read) = await AgentMemoryScopes.OpenAsync(services.GetRequiredService<IMemoryScopeResolver>(), identity, executionOptions.Memory, deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (MemoryEscalationDeniedException)
        {
            return Refuse(AgentOutcome.Rejected, "memory_escalation_denied");
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            AgentLog.AgentTimedOut(this.logger, entry.AgentId, executionId);
            return Refuse(AgentOutcome.Failed, "timeout");
        }
        catch (Exception exception) when (this.IsRefusable(exception, cancellationToken))
        {
            AgentLog.AgentFailed(this.logger, entry.AgentId, executionId, exception.GetType().Name);
            return Refuse(AgentOutcome.Failed, "memory_error");
        }

        var telemetry = services.GetRequiredService<ITelemetryActivityFactory>();
        using var memory = new MemoryAgentMemory(services.GetRequiredService<IMemoryStore>(), home, read, identity, telemetry, this.logger);
        using var tools = new CapabilityToolInvoker(
            identity,
            services.GetRequiredService<ICapabilityRegistry>(),
            services.GetRequiredService<ICapabilityInvoker>(),
            new OperatorGates(breaker, services.GetRequiredService<IDecisionPolicy>(), services.GetRequiredService<IApprovalGate>(), this.ledger),
            this.runtimeOptions,
            telemetry,
            this.logger,
            this.timeProvider);
        using var operation = new AgentOperationContext(grid, "Agent:" + entry.AgentId, executionId, this.timeProvider);
        var kernel = services.GetRequiredService<IAgentExecutionContextFactory>().Create(entry.Descriptor, grid, operation);
        using var context = new AgentRunContext(kernel, identity, tools, memory);
        return await this.RunAsync(admission, context, request, operation, timeout, deadline.Token, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AgentResult> RunAsync(
        Admission admission,
        AgentRunContext context,
        AgentRequest request,
        IOperationContext operation,
        CancellationTokenSource timeout,
        CancellationToken deadline,
        CancellationToken cancellationToken)
    {
        Task<AgentResult>? running = null;
        try
        {
            // WaitAsync stops waiting at the deadline even when an agent ignores its token; the context is then disposed.
            running = admission.Entry.Registration.Agent.ExecuteAsync(context, request, deadline);
            var result = await running.WaitAsync(deadline).ConfigureAwait(false);
            if (result is null)
            {
                operation.Fail("agent_error");
                return Refuse(AgentOutcome.Failed, "agent_error");
            }

            // Only a completed run completes the Kernel operation, so the two never disagree.
            if (result.Outcome == AgentOutcome.Completed)
            {
                operation.Complete();
            }
            else
            {
                operation.Fail(result.Outcome.ToString());
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            operation.Fail("cancelled");
            this.ObserveAbandoned(running, context);
            admission.DeferUntil(running);
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            AgentLog.AgentTimedOut(this.logger, context.AgentIdForLogs, context.ExecutionId);
            this.ObserveAbandoned(running, context);
            admission.DeferUntil(running);
            operation.Fail("timeout");
            return Refuse(AgentOutcome.Failed, "timeout");
        }
        catch (Exception exception) when (this.IsPropagated(exception))
        {
            AgentLog.AgentStopped(this.logger, context.AgentIdForLogs, context.ExecutionId, exception.GetType().Name);
            operation.Fail(exception.GetType().Name);
            throw;
        }
        catch (Exception exception) when (!this.IsPropagated(exception))
        {
            // The message may carry model output or secrets; only the type name is recorded.
            AgentLog.AgentFailed(this.logger, context.AgentIdForLogs, context.ExecutionId, exception.GetType().Name);
            operation.Fail(exception.GetType().Name);
            return Refuse(AgentOutcome.Failed, "agent_error");
        }
    }

    // An agent abandoned at its deadline may still fault later; observe it so the fault is logged, not lost.
    private void ObserveAbandoned(Task<AgentResult>? running, AgentRunContext context)
    {
        if (running is null || running.IsCompleted)
        {
            return;
        }

        var agentId = context.AgentIdForLogs;
        var executionId = context.ExecutionId;
        _ = running.ContinueWith(
            task => AgentLog.AgentFailed(this.logger, agentId, executionId, task.Exception?.InnerException?.GetType().Name ?? "unknown"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private bool IsPropagated(Exception exception) => this.propagated.Any(type => type.IsInstanceOfType(exception));

    private bool IsRefusable(Exception exception, CancellationToken cancellationToken) =>
        !this.IsPropagated(exception) && (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested);

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

    private sealed class Entry(string agentId, IAgentDescriptor descriptor, AgentRegistration registration)
    {
        private int phase = (int)AgentLifecyclePhase.Registered;
        private long owner;
        private long generations;

        public string AgentId { get; } = agentId;

        public IAgentDescriptor Descriptor { get; } = descriptor;

        public AgentRegistration Registration { get; } = registration;

        public AgentLifecyclePhase Phase => (AgentLifecyclePhase)Volatile.Read(ref this.phase);

        /// <summary>
        /// Takes the agent's single execution slot, if it is free. Each admission has its own generation, so a late
        /// release can never free a slot that a newer admission holds.
        /// </summary>
        /// <returns>The admission, or null when the agent is busy.</returns>
        public Admission? TryEnter()
        {
            var generation = Interlocked.Increment(ref this.generations);
            return Interlocked.CompareExchange(ref this.owner, generation, 0) == 0 ? new Admission(this, generation) : null;
        }

        /// <summary>Frees the slot only while the given admission still owns it.</summary>
        /// <param name="generation">The admission's generation.</param>
        public void Release(long generation) => Interlocked.CompareExchange(ref this.owner, 0, generation);

        public bool TryMove(AgentLifecyclePhase from, AgentLifecyclePhase to) =>
            Interlocked.CompareExchange(ref this.phase, (int)to, (int)from) == (int)from;
    }

    /// <summary>One execution's hold on its agent's slot. The decision to defer is local to this admission.</summary>
    private sealed class Admission(Entry entry, long generation)
    {
        private int deferred;

        public Entry Entry { get; } = entry;

        /// <summary>Keeps the slot until an agent task that is still running really ends.</summary>
        /// <param name="running">The task, if one was started.</param>
        public void DeferUntil(Task? running)
        {
            if (running is null || running.IsCompleted || Interlocked.Exchange(ref this.deferred, 1) == 1)
            {
                return;
            }

            _ = running.ContinueWith(
                _ => this.Entry.Release(generation),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        /// <summary>Frees the slot when the execution ends, unless it was deferred to a running task.</summary>
        public void Exit()
        {
            if (Volatile.Read(ref this.deferred) == 0)
            {
                this.Entry.Release(generation);
            }
        }
    }
}
