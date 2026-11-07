namespace HoneyDrunk.Agents.Abstractions;

/// <summary>An agent with the grants and tools it is registered with. Each tool name is bound once.</summary>
/// <param name="Agent">The agent.</param>
/// <param name="Grants">The most the agent may ever hold.</param>
/// <param name="Tools">The tools the agent may call.</param>
public sealed record AgentRegistration(IAgent Agent, AgentGrants Grants, IReadOnlyList<AgentToolBinding> Tools)
{
    /// <summary>Gets the agent.</summary>
    public IAgent Agent { get; } = Agent ?? throw new ArgumentNullException(nameof(Agent));

    /// <summary>Gets the registered grants.</summary>
    public AgentGrants Grants { get; } = Grants ?? throw new ArgumentNullException(nameof(Grants));

    /// <summary>Gets a private copy of the tool bindings.</summary>
    public IReadOnlyList<AgentToolBinding> Tools { get; } = Validate(Tools);

    private static IReadOnlyList<AgentToolBinding> Validate(IReadOnlyList<AgentToolBinding> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        if (tools.Any(tool => tool is null))
        {
            throw new ArgumentException("Bindings must not be null.", nameof(tools));
        }

        if (tools.Select(tool => tool.Name).Distinct(StringComparer.Ordinal).Count() != tools.Count)
        {
            throw new ArgumentException("Each tool name may be bound once.", nameof(tools));
        }

        return tools.ToArray().AsReadOnly();
    }
}
