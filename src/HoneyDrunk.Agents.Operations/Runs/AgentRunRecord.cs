namespace HoneyDrunk.Agents.Operations.Runs;

/// <summary>One agent execution, as the run journal keeps it. It carries ids, times and a status name only.</summary>
/// <param name="RunId">The execution id: 32 hexadecimal digits.</param>
/// <param name="AgentId">The agent id.</param>
/// <param name="StartedAt">When the run started.</param>
/// <param name="CompletedAt">When the run completed; null while it is running.</param>
/// <param name="Status">One of <see cref="AgentRunStatus.All"/>; null while the run is running.</param>
public sealed record AgentRunRecord(string RunId, string AgentId, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, string? Status);
