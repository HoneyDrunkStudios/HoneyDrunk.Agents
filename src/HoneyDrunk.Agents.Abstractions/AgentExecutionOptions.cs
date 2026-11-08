namespace HoneyDrunk.Agents.Abstractions;

/// <summary>Per-execution settings.</summary>
/// <param name="Memory">Where the execution remembers and recalls.</param>
/// <param name="NarrowGrantsTo">Narrows the agent's registered grants for this execution; never widens them.</param>
public sealed record AgentExecutionOptions(AgentMemoryBinding Memory, AgentGrants? NarrowGrantsTo = null)
{
    /// <summary>Gets the memory binding.</summary>
    public AgentMemoryBinding Memory { get; } = Memory ?? throw new ArgumentNullException(nameof(Memory));
}
