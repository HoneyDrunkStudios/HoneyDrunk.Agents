namespace HoneyDrunk.Agents.Operations;

/// <summary>
/// The authorization policies the operations endpoints require. The host defines them; this package chooses no
/// authentication scheme (ADR-0110).
/// </summary>
public static class AgentOperationsPolicies
{
    /// <summary>The policy every read endpoint requires.</summary>
    public const string Read = "agent-operations:read";
}
