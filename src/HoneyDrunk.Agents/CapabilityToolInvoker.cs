using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Capabilities.Abstractions;
using HoneyDrunk.Kernel.Abstractions.Context;
using HoneyDrunk.Operator.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace HoneyDrunk.Agents;

internal sealed class CapabilityToolInvoker(
    IServiceProvider services,
    IGridContext grid,
    string agentId,
    string executionId,
    AgentGrants grants,
    IReadOnlyList<AgentToolBinding> bindings,
    ToolApprovalLedger approvals,
    AgentRuntimeOptions options,
    ExecutionLease lease) : IToolInvoker
{
    private readonly HashSet<CapabilityKey> _bindings = bindings.Select(binding => new CapabilityKey(binding.Name, binding.Version)).ToHashSet();
    private int _attempts;

    public async Task<ToolCallResult> InvokeAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);
        using var linked = lease.Link(cancellationToken);
        if (Interlocked.Increment(ref this._attempts) > options.MaxToolCallsPerExecution)
        {
            return Failure(ToolCallStatus.LimitReached, "tool_call_limit");
        }

        var key = new CapabilityKey(call.Name, call.Version);
        if (!this._bindings.Contains(key))
        {
            return Failure(ToolCallStatus.NotBound, "not_bound");
        }

        if (services.GetRequiredService<ICapabilityRegistry>().Resolve(key) is null)
        {
            return Failure(ToolCallStatus.NotFound, "not_found");
        }

        var refusal = await this.CheckOperatorAsync(key, call.Arguments, linked.Token).ConfigureAwait(false);
        if (refusal is not null)
        {
            return refusal;
        }

        linked.Token.ThrowIfCancellationRequested();
        var caller = new CapabilityCaller($"agent:{agentId}", grants.Scopes, grants.Roles);
        var result = await services.GetRequiredService<ICapabilityInvoker>()
            .InvokeAsync(new CapabilityInvocation(key, call.Arguments, caller), linked.Token).ConfigureAwait(false);
        return result.Status switch
        {
            CapabilityResultStatus.Succeeded => new ToolCallResult(ToolCallStatus.Succeeded, result.Output is { ValueKind: not JsonValueKind.Undefined } ? result.Output : null, null, null),
            CapabilityResultStatus.NotFound => Failure(ToolCallStatus.NotFound, "not_found"),
            CapabilityResultStatus.Denied => Failure(ToolCallStatus.Denied, result.DenyCodes.Count > 0 ? result.DenyCodes[0] : "denied"),
            CapabilityResultStatus.InvalidArguments => Failure(ToolCallStatus.InvalidArguments, "invalid_arguments"),
            CapabilityResultStatus.Failed => Failure(ToolCallStatus.Failed, result.ErrorCode ?? "tool_error"),
            _ => Failure(ToolCallStatus.Failed, "tool_error"),
        };
    }

    private static ToolCallResult Failure(ToolCallStatus status, string code) => new(status, null, code, null);

    private async Task<ToolCallResult?> CheckOperatorAsync(CapabilityKey key, JsonElement arguments, CancellationToken cancellationToken)
    {
        try
        {
            if (!await services.GetRequiredService<ICircuitBreaker>().IsAllowedAsync($"agent:{agentId}", cancellationToken).ConfigureAwait(false))
            {
                return Failure(ToolCallStatus.Halted, "breaker_open");
            }

            var action = $"tool.{key.Name}";
            var resource = $"capability/{key.Name}/{key.Version}";
            var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["executionId"] = executionId,
                ["correlationId"] = grid.CorrelationId,
            };
            var policy = await services.GetRequiredService<IDecisionPolicy>()
                .EvaluateAsync(new ActionContext(agentId, action, resource, attributes), cancellationToken).ConfigureAwait(false);
            return policy.Outcome switch
            {
                PolicyOutcome.Allow => null,
                PolicyOutcome.Deny => Failure(ToolCallStatus.Denied, "operator_denied"),
                PolicyOutcome.RequireApproval => await this.CheckApprovalAsync(key, arguments, action, resource, cancellationToken).ConfigureAwait(false),
                _ => Failure(ToolCallStatus.Failed, "gate_error"),
            };
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return Failure(ToolCallStatus.Failed, "gate_error");
        }
    }

    private Task<ToolCallResult?> CheckApprovalAsync(
        CapabilityKey key,
        JsonElement arguments,
        string action,
        string resource,
        CancellationToken cancellationToken)
    {
        var requestKey = ToolRequestKey.Create(grid.TenantId, agentId, key, arguments);
        var context = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tool"] = key.ToString(),
            ["requestKey"] = requestKey,
            ["executionId"] = executionId,
        };
        var request = new ApprovalRequest(string.Empty, $"agent:{agentId}", action, context, resource, default, grid.CorrelationId);
        return approvals.CheckAsync(requestKey, request, services.GetRequiredService<IApprovalGate>(), options.ToolApprovalTtl, cancellationToken);
    }
}
