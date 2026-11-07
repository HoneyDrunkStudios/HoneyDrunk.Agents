namespace HoneyDrunk.Agents.Abstractions;

/// <summary>An execution's scoped memory. It writes to the home scope only and has no delete or summarize.</summary>
public interface IAgentMemory
{
    /// <summary>Remembers a note in the execution's home scope.</summary>
    /// <param name="note">The note.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The stored entry.</returns>
    Task<AgentMemoryItem> RememberAsync(AgentMemoryNote note, CancellationToken cancellationToken = default);

    /// <summary>Recalls entries the execution may read, newest first.</summary>
    /// <param name="recall">What to recall.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The entries.</returns>
    Task<IReadOnlyList<AgentMemoryItem>> RecallAsync(AgentRecall recall, CancellationToken cancellationToken = default);
}
