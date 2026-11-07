using HoneyDrunk.Kernel.Abstractions.Agents;

namespace HoneyDrunk.Agents.Abstractions;

/// <summary>Executes one workload request with the context supplied by its host.</summary>
public interface IAgent
{
    /// <summary>Gets the workload's Grid identity.</summary>
    IAgentDescriptor Descriptor { get; }

    /// <summary>Executes one request.</summary>
    /// <param name="context">The context valid for this execution.</param>
    /// <param name="request">The workload request.</param>
    /// <param name="cancellationToken">The execution cancellation token.</param>
    /// <returns>The workload result.</returns>
    Task<AgentResult> ExecuteAsync(IAgentExecutionContext context, AgentRequest request, CancellationToken cancellationToken);
}
