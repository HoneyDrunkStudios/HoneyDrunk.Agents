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
    private readonly ExecutionIdentity identity;
    private readonly ICapabilityRegistry registry;
    private readonly ICapabilityInvoker invoker;
    private readonly OperatorGates gates;
    private readonly AgentRuntimeOptions options;
    private readonly ITelemetryActivityFactory telemetry;
    private readonly ILogger logger;
    private readonly TimeProvider timeProvider;
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
        using var activity = this.telemetry.Start(
            AgentTelemetry.ToolCallActivity,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [AgentTelemetry.AgentIdTag] = this.identity.AgentId,
                [AgentTelemetry.ExecutionIdTag] = this.identity.ExecutionId,
                [AgentTelemetry.ToolNameTag] = call.Name,
                [AgentTelemetry.ToolVersionTag] = call.Version,
            });
        var result = await this.InvokeCoreAsync(call, activity, cancellationToken).ConfigureAwait(false);
        activity?.SetTag(AgentTelemetry.ToolStatusTag, result.Status.ToString());
        AgentLog.ToolCallEnded(this.logger, this.identity.AgentId, call.Name + "@" + call.Version, result.Status.ToString(), result.ErrorCode);
        return result;
    }

    /// <inheritdoc />
    public void Dispose() => Interlocked.Exchange(ref this.disposed, 1);

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

    private async Task<ToolCallResult> InvokeCoreAsync(ToolCall call, Activity? activity, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref this.calls) > this.options.MaxToolCallsPerExecution)
        {
            return Fail(ToolCallStatus.LimitReached, "tool_call_limit");
        }

        if (!this.identity.Bindings.Any(binding => string.Equals(binding.Name, call.Name, StringComparison.Ordinal)
            && string.Equals(binding.Version, call.Version, StringComparison.Ordinal)))
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
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            AgentLog.GateFailed(this.logger, this.identity.AgentId, key.ToString(), exception.GetType().Name);
            activity?.SetTag(AgentTelemetry.GateOutcomeTag, "error");
            return Fail(ToolCallStatus.Failed, "gate_error");
        }

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
        var requestKey = ToolRequestKey.For(this.identity.AgentId, key, arguments);
        var open = this.gates.Ledger.OpenApprovalFor(requestKey);
        if (open is null)
        {
            return await this.RaiseAsync(key, requestKey, activity, cancellationToken).ConfigureAwait(false);
        }

        var status = await this.gates.Gate.CheckStatusAsync(open, cancellationToken).ConfigureAwait(false);
        activity?.SetTag(AgentTelemetry.GateOutcomeTag, status?.Outcome.ToString() ?? "unknown");
        return status?.Outcome switch
        {
            ApprovalOutcome.Pending => Fail(ToolCallStatus.AwaitingApproval, "approval_pending", open),
            ApprovalOutcome.Denied => Fail(ToolCallStatus.Denied, "approval_denied"),
            ApprovalOutcome.Approved when this.gates.Ledger.TryConsume(requestKey, open) => null,
            ApprovalOutcome.Approved or ApprovalOutcome.Expired or null => await this.RaiseAsync(key, requestKey, activity, cancellationToken).ConfigureAwait(false),
            _ => await this.RaiseAsync(key, requestKey, activity, cancellationToken).ConfigureAwait(false),
        };
    }

    private async Task<ToolCallResult?> RaiseAsync(CapabilityKey key, string requestKey, Activity? activity, CancellationToken cancellationToken)
    {
        var expiry = this.timeProvider.GetUtcNow() + this.options.ToolApprovalTtl;
        var approvalId = this.gates.Ledger.Open(requestKey, expiry);
        var context = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tool"] = key.ToString(),
            ["requestKey"] = requestKey,
            ["executionId"] = this.identity.ExecutionId,
        };
        var reply = await this.gates.Gate.RequestAsync(
            new ApprovalRequest(approvalId, this.identity.Subject, "tool." + key.Name, context, Resource(key), expiry, this.identity.CorrelationId),
            cancellationToken).ConfigureAwait(false);
        activity?.SetTag(AgentTelemetry.GateOutcomeTag, reply.Outcome.ToString());

        // One call raises at most one request: where the rules would raise again, the call waits instead.
        return reply.Outcome switch
        {
            ApprovalOutcome.Denied => Fail(ToolCallStatus.Denied, "approval_denied"),
            ApprovalOutcome.Approved when this.gates.Ledger.TryConsume(requestKey, approvalId) => null,
            ApprovalOutcome.Approved or ApprovalOutcome.Pending or ApprovalOutcome.Expired => Fail(ToolCallStatus.AwaitingApproval, "approval_pending", approvalId),
            _ => Fail(ToolCallStatus.AwaitingApproval, "approval_pending", approvalId),
        };
    }

    private static string Resource(CapabilityKey key) => "capability/" + key.Name + "/" + key.Version;
}
