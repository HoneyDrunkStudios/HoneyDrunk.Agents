namespace HoneyDrunk.Agents.Operations.Contract;

/// <summary>A manual pause on an agent.</summary>
/// <param name="PausedBy">Who paused it.</param>
/// <param name="Reason">Why, in a short sentence.</param>
/// <param name="Since">When the pause began.</param>
public sealed record PauseView(string PausedBy, string Reason, DateTimeOffset Since);
