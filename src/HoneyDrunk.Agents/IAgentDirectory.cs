namespace HoneyDrunk.Agents;

/// <summary>A read-only view of the agents registered in this host.</summary>
public interface IAgentDirectory
{
    /// <summary>Lists the registered agents, decommissioned ones included, ordered by agent id (ordinal).</summary>
    /// <returns>A snapshot; later registrations and phase changes do not alter it.</returns>
    IReadOnlyList<AgentListing> List();
}
