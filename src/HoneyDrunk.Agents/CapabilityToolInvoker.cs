using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Capabilities.Abstractions;
using HoneyDrunk.Kernel.Abstractions.Telemetry;
using HoneyDrunk.Operator.Abstractions;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace HoneyDrunk.Agents;

/// <summary>
/// One execution's tool invoker. Checks run in order and the first failure wins: disposal, the call limit, the
/// bindings, the registry, the agent's breaker, the decision policy (with the approval flow), then dispatch through
/// the Capabilities invoker, which runs the capability guard exactly once. Any Operator failure fails closed.
/// </summary>
internal sealed class CapabilityToolInvoker : IToolInvoker, IDisposable
{
    private const string Unbound = "unbound";

    private readonly ExecutionIdentity identity;
    private readonly ICapabilityRegistry registry;
    private readonly ICapabilityInvoker invoker;
    private readonly OperatorGates gates;
    private readonly AgentRuntimeOptions options;
    private readonly ITelemetryActivityFactory telemetry;
    private readonly ILogger logger;
    private readonly TimeProvider timeProvider;
    private readonly CancellationTokenSource lifetime = new();
    private int calls;
    private int disposed;

    /// <summary>Initializes a new instance of the <see cref="CapabilityToolInvoker"/> class.</summary>
    /// <param name="identity">Who the execution acts as.</param>
    /// <param name="registry">The capability registry.</param>
    /// <param name="invoker">The capability invoker.</param>
    /// <param name="gates">The Operator checks.</param>
    /// <param name="options">The runtime options.</param>
    /// <param name="telemetry">Kernel's activity factory.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="timeProvider">The clock.</param>
    public CapabilityToolInvoker(
        ExecutionIdentity identity,
        ICapabilityRegistry registry,
        ICapabilityInvoker invoker,
        OperatorGates gates,
        AgentRuntimeOptions options,
        ITelemetryActivityFactory telemetry,
        ILogger logger,
        TimeProvider timeProvider)
    {
        this.identity = identity ?? throw new ArgumentNullException(nameof(identity));
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        this.invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        this.gates = gates ?? throw new ArgumentNullException(nameof(gates));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public async Task<ToolCallResult> InvokeAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref this.disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(call);

        // Only a bound tool's own name reaches telemetry and logs; a proposed name may carry anything.
        var binding = this.identity.Bindings.FirstOrDefault(candidate => string.Equals(candidate.Name, call.Name, StringComparison.Ordinal)
            && string.Equals(candidate.Version, call.Version, StringComparison.Ordinal));
        var toolName = binding?.Name ?? Unbound;
        var toolVersion = binding?.Version ?? Unbound;
        using var activity = this.telemetry.Start(
            AgentTelemetry.ToolCallActivity,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [AgentTelemetry.AgentIdTag] = this.identity.AgentId,
                [AgentTelemetry.ExecutionIdTag] = this.identity.ExecutionId,
                [AgentTelemetry.ToolNameTag] = toolName,
                [AgentTelemetry.ToolVersionTag] = toolVersion,
            });

