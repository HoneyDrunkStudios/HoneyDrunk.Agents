namespace HoneyDrunk.Agents.Abstractions;

/// <summary>A host registration with the agent's maximum grants and exact tool bindings.</summary>
/// <param name="Agent">The agent implementation.</param>
/// <param name="Grants">The maximum scopes and roles for this registration.</param>
/// <param name="Tools">The allowed name and version pairs.</param>
public sealed record AgentRegistration(IAgent Agent, AgentGrants Grants, IReadOnlyList<AgentToolBinding> Tools)
{
    /// <summary>Gets the agent implementation.</summary>
    public IAgent Agent { get; } = Agent ?? throw new ArgumentNullException(nameof(Agent));

    /// <summary>Gets the maximum scopes and roles.</summary>
    public AgentGrants Grants { get; } = Grants ?? throw new ArgumentNullException(nameof(Grants));

    /// <summary>Gets the allowed name and version pairs.</summary>
    public IReadOnlyList<AgentToolBinding> Tools { get; } = SnapshotBindings(Tools);

    private static IReadOnlyList<AgentToolBinding> SnapshotBindings(IReadOnlyList<AgentToolBinding> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var snapshot = tools.ToArray();
        if (snapshot.Any(binding => binding is null) || snapshot.Distinct().Count() != snapshot.Length)
        {
            throw new ArgumentException("Tool bindings must be non-null and unique by name and version.", nameof(tools));
        }

        return Array.AsReadOnly(snapshot);
    }
}
