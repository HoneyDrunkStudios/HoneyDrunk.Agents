namespace HoneyDrunk.Agents.Abstractions;

/// <summary>Host-selected restrictions for one execution.</summary>
/// <param name="Memory">The requested memory binding.</param>
/// <param name="NarrowGrantsTo">The optional scope and role subset for this execution.</param>
public sealed record AgentExecutionOptions(AgentMemoryBinding Memory, AgentGrants? NarrowGrantsTo = null)
{
    /// <summary>Gets the requested memory binding.</summary>
    public AgentMemoryBinding Memory { get; } = Memory ?? throw new ArgumentNullException(nameof(Memory));
}
