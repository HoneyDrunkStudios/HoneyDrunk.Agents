namespace HoneyDrunk.Agents.Abstractions;

/// <summary>
/// One execution's context: Kernel's agent context plus the effective grants, the tool bindings, tool invocation
/// and scoped memory. It holds execution-scoped state only and is disposed when the execution completes; Kernel's
/// <c>ExecutionMetadata</c> carries short-term working state.
/// </summary>
public interface IAgentExecutionContext : HoneyDrunk.Kernel.Abstractions.Agents.IAgentExecutionContext
{
    /// <summary>Gets the execution id.</summary>
    string ExecutionId { get; }

    /// <summary>Gets the effective grants: the registered grants narrowed for this execution.</summary>
    AgentGrants Grants { get; }

    /// <summary>Gets the tools the agent may call.</summary>
    IReadOnlyList<AgentToolBinding> ToolBindings { get; }

    /// <summary>Gets tool invocation for this execution.</summary>
    IToolInvoker Tools { get; }

    /// <summary>Gets scoped memory for this execution.</summary>
    IAgentMemory Memory { get; }
}
