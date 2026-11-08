namespace HoneyDrunk.Agents;

/// <summary>
/// One lifecycle transition: <c>register</c>, <c>initialize</c>, <c>execute</c>, <c>complete</c> or
/// <c>decommission</c>. It carries ids and an outcome name only.
/// </summary>
/// <param name="AgentId">The agent id.</param>
/// <param name="ExecutionId">The execution id, for <c>execute</c> and <c>complete</c>.</param>
/// <param name="Transition">The transition name.</param>
/// <param name="At">When it happened.</param>
/// <param name="Outcome">For <c>complete</c>: the outcome name, or <c>propagated</c> for a rethrown stop exception.</param>
public sealed record AgentLifecycleTransition(string AgentId, string? ExecutionId, string Transition, DateTimeOffset At, string? Outcome);
