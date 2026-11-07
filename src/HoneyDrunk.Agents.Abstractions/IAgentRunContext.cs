namespace HoneyDrunk.Agents.Abstractions;

/// <summary>Extends the Kernel context with the resources granted to one execution.</summary>
public interface IAgentRunContext : Kernel.Abstractions.Agents.IAgentExecutionContext
{
    /// <summary>Gets the execution identifier.</summary>
    string ExecutionId { get; }

    /// <summary>Gets the effective grants for this execution.</summary>
    AgentGrants Grants { get; }

    /// <summary>Gets the exact tool bindings registered for this agent.</summary>
    IReadOnlyList<AgentToolBinding> ToolBindings { get; }

    /// <summary>Gets access to tools for the lifetime of this execution.</summary>
    IToolInvoker Tools { get; }

    /// <summary>Gets access to memory for the lifetime of this execution.</summary>
    IAgentMemory Memory { get; }
}
