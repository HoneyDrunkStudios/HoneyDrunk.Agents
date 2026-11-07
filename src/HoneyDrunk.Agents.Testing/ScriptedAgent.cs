using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Kernel.Abstractions.Agents;
using IAgentExecutionContext = HoneyDrunk.Agents.Abstractions.IAgentExecutionContext;

namespace HoneyDrunk.Agents.Testing;

/// <summary>An agent that runs a delegate and records each context it was given.</summary>
public sealed class ScriptedAgent : IAgent
{
    private readonly Func<IAgentExecutionContext, CancellationToken, Task<AgentResult>>? run;
    private readonly List<IAgentExecutionContext> contexts = [];

    /// <summary>Initializes a new instance of the <see cref="ScriptedAgent"/> class.</summary>
    /// <param name="agentId">The agent id.</param>
    /// <param name="run">What each execution does; by default it completes with no output.</param>
    public ScriptedAgent(string agentId, Func<IAgentExecutionContext, CancellationToken, Task<AgentResult>>? run = null)
    {
        this.Descriptor = new AgentDescriptor(agentId, agentId, "scripted", "1.0.0");
        this.run = run;
    }

    /// <inheritdoc />
    public IAgentDescriptor Descriptor { get; }

    /// <summary>Gets the contexts the agent was given, in order.</summary>
    public IReadOnlyList<IAgentExecutionContext> Contexts
    {
        get
        {
            lock (this.contexts)
            {
                return [.. this.contexts];
            }
        }
    }

    /// <inheritdoc />
    public Task<AgentResult> ExecuteAsync(IAgentExecutionContext context, AgentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (this.contexts)
        {
            this.contexts.Add(context);
        }

        return this.run is null
            ? Task.FromResult(new AgentResult(AgentOutcome.Completed, null, null, null))
            : this.run(context, cancellationToken);
    }
}