        // A call still in flight when the execution ends is cancelled, so nothing dispatches after the host has its result.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.lifetime.Token);
        var result = await this.InvokeCoreAsync(call, binding, activity, linked.Token).ConfigureAwait(false);
        activity?.SetTag(AgentTelemetry.ToolStatusTag, result.Status.ToString());
        AgentLog.ToolCallEnded(this.logger, this.identity.AgentId, toolName + "@" + toolVersion, result.Status.ToString(), result.ErrorCode);
        return result;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) == 0)
        {
            this.lifetime.Cancel();
            this.lifetime.Dispose();
        }
    }

    private static ToolCallResult Fail(ToolCallStatus status, string code, string? approvalId = null) => new(status, null, code, approvalId);

    private static ToolCallResult Map(CapabilityResult result) => result.Status switch
    {
        CapabilityResultStatus.Succeeded => new(ToolCallStatus.Succeeded, result.Output, null, null),
        CapabilityResultStatus.NotFound => Fail(ToolCallStatus.NotFound, result.ErrorCode ?? "not_found"),
        CapabilityResultStatus.Denied => Fail(ToolCallStatus.Denied, result.DenyCodes.Count > 0 ? result.DenyCodes[0] : result.ErrorCode ?? "denied"),
        CapabilityResultStatus.InvalidArguments => Fail(ToolCallStatus.InvalidArguments, result.ErrorCode ?? "invalid_arguments"),
        CapabilityResultStatus.Failed => Fail(ToolCallStatus.Failed, result.ErrorCode ?? "failed"),
        _ => Fail(ToolCallStatus.Failed, "failed"),
    };

    private async Task<ToolCallResult> InvokeCoreAsync(ToolCall call, AgentToolBinding? binding, Activity? activity, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref this.calls) > this.options.MaxToolCallsPerExecution)
        {
            return Fail(ToolCallStatus.LimitReached, "tool_call_limit");
        }

        if (binding is null)
        {
            return Fail(ToolCallStatus.NotBound, "not_bound");
        }

        CapabilityKey key;
        try
        {
            key = new CapabilityKey(call.Name, call.Version);
        }
        catch (ArgumentException)
        {
            return Fail(ToolCallStatus.NotFound, "not_found");
        }

        if (this.registry.Resolve(key) is null)
        {
            return Fail(ToolCallStatus.NotFound, "not_found");
        }

        try
        {
            var gateResult = await this.CheckGatesAsync(key, call.Arguments, activity, cancellationToken).ConfigureAwait(false);
            if (gateResult is not null)
            {
                return gateResult;
            }
        }
        catch (Exception exception) when (exception is not ObjectDisposedException && !this.IsPropagated(exception) && (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested))
        {
            AgentLog.GateFailed(this.logger, this.identity.AgentId, key.ToString(), exception.GetType().Name);
            activity?.SetTag(AgentTelemetry.GateOutcomeTag, "error");
            return Fail(ToolCallStatus.Failed, "gate_error");
        }

        this.ThrowIfDisposed();
        var caller = new CapabilityCaller(this.identity.Subject, this.identity.Grants.Scopes, this.identity.Grants.Roles);
        var result = await this.invoker.InvokeAsync(new CapabilityInvocation(key, call.Arguments, caller), cancellationToken).ConfigureAwait(false);
        return Map(result);
    }

    private async Task<ToolCallResult?> CheckGatesAsync(CapabilityKey key, JsonElement arguments, Activity? activity, CancellationToken cancellationToken)
    {
        if (!await this.gates.Breaker.IsAllowedAsync(this.identity.BreakerName, cancellationToken).ConfigureAwait(false))
        {
            activity?.SetTag(AgentTelemetry.GateKindTag, "breaker");
            activity?.SetTag(AgentTelemetry.GateOutcomeTag, "open");
            return Fail(ToolCallStatus.Halted, "breaker_open");
        }

        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["executionId"] = this.identity.ExecutionId,
            ["correlationId"] = this.identity.CorrelationId,
        };
        var decision = await this.gates.Policy.EvaluateAsync(
            new ActionContext(this.identity.AgentId, "tool." + key.Name, Resource(key), attributes),
            cancellationToken).ConfigureAwait(false);
        activity?.SetTag(AgentTelemetry.GateKindTag, "policy");
        activity?.SetTag(AgentTelemetry.GateOutcomeTag, decision.Outcome.ToString());
        return decision.Outcome switch
        {
            PolicyOutcome.Allow => null,
            PolicyOutcome.RequireApproval => await this.ApproveAsync(key, arguments, activity, cancellationToken).ConfigureAwait(false),
            PolicyOutcome.Deny => Fail(ToolCallStatus.Denied, "operator_denied"),
            _ => Fail(ToolCallStatus.Denied, "operator_denied"),
        };
    }

    private async Task<ToolCallResult?> ApproveAsync(CapabilityKey key, JsonElement arguments, Activity? activity, CancellationToken cancellationToken)
    {
        activity?.SetTag(AgentTelemetry.GateKindTag, "approval");
        var requestKey = this.gates.Ledger.RequestKeyFor(this.identity.AgentId, key, arguments);
        var (open, opened) = this.gates.Ledger.GetOrOpen(requestKey, this.Expiry());
        if (opened)
        {
            return await this.RaiseAsync(key, requestKey, open, activity, cancellationToken).ConfigureAwait(false);
        }

        if (!this.gates.Ledger.IsRequested(requestKey, open))
        {
            return Fail(ToolCallStatus.AwaitingApproval, "approval_pending", open);
        }

        var status = await this.gates.Gate.CheckStatusAsync(open, cancellationToken).ConfigureAwait(false);
        activity?.SetTag(AgentTelemetry.GateOutcomeTag, status?.Outcome.ToString() ?? "unknown");
        switch (status?.Outcome)
        {
            case ApprovalOutcome.Pending:
                return Fail(ToolCallStatus.AwaitingApproval, "approval_pending", open);
            case ApprovalOutcome.Denied:
                return Fail(ToolCallStatus.Denied, "approval_denied");
            case ApprovalOutcome.Approved when this.TryConsume(requestKey, open):
                return null;
            case ApprovalOutcome.Approved:
            case ApprovalOutcome.Expired:
            case null:
            default:
                // Stale, unknown or already consumed: open a fresh approval, unless another call already did.
                var (fresh, reopened) = this.gates.Ledger.Reopen(requestKey, open, this.Expiry());
                return reopened
                    ? await this.RaiseAsync(key, requestKey, fresh, activity, cancellationToken).ConfigureAwait(false)
                    : Fail(ToolCallStatus.AwaitingApproval, "approval_pending", fresh);
        }
    }

    private async Task<ToolCallResult?> RaiseAsync(CapabilityKey key, string requestKey, string approvalId, Activity? activity, CancellationToken cancellationToken)
    {
        var expiry = this.Expiry();
        var context = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tool"] = key.ToString(),
            ["requestKey"] = requestKey,
            ["executionId"] = this.identity.ExecutionId,
        };
        ApprovalDecision reply;
        var raised = false;
        try
        {
            reply = await this.gates.Gate.RequestAsync(
                new ApprovalRequest(approvalId, this.identity.Subject, "tool." + key.Name, context, Resource(key), expiry, this.identity.CorrelationId),
                cancellationToken).ConfigureAwait(false);
            raised = true;
        }
        finally
        {
            // If the gate never took the request, drop it so the next proposal raises a fresh one.
            if (!raised)
            {
                this.gates.Ledger.Abandon(requestKey, approvalId);
            }
        }

        this.gates.Ledger.MarkRequested(requestKey, approvalId);
        activity?.SetTag(AgentTelemetry.GateOutcomeTag, reply.Outcome.ToString());

        // One call raises at most one request: where the rules would raise again, the call waits instead.
        return reply.Outcome switch
        {
            ApprovalOutcome.Denied => Fail(ToolCallStatus.Denied, "approval_denied"),
            ApprovalOutcome.Approved when this.TryConsume(requestKey, approvalId) => null,
            ApprovalOutcome.Approved or ApprovalOutcome.Pending or ApprovalOutcome.Expired => Fail(ToolCallStatus.AwaitingApproval, "approval_pending", approvalId),
            _ => Fail(ToolCallStatus.AwaitingApproval, "approval_pending", approvalId),
        };
    }

    private static string Resource(CapabilityKey key) => "capability/" + key.Name + "/" + key.Version;

    // An approval is never consumed by an execution that has already ended.
    private bool TryConsume(string requestKey, string approvalId)
    {
        this.ThrowIfDisposed();
        return this.gates.Ledger.TryConsume(requestKey, approvalId);
    }

    private DateTimeOffset Expiry() => this.timeProvider.GetUtcNow() + this.options.ToolApprovalTtl;

    private bool IsPropagated(Exception exception) => this.options.PropagatedExceptionTypes.Any(type => type.IsInstanceOfType(exception));

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref this.disposed) != 0, this);
}
