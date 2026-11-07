using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Capabilities.Abstractions;
using HoneyDrunk.Kernel.Abstractions.Agents;
using HoneyDrunk.Kernel.Abstractions.Context;
using HoneyDrunk.Memory.Abstractions;
using HoneyDrunk.Operator.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace HoneyDrunk.Agents;

internal sealed class InProcessAgentLifecycle(
    IServiceScopeFactory scopeFactory,
    ToolApprovalLedger approvals,
    IEnumerable<IAgentLifecycleObserver> observers,
    AgentRuntimeOptions options,
    TimeProvider timeProvider) : IAgentLifecycle
{
    private readonly ConcurrentDictionary<string, Registration> _agents = new(StringComparer.Ordinal);
    private readonly IAgentLifecycleObserver[] _observers = observers.ToArray();

    public void Register(AgentRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var descriptor = registration.Agent.Descriptor;
        ArgumentNullException.ThrowIfNull(descriptor);
        var identity = new AgentDescriptor(descriptor.AgentId, descriptor.Name, descriptor.AgentType, descriptor.Version);
        if (!this._agents.TryAdd(identity.AgentId, new Registration(identity, registration)))
        {
            throw new InvalidOperationException($"Agent '{identity.AgentId}' is already registered.");
        }

        this.Observe(identity.AgentId, null, "register", null);
    }

    public async Task InitializeAsync(string agentId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var registration = this.Find(agentId);
        await using var scope = scopeFactory.CreateAsyncScope();
        lock (registration.Sync)
        {
            RequirePhase(registration, AgentLifecyclePhase.Registered);
            if (registration.Binding.Tools.Count > 0)
            {
                var registry = scope.ServiceProvider.GetRequiredService<ICapabilityRegistry>();
                foreach (var binding in registration.Binding.Tools)
                {
                    if (registry.Resolve(new CapabilityKey(binding.Name, binding.Version)) is null)
                    {
                        throw new InvalidOperationException($"Agent '{agentId}' has an unregistered binding '{binding.Name}@{binding.Version}'.");
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            registration.Phase = AgentLifecyclePhase.Initialized;
        }

        this.Observe(agentId, null, "initialize", null);
    }

    public async Task<AgentResult> ExecuteAsync(
        string agentId,
        AgentRequest request,
        AgentExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        var registration = this.Find(agentId);
        lock (registration.Sync)
        {
            RequirePhase(registration, AgentLifecyclePhase.Initialized);
        }

        var executionId = Guid.CreateVersion7(timeProvider.GetUtcNow()).ToString("N");
        this.Observe(agentId, executionId, "execute", null);
        var outcome = "failed";
        try
        {
            var result = await this.RunAsync(registration, executionId, request, options, cancellationToken).ConfigureAwait(false);
            outcome = result.Outcome.ToString().ToLowerInvariant();
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "canceled";
            throw;
        }
        catch (Exception error) when (this.ShouldPropagate(error))
        {
            outcome = "propagated";
            throw;
        }
        catch (Exception)
        {
            return Result(AgentOutcome.Failed, "agent_error");
        }
        finally
        {
            this.Observe(agentId, executionId, "complete", outcome);
        }
    }

    public Task DecommissionAsync(string agentId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var registration = this.Find(agentId);
        lock (registration.Sync)
        {
            RequirePhase(registration, AgentLifecyclePhase.Initialized);
            registration.Phase = AgentLifecyclePhase.Decommissioned;
        }

        this.Observe(agentId, null, "decommission", null);
        return Task.CompletedTask;
    }

    public AgentLifecyclePhase GetPhase(string agentId)
    {
        var registration = this.Find(agentId);
        lock (registration.Sync)
        {
            return registration.Phase;
        }
    }

    private static AgentResult Result(AgentOutcome outcome, string reason) => new(outcome, null, reason, null);

    private static IGridContext? ReadGrid(IServiceProvider services)
    {
        var accessor = services.GetService<IGridContextAccessor>();
        try
        {
            return accessor?.GridContext;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void RequirePhase(Registration registration, AgentLifecyclePhase expected)
    {
        if (registration.Phase != expected)
        {
            throw new InvalidOperationException($"Agent '{registration.Identity.AgentId}' is in phase {registration.Phase}. Expected {expected}.");
        }
    }

    private Registration Find(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return this._agents.TryGetValue(id, out var registration)
            ? registration
            : throw new KeyNotFoundException($"Agent '{id}' is not registered.");
    }

    private async Task<AgentResult> RunAsync(
        Registration registration,
        string executionId,
        AgentRequest request,
        AgentExecutionOptions executionOptions,
        CancellationToken cancellationToken)
    {
        var agentId = registration.Identity.AgentId;
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        using var deadline = new CancellationTokenSource(options.ExecutionTimeout, timeProvider);
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var lease = new ExecutionLease(execution.Token);
        IAgentExecutionContext? kernel = null;
        IOperationContext? operation = null;
        try
        {
            var grid = ReadGrid(services);
            if (grid is not { IsInitialized: true })
            {
                return Result(AgentOutcome.Failed, "grid_context_missing");
            }

            if (grid.TenantId != executionOptions.Memory.Tenant)
            {
                return Result(AgentOutcome.Rejected, "tenant_mismatch");
            }

            var breaker = services.GetRequiredService<ICircuitBreaker>();
            if (!await breaker.IsAllowedAsync($"agent:{agentId}", execution.Token).ConfigureAwait(false))
            {
                return Result(AgentOutcome.Halted, "breaker_open");
            }

            var grants = registration.Binding.Grants.Narrow(executionOptions.NarrowGrantsTo);
            var (home, read) = await AgentMemoryScopes.OpenAsync(
                services.GetRequiredService<IMemoryScopeResolver>(), agentId, grants, executionOptions.Memory, execution.Token).ConfigureAwait(false);
            operation = options.CreateOperationContext?.Invoke(grid, $"Agent:{agentId}");
            kernel = services.GetRequiredService<IAgentExecutionContextFactory>().Create(registration.Identity, grid, operation);
            operation ??= kernel.OperationContext;
            var tools = new CapabilityToolInvoker(services, grid, agentId, executionId, grants, registration.Binding.Tools, approvals, options, lease);
            var memory = new MemoryAgentMemory(services.GetRequiredService<IMemoryStore>(), home, read, lease);
            var context = new AgentRunContext(kernel, executionId, grants, registration.Binding.Tools, tools, memory, lease);

            var result = await registration.Binding.Agent.ExecuteAsync(context, request, execution.Token).ConfigureAwait(false);
            execution.Token.ThrowIfCancellationRequested();
            if (result.Outcome == AgentOutcome.Completed)
            {
                operation.Complete();
            }
            else
            {
                operation.Fail("agent_did_not_complete");
            }

            return result;
        }
        catch (MemoryEscalationDeniedException) when (kernel is null)
        {
            return Result(AgentOutcome.Rejected, "memory_escalation_denied");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            operation?.Fail("caller_canceled");
            throw;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            operation?.Fail("timeout");
            return Result(AgentOutcome.Failed, "timeout");
        }
        catch (Exception error) when (this.ShouldPropagate(error))
        {
            operation?.Fail(error.GetType().Name);
            throw;
        }
        catch (Exception)
        {
            operation?.Fail("agent_error");
            return Result(AgentOutcome.Failed, "agent_error");
        }
        finally
        {
            var drained = lease.EndAsync();
            try
            {
                await execution.CancelAsync().ConfigureAwait(false);
            }
            catch (AggregateException)
            {
                // A workload's cancellation callback cannot change the execution result.
            }

            await drained.ConfigureAwait(false);
            if (kernel is IAsyncDisposable asyncKernel)
            {
                await asyncKernel.DisposeAsync().ConfigureAwait(false);
            }
            else if (kernel is IDisposable disposableKernel)
            {
                disposableKernel.Dispose();
            }

            operation?.Dispose();
        }
    }

    private bool ShouldPropagate(Exception error) =>
        options.PropagatedExceptionTypes.Any(type => type.IsInstanceOfType(error));

    private void Observe(string agentId, string? executionId, string transition, string? outcome)
    {
        var message = new AgentLifecycleTransition(agentId, executionId, transition, timeProvider.GetUtcNow(), outcome);
        foreach (var observer in this._observers)
        {
            try
            {
                observer.OnTransition(message);
            }
            catch (Exception)
            {
                // Observers cannot change execution state or suppress other observers.
            }
        }
    }

    private sealed class Registration(AgentDescriptor identity, AgentRegistration binding)
    {
        public Lock Sync { get; } = new();

        public AgentDescriptor Identity { get; } = identity;

        public AgentRegistration Binding { get; } = binding;

        public AgentLifecyclePhase Phase { get; set; } = AgentLifecyclePhase.Registered;
    }
}
