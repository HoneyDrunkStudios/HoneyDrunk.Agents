namespace HoneyDrunk.Agents.Operations.Contract;

/// <summary>One finished run. It carries an id, a status name and times only.</summary>
/// <param name="Id">The run id: 32 hexadecimal digits.</param>
/// <param name="Status">One of the seven v1 statuses.</param>
/// <param name="StartedAt">When the run started.</param>
/// <param name="CompletedAt">When the run completed; never before <paramref name="StartedAt"/>.</param>
public sealed record RunSummary(string Id, string Status, DateTimeOffset StartedAt, DateTimeOffset CompletedAt);
