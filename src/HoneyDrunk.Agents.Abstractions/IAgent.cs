using HoneyDrunk.Kernel.Abstractions.Agents;

namespace HoneyDrunk.Agents.Abstractions;

/// <summary>An agent: something that runs one execution at a time inside a runtime-built context.</summary>
public interface IAgent
{
    /// <summary>Gets the agent's descriptor.</summary>
    IAgentDescriptor Descriptor { get; }

    /// <summary>Runs one execution. The context and everything reached through it are disposed when it completes.</summary>
    /// <param name="context">The execution context.</param>
    /// <param name="request">What the execution is asked to do.</param>
    /// <param name="cancellationToken">Cancels the execution.</param>
    /// <returns>The result.</returns>
    Task<AgentResult> ExecuteAsync(IAgentRunContext context, AgentRequest request, CancellationToken cancellationToken);
}
