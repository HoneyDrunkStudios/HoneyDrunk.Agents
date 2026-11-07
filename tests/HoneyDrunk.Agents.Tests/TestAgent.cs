using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Kernel.Abstractions.Agents;
using IAgentExecutionContext = HoneyDrunk.Agents.Abstractions.IAgentExecutionContext;

namespace HoneyDrunk.Agents.Tests;

/// <summary>An agent that runs a delegate and keeps the contexts it was given.</summary>
internal sealed class TestAgent(Func<IAgentExecutionContext, CancellationToken, Task<AgentResult>>? run = null, string agentId = InvokerFixture.AgentId) : IAgent
{
    private readonly List<IAgentExecutionContext> contexts = [];

    public IAgentDescriptor Descriptor { get; } = new AgentDescriptor(agentId, "Test", "test", "1.0.0");

    public IReadOnlyList<IAgentExecutionContext> Contexts => this.contexts;

    public int Runs => this.contexts.Count;

    public Task<AgentResult> ExecuteAsync(IAgentExecutionContext context, AgentRequest request, CancellationToken cancellationToken)
    {
        this.contexts.Add(context);
        return run is null
            ? Task.FromResult(new AgentResult(AgentOutcome.Completed, null, null, null))
            : run(context, cancellationToken);
    }
}
