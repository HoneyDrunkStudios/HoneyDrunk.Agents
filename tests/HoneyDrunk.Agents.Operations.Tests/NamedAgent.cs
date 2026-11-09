using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Kernel.Abstractions.Agents;

namespace HoneyDrunk.Agents.Operations.Tests;

/// <summary>An agent with any descriptor name, even one a display would have to make safe.</summary>
internal sealed class NamedAgent(string agentId, string name, Func<IAgentRunContext, AgentRequest, Task<AgentResult>>? run = null) : IAgent
{
    public IAgentDescriptor Descriptor { get; } = new LooseDescriptor(agentId, name);

    public Task<AgentResult> ExecuteAsync(IAgentRunContext context, AgentRequest request, CancellationToken cancellationToken) =>
        run is null ? Task.FromResult(new AgentResult(AgentOutcome.Completed, null, null, null)) : run(context, request);

    /// <summary>A descriptor that does not validate its name.</summary>
    private sealed class LooseDescriptor(string agentId, string name) : IAgentDescriptor
    {
        public string AgentId { get; } = agentId;

        public string Name { get; } = name;

        public string AgentType => "test";

        public string Version => "1.0.0";

        public IReadOnlyList<IAgentCapability> Capabilities => [];

        public AgentContextScope ContextScope => AgentContextScope.Standard;

        public IReadOnlyDictionary<string, string> Metadata { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        public bool HasCapability(string capabilityName) => false;
    }
}
