namespace HoneyDrunk.Agents.Abstractions;

/// <summary>Invokes tools through the execution's bindings and authorization checks.</summary>
public interface IToolInvoker
{
    /// <summary>Requests one exact capability invocation.</summary>
    /// <param name="call">The capability key and arguments.</param>
    /// <param name="cancellationToken">The call cancellation token.</param>
    /// <returns>The invocation result.</returns>
    Task<ToolCallResult> InvokeAsync(ToolCall call, CancellationToken cancellationToken = default);
}
