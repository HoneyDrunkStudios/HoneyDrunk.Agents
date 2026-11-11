namespace HoneyDrunk.Agents.Operations.Contract;

/// <summary>An agent's spend in one budget window.</summary>
/// <param name="Window">The window key, for example <c>2026-11</c>.</param>
/// <param name="Spent">The amount spent so far in the window.</param>
/// <param name="Limit">The limit; 0 means there is none.</param>
/// <param name="Remaining">What is left under the limit; null when there is no limit.</param>
public sealed record BudgetView(string Window, decimal Spent, decimal Limit, decimal? Remaining);
