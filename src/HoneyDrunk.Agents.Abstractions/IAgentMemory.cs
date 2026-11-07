namespace HoneyDrunk.Agents.Abstractions;

/// <summary>Reads and writes memory within the scopes of an execution.</summary>
public interface IAgentMemory
{
    /// <summary>Writes an entry to the execution's home scope.</summary>
    /// <param name="note">The entry to write.</param>
    /// <param name="cancellationToken">The write cancellation token.</param>
    /// <returns>The written entry.</returns>
    Task<AgentMemoryItem> RememberAsync(AgentMemoryNote note, CancellationToken cancellationToken = default);

    /// <summary>Reads recent entries from the scopes this execution can read.</summary>
    /// <param name="recall">The filters and result limit.</param>
    /// <param name="cancellationToken">The read cancellation token.</param>
    /// <returns>The matching entries.</returns>
    Task<IReadOnlyList<AgentMemoryItem>> RecallAsync(AgentRecall recall, CancellationToken cancellationToken = default);
}
