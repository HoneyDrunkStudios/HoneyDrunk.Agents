namespace HoneyDrunk.Agents;

/// <summary>Bounds and settings for agent executions.</summary>
public sealed class AgentRuntimeOptions
{
    /// <summary>Gets or sets the most tool calls one execution may make. Default 8.</summary>
    public int MaxToolCallsPerExecution { get; set; } = 8;

    /// <summary>Gets or sets how long one execution may run. Default 60 seconds.</summary>
    public TimeSpan ExecutionTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Gets or sets how long a tool-call approval request stays open. Default 24 hours.</summary>
    public TimeSpan ToolApprovalTtl { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Gets exception types that an agent may throw to stop the host, such as a budget stop. An execution that
    /// throws one of these, or a type derived from one, rethrows it unchanged after cleanup instead of reporting
    /// <c>agent_error</c>. The runtime never retries it.
    /// </summary>
    public IList<Type> PropagatedExceptionTypes { get; } = [];
}
