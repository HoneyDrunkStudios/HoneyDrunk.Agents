using HoneyDrunk.Agents.Abstractions;

namespace HoneyDrunk.Agents;

/// <summary>One registered agent, as the directory lists it.</summary>
/// <param name="AgentId">The agent id.</param>
/// <param name="Name">The descriptor's name, as captured at registration.</param>
/// <param name="Phase">The agent's phase when the list was taken.</param>
public sealed record AgentListing(string AgentId, string Name, AgentLifecyclePhase Phase);
