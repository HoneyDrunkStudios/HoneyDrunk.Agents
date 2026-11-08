using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Kernel.Abstractions.Agents;

namespace HoneyDrunk.Agents.Tests;

/// <summary>An agent that runs a delegate and keeps the contexts it was given.</summary>
internal sealed class TestAgent(Func<IAgentRunContext, CancellationToken, Task<AgentResult>>? run = null, string agentId = InvokerFixture.AgentId) : IAgent
{
    private readonly List<IAgentRunContext> contexts = [];

    public IAgentDescriptor Descriptor { get; } = new AgentDescriptor(agentId, "Test", "test", "1.0.0");

    public IReadOnlyList<IAgentRunContext> Contexts => this.contexts;

    public int Runs => this.contexts.Count;

    public Task<AgentResult> ExecuteAsync(IAgentRunContext context, AgentRequest request, CancellationToken cancellationToken)
    {
        this.contexts.Add(context);
        return run is null
            ? Task.FromResult(new AgentResult(AgentOutcome.Completed, null, null, null))
            : run(context, cancellationToken);
    }
}
