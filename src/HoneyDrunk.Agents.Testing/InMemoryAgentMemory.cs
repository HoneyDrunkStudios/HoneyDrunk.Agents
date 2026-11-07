using HoneyDrunk.Agents.Abstractions;
using System.Globalization;

namespace HoneyDrunk.Agents.Testing;

/// <summary>A list-backed agent memory. Every entry is a home entry; recall is newest first.</summary>
/// <param name="timeProvider">The clock that stamps entries; the system clock by default.</param>
public sealed class InMemoryAgentMemory(TimeProvider? timeProvider = null) : IAgentMemory
{
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;
    private readonly List<AgentMemoryItem> items = [];

    /// <summary>Gets every remembered entry, oldest first.</summary>
    public IReadOnlyList<AgentMemoryItem> Items
    {
        get
        {
            lock (this.items)
            {
                return [.. this.items];
            }
        }
    }

    /// <inheritdoc />
    public Task<AgentMemoryItem> RememberAsync(AgentMemoryNote note, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(note);
        lock (this.items)
        {
            var item = new AgentMemoryItem(
                (this.items.Count + 1).ToString(CultureInfo.InvariantCulture),
                note.Kind,
                note.Content,
                note.Tags,
                this.timeProvider.GetUtcNow(),
                true);
            this.items.Add(item);
            return Task.FromResult(item);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AgentMemoryItem>> RecallAsync(AgentRecall recall, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recall);
        lock (this.items)
        {
            IReadOnlyList<AgentMemoryItem> found = this.items
                .Where(item => recall.Kind is null || string.Equals(item.Kind, recall.Kind, StringComparison.Ordinal))
                .Where(item => recall.AllTags is null || recall.AllTags.All(tag => item.Tags.Contains(tag, StringComparer.Ordinal)))
                .Reverse()
                .Take(recall.Limit)
                .ToArray();
            return Task.FromResult(found);
        }
    }
}
