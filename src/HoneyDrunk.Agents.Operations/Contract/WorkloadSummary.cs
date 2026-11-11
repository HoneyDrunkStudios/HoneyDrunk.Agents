namespace HoneyDrunk.Agents.Operations.Contract;

/// <summary>One agent in the workloads list.</summary>
/// <param name="Id">The agent id.</param>
/// <param name="DisplayName">The agent's name: 1 to 64 characters, none of them control characters.</param>
/// <param name="Environment">The host's environment identifier.</param>
/// <param name="Runs">The newest finished runs, newest first, at most 10.</param>
public sealed record WorkloadSummary(string Id, string DisplayName, string Environment, IReadOnlyList<RunSummary> Runs);
