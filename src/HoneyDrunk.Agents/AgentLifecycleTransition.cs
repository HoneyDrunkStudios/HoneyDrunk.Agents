namespace HoneyDrunk.Agents;

/// <summary>Safe metadata about an agent lifecycle transition.</summary>
/// <param name="AgentId">The stable registration identifier.</param>
/// <param name="ExecutionId">The execution identifier, when this is a run transition.</param>
/// <param name="Transition">The lifecycle transition.</param>
/// <param name="At">The transition timestamp.</param>
/// <param name="Outcome">The optional execution outcome.</param>
public sealed record AgentLifecycleTransition(string AgentId, string? ExecutionId, string Transition, DateTimeOffset At, string? Outcome);
