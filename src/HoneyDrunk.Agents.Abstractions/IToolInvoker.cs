namespace HoneyDrunk.Agents.Abstractions;

/// <summary>
/// Calls a tool on an agent's behalf. The call must be in the agent's bindings, pass the operator's checks, and pass
/// the capability node's authorization, in that order.
/// </summary>
public interface IToolInvoker
{
    /// <summary>Invokes a tool.</summary>
    /// <param name="call">The proposed call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result; failures are reported as a status and code, not thrown.</returns>
    Task<ToolCallResult> InvokeAsync(ToolCall call, CancellationToken cancellationToken = default);
}
